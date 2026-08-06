#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Git Time Lapse: replays one file's git history under a slider. The
    /// left end is the oldest commit that touched the file, the right end is
    /// the tab buffer as it was when the window opened; dragging steps the
    /// read-only view through every revision in between. Each step paints
    /// the lines that revision introduced (diff-style added/changed tints
    /// plus the git gutter bars — deletions leave only the red gutter stub),
    /// keeps whatever line was centered in view centered, and the header
    /// shows the revision's commit info plus how the current tab differs
    /// from it. Multi-instance by design: each invocation opens its own
    /// window, so several files can be compared side by side. Copy to Tab
    /// pushes the shown revision back into the file's tab (undoable while
    /// that tab is active).
    /// </summary>
    public class GitTimeLapseWindow : EditorWindow
    {
        // Same tints the Diff window uses, so "added" reads identically.
        static readonly Color AddedBg = new Color(0.25f, 0.62f, 0.25f, 0.16f);
        static readonly Color ChangedBg = new Color(0.78f, 0.65f, 0.20f, 0.13f);

        [SerializeField] TextEditorWindow _owner; // an object ref survives reloads
        [SerializeField] string _filePath;
        [SerializeField] string _displayName;
        [SerializeField] string _tabText; // the buffer at open time — the slider's right end

        Label _header;
        SliderInt _slider;
        CodeView _view;   // the REAL editor view, read-only — syntax colors,
                          // line numbers, wrap, selection/copy all intact
        Button _copyToTab;

        // Revision data: _hist[0] = oldest … _hist[N-1] = newest commit;
        // slider position N is the tab buffer itself. All rebuilt after a
        // domain reload — a mid-flight prefetch must not appear finished.
        [System.NonSerialized] List<GitService.LogEntry> _hist;
        [System.NonSerialized] string[] _cache;   // revision contents, filled newest-first
        [System.NonSerialized] bool _loading;
        [System.NonSerialized] int _pending = -1; // slider position waiting for its content
        [System.NonSerialized] System.Threading.SynchronizationContext _ctx;
        int _shown = -1;

        public static void Open(TextEditorWindow owner, string filePath, string displayName, string tabText)
        {
            var w = CreateInstance<GitTimeLapseWindow>();
            w.titleContent = new GUIContent(string.Format(L10n.Tr("Time Lapse {0}"), displayName));
            w.minSize = new Vector2(640, 360);
            w._owner = owner;
            w._filePath = filePath;
            w._displayName = displayName;
            w._tabText = Normalize(tabText);
            w.Show();
            w.BuildUI();
        }

        static string Normalize(string s) =>
            (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

        /// <summary>Reload survivor: rebind to a main window and rebuild —
        /// the file path and captured tab text are serialized, the history
        /// and content cache are refetched. Deferred so the normal Open()
        /// path (which builds immediately) wins when both run.</summary>
        void CreateGUI()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                if (_view != null) return; // already built by Open()
                if (_owner == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
                    _owner = all.Length > 0 ? all[0] : null;
                }
                if (_owner == null || string.IsNullOrEmpty(_filePath)) { Close(); return; }
                BuildUI();
            });
        }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = 6;

            _header = new Label
            {
                text = L10n.Tr("Loading git history…"),
                tooltip = L10n.Tr("The revision shown below, and how the current tab differs from it."),
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 }
            };
            root.Add(_header);

            _slider = new SliderInt(0, 0)
            {
                tooltip = L10n.Tr("Drag through the file's git history — oldest commit on the left, the current tab contents on the right."),
                style = { marginBottom = 2, flexShrink = 0 }
            };
            _slider.RegisterValueChangedCallback(e => ShowPosition(e.newValue));
            _slider.SetEnabled(false);
            root.Add(_slider);

            var frame = new VisualElement { style = { flexGrow = 1 } };
            AteViewStyle.Frame(frame);
            _view = new CodeView
            {
                readOnly = true,
                tooltip = L10n.Tr("Read-only view of the file at the selected revision. Text can be selected and copied."),
                style = { flexGrow = 1 }
            };
            StyleView();
            frame.Add(_view);
            root.Add(frame);

            var buttons = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd,
                          marginTop = 2, marginBottom = 6, flexShrink = 0 }
            };
            _copyToTab = new Button(CopyToTab)
            {
                text = L10n.Tr("Copy to Tab"),
                tooltip = L10n.Tr("Replace the file's tab contents with this revision. For the active tab this is undoable.")
            };
            _copyToTab.SetEnabled(false);
            buttons.Add(_copyToTab);
            root.Add(buttons);

            // Keyboard: Left/Right (and Home/End) step through revisions —
            // unless the read-only view or the slider itself has the event,
            // which keep their own key handling.
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);

            // The buffer shows immediately; history and revision contents
            // stream in from a background prefetch once the functional main
            // thread context exists (see AteMainCtx for why not Current).
            _shown = -1;
            Render(PosCount());
            _header.text = _displayName + "   —   " + L10n.Tr("Loading git history…");
            AteMainCtx.WhenReady(c =>
            {
                if (this == null) return;
                _ctx = c;
                StartLoad();
            });
        }

        void StyleView()
        {
            if (_owner == null) return;
            _owner.StyleAuxView(_view, _owner.FindDocByPath(_filePath));
            // The tab may have been closed since — classify by path anyway.
            _view.SetClassifier(SyntaxClassifiers.ForPath(_filePath));
            _view.wordWrap = _owner.WordWrapEnabled;
        }

        int PosCount() => _hist?.Count ?? 0; // slider position of the tab buffer

        string ContentAt(int pos) => pos >= PosCount() ? _tabText : _cache[pos];

        /// <summary>A position renders once its content AND its older
        /// neighbor (the highlight baseline) have been prefetched.</summary>
        bool Ready(int pos) =>
            (pos >= PosCount() || _cache[pos] != null) &&
            (pos == 0 || ContentAt(pos - 1) != null);

        void StartLoad()
        {
            if (_loading) return;
            _loading = true;
            string path = _filePath;
            var ctx = _ctx;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<GitService.LogEntry> hist = null;
                try { if (GitService.GitAvailable) hist = GitService.FileHistory(path, 400); }
                catch (System.Exception) { }
                ctx.Post(_ =>
                {
                    if (this == null || _view == null || _view.panel == null) return;
                    if (hist == null || hist.Count == 0)
                    {
                        _header.text = _displayName + "   —   " + L10n.Tr("No history available (not in a git repository?).");
                        return;
                    }
                    hist.Reverse(); // git log is newest-first; the slider wants oldest on the left
                    _hist = hist;
                    _cache = new string[hist.Count];
                    _slider.highValue = hist.Count;
                    _slider.SetValueWithoutNotify(hist.Count);
                    _slider.SetEnabled(true);
                    _shown = hist.Count; // the shown tab buffer is now the LAST position
                    UpdateHeader(_shown);
                    Prefetch(path, hist, ctx);
                }, null);
            });
        }

        /// <summary>Fetches every revision's content newest-first (the user
        /// starts at the right end), posting each into the cache; a slider
        /// position that outran the prefetch renders as soon as its content
        /// lands.</summary>
        void Prefetch(string path, List<GitService.LogEntry> hist, System.Threading.SynchronizationContext ctx)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = hist.Count - 1; i >= 0; i--)
                {
                    string content = null;
                    try { content = GitService.ShowFileAt(path, hist[i].Hash); }
                    catch (System.Exception) { }
                    string norm = Normalize(content ?? "");
                    int idx = i;
                    ctx.Post(_ =>
                    {
                        if (this == null || _view == null || _view.panel == null || _cache == null) return;
                        _cache[idx] = norm;
                        if (_pending >= 0 && Ready(_pending))
                        {
                            int p = _pending;
                            _pending = -1;
                            Render(p);
                        }
                    }, null);
                }
            });
        }

        void ShowPosition(int pos)
        {
            if (Ready(pos)) { _pending = -1; Render(pos); return; }
            _pending = pos;
            _header.text = L10n.Tr("Loading git history…");
        }

        void Render(int pos)
        {
            bool first = _shown < 0;
            _shown = pos;
            string cur = ContentAt(pos) ?? "";
            string prev = pos > 0 ? ContentAt(pos - 1) : null;

            // The viewer's place in the file survives the content swap: note
            // the centered line before, re-center on it after.
            int center = first ? -1 : _view.CenterLine();
            _view.value = cur;

            // Per-step highlights: what THIS revision changed vs its older
            // neighbor. Added/changed lines get the diff tints plus green /
            // amber gutter bars; deletions leave the red gutter stub below
            // the line they followed.
            var ov = new CodeView.ColorOverlay();
            Dictionary<int, GitService.LineMark> marks = null;
            if (prev != null)
            {
                marks = new Dictionary<int, GitService.LineMark>();
                var curLines = DiffEngine.SplitLines(cur);
                foreach (var b in DiffEngine.DiffLines(DiffEngine.SplitLines(prev), curLines))
                {
                    if (b.Op == DiffEngine.Op.Insert || b.Op == DiffEngine.Op.Replace)
                    {
                        bool added = b.Op == DiffEngine.Op.Insert;
                        for (int l = b.BStart; l < b.BStart + b.BCount && l < curLines.Length; l++)
                        {
                            ov.Set(l, 0, Mathf.Max(1, curLines[l].Length), null, added ? AddedBg : ChangedBg);
                            marks[l] = added ? GitService.LineMark.Added : GitService.LineMark.Modified;
                        }
                    }
                    else if (b.Op == DiffEngine.Op.Delete)
                    {
                        int at = Mathf.Max(0, b.BStart - 1);
                        if (!marks.ContainsKey(at)) marks[at] = GitService.LineMark.DeletedBelow;
                    }
                }
            }
            _view.AttachOverlay(ov);
            _view.ApplyGitMarks(marks);
            if (center >= 0) _view.CenterOnLine(center);

            UpdateHeader(pos);
            _copyToTab.SetEnabled(pos < PosCount() && _owner != null);
        }

        /// <summary>"revision info — how the tab differs from it": commit
        /// meta of the shown revision plus +added/−removed line counts of
        /// the tab buffer against it.</summary>
        void UpdateHeader(int pos)
        {
            if (pos >= PosCount())
            {
                _header.text = _displayName + "   —   " + L10n.Tr("Current tab contents");
                return;
            }
            string cur = ContentAt(pos);
            int add = 0, del = 0;
            if (cur != null)
            {
                foreach (var b in DiffEngine.DiffLines(DiffEngine.SplitLines(cur), DiffEngine.SplitLines(_tabText)))
                {
                    if (b.Op == DiffEngine.Op.Insert || b.Op == DiffEngine.Op.Replace) add += b.BCount;
                    if (b.Op == DiffEngine.Op.Delete || b.Op == DiffEngine.Op.Replace) del += b.ACount;
                }
            }
            var e = _hist[pos];
            string subj = e.Subject.Length > 60 ? e.Subject.Substring(0, 57) + "..." : e.Subject;
            _header.text = (pos + 1) + "/" + (PosCount() + 1) + "   " +
                e.Date + "  " + e.Hash + "  " + e.Author + " — " + subj + "   —   " +
                string.Format(L10n.Tr("Current tab vs this revision: +{0} −{1}"), add, del);
        }

        void OnKey(KeyDownEvent e)
        {
            if (_view != null && e.target is VisualElement ve &&
                (ve == _view || _view.Contains(ve)))
                return; // the view owns its keys while focused
            if (_slider != null && e.target is VisualElement se &&
                (se == _slider || _slider.Contains(se)))
                return; // the slider already steps on arrow keys itself
            if (_slider == null || !_slider.enabledSelf) return;
            int target = e.keyCode switch
            {
                KeyCode.LeftArrow => Mathf.Max(0, _slider.value - 1),
                KeyCode.RightArrow => Mathf.Min(_slider.highValue, _slider.value + 1),
                KeyCode.Home => 0,
                KeyCode.End => _slider.highValue,
                _ => -1
            };
            if (target >= 0)
            {
                _slider.value = target; // fires ShowPosition via the callback
                e.StopPropagation();
            }
        }

        void CopyToTab()
        {
            if (_owner == null || _shown < 0 || _shown >= PosCount()) return;
            string content = ContentAt(_shown);
            if (content == null) return;
            _owner.GitTimeLapseApply(_filePath, content);
        }
    }
}
#endif
