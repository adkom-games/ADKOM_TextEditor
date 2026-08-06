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
    /// from it.
    ///
    /// Revision CONTENTS live in a sliding window around the slider
    /// position: while the slider rests (~400 ms), the window's missing
    /// revisions are fetched nearest-first in the background; contents that
    /// fall outside the window are dropped, so memory stays bounded at
    /// ~window-size revisions no matter how long the history is. Landing on
    /// a revision the prefetch hasn't reached fetches it immediately — the
    /// only cost of outrunning the window is that brief wait. The window
    /// size is a per-window override (the field beside the slider) seeded
    /// from the Options default.
    ///
    /// Multi-instance by design: each invocation opens its own window, so
    /// several files can be compared side by side. Copy to Tab pushes the
    /// shown revision back into the file's tab (undoable while that tab is
    /// active).
    /// </summary>
    public class GitTimeLapseWindow : EditorWindow
    {
        // Same tints the Diff window uses, so "added" reads identically.
        static readonly Color AddedBg = new Color(0.25f, 0.62f, 0.25f, 0.16f);
        static readonly Color ChangedBg = new Color(0.78f, 0.65f, 0.20f, 0.13f);

        const int PauseMs = 400; // slider must rest this long before the window prefetches

        [SerializeField] TextEditorWindow _owner; // an object ref survives reloads
        [SerializeField] string _filePath;
        [SerializeField] string _displayName;
        [SerializeField] string _tabText; // the buffer at open time — the slider's right end
        [SerializeField] int _windowSize; // per-window override, seeded from EditorConfig

        Label _header;
        SliderInt _slider;
        Button _prevBtn, _nextBtn;
        IntegerField _sizeField;
        CodeView _view;   // the REAL editor view, read-only — syntax colors,
                          // line numbers, wrap, selection/copy all intact
        Button _copyToTab;

        // Revision data: _hist[0] = oldest … _hist[N-1] = newest commit;
        // slider position N is the tab buffer itself. All rebuilt after a
        // domain reload — a mid-flight fetch must not appear finished.
        [System.NonSerialized] List<GitService.LogEntry> _hist;
        [System.NonSerialized] string[] _cache;   // only in-window slots are non-null
        [System.NonSerialized] bool _loading;
        [System.NonSerialized] int _pending = -1; // slider position waiting for its content
        [System.NonSerialized] int _fetchGen;     // bumped on every move/resize; stale fetch loops stop
        [System.NonSerialized] System.Threading.SynchronizationContext _ctx;
        IVisualElementScheduledItem _pauseTimer;
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
            w._windowSize = EditorConfig.TimeLapseWindowSize;
            w.Show();
            w.BuildUI();
        }

        static string Normalize(string s) =>
            (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

        /// <summary>Reload survivor: rebind to a main window and rebuild —
        /// the file path, captured tab text and window-size override are
        /// serialized; the history and content cache are refetched. Deferred
        /// so the normal Open() path (which builds immediately) wins when
        /// both run.</summary>
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
            if (_windowSize <= 0) _windowSize = EditorConfig.TimeLapseWindowSize;

            _header = new Label
            {
                text = L10n.Tr("Loading git history…"),
                tooltip = L10n.Tr("The revision shown below, and how the current tab differs from it."),
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 }
            };
            root.Add(_header);

            var sliderRow = new VisualElement
            { style = { flexDirection = FlexDirection.Row, marginBottom = 2, flexShrink = 0 } };
            sliderRow.Add(new Label(L10n.Tr("Version"))
            {
                tooltip = L10n.Tr("Drag through the file's git history — oldest commit on the left, the current tab contents on the right."),
                style = { alignSelf = Align.Center, flexShrink = 0, marginRight = 4 }
            });
            _slider = new SliderInt(0, 0)
            {
                tooltip = L10n.Tr("Drag through the file's git history — oldest commit on the left, the current tab contents on the right."),
                style = { flexGrow = 1 }
            };
            _slider.RegisterValueChangedCallback(e => ShowPosition(e.newValue));
            _slider.SetEnabled(false);
            sliderRow.Add(_slider);
            _prevBtn = new Button(() => _slider.value = Mathf.Max(0, _slider.value - 1))
            {
                text = "◀",
                tooltip = L10n.Tr("Step to the previous (older) revision."),
                style = { flexShrink = 0, marginLeft = 6 }
            };
            _prevBtn.SetEnabled(false);
            sliderRow.Add(_prevBtn);
            _nextBtn = new Button(() => _slider.value = Mathf.Min(_slider.highValue, _slider.value + 1))
            {
                text = "▶",
                tooltip = L10n.Tr("Step to the next (newer) revision."),
                style = { flexShrink = 0 }
            };
            _nextBtn.SetEnabled(false);
            sliderRow.Add(_nextBtn);
            _sizeField = new IntegerField(L10n.Tr("Window Size"))
            {
                value = _windowSize,
                tooltip = L10n.Tr("How many revisions stay fetched around the slider position; contents outside this sliding window are dropped and re-fetched when needed (5-500). Overrides the Options default for this window only."),
                style = { flexShrink = 0, marginLeft = 8, width = 190 }
            };
            // The stock field label reserves Unity's inspector label width,
            // leaving a gap — shrink it to its text so it hugs the input.
            _sizeField.labelElement.style.minWidth = StyleKeyword.Auto;
            _sizeField.labelElement.style.width = StyleKeyword.Auto;
            _sizeField.RegisterValueChangedCallback(e =>
            {
                _windowSize = Mathf.Clamp(e.newValue, 5, 500);
                _sizeField.SetValueWithoutNotify(_windowSize); // clamp echo
                _fetchGen++;
                EvictOutsideWindow();
                _pauseTimer?.ExecuteLater(PauseMs);
            });
            sliderRow.Add(_sizeField);
            root.Add(sliderRow);

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
            // unless the read-only view or a field has the event, which keep
            // their own key handling.
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);

            // The pause timer drives the sliding-window prefetch: (re)armed
            // on every slider move, it fires once after the slider has
            // rested. Created paused; ExecuteLater() re-arms it.
            _pauseTimer = root.schedule.Execute(PrefetchWindow);
            _pauseTimer.Pause();

            // The buffer shows immediately; the history list and revision
            // contents stream in once the functional main thread context
            // exists (see AteMainCtx for why not Current).
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
        /// neighbor (the highlight baseline) are cached.</summary>
        bool Ready(int pos) =>
            (pos >= PosCount() || _cache[pos] != null) &&
            (pos == 0 || ContentAt(pos - 1) != null);

        /// <summary>The sliding window's revision-index bounds around a
        /// slider position (the tab-buffer position anchors at the newest
        /// revision).</summary>
        void WindowBounds(int pos, out int lo, out int hi)
        {
            int n = PosCount();
            if (n == 0) { lo = 0; hi = -1; return; }
            int half = _windowSize / 2;
            lo = Mathf.Clamp(pos - half, 0, n - 1);
            hi = Mathf.Clamp(pos + half, 0, n - 1);
        }

        /// <summary>Drops cached contents outside the current window — the
        /// half of "sliding": memory stays bounded at ~window size.</summary>
        void EvictOutsideWindow()
        {
            if (_cache == null || _shown < 0) return;
            WindowBounds(_shown, out int lo, out int hi);
            for (int i = 0; i < _cache.Length; i++)
                if ((i < lo || i > hi) && _cache[i] != null)
                    _cache[i] = null;
        }

        void StartLoad()
        {
            if (_loading) return;
            _loading = true;
            string path = _filePath;
            var ctx = _ctx;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<GitService.LogEntry> hist = null;
                try { if (GitService.GitAvailable) hist = GitService.FileHistory(path, int.MaxValue); }
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
                    _prevBtn.SetEnabled(true);
                    _nextBtn.SetEnabled(true);
                    _shown = hist.Count; // the shown tab buffer is now the LAST position
                    UpdateHeader(_shown);
                    _pauseTimer?.ExecuteLater(PauseMs); // fill the newest window
                }, null);
            });
        }

        /// <summary>Pause-time prefetch: fetches the window's missing
        /// revisions nearest-to-the-slider first, expanding outward. Runs
        /// only after the slider has rested; any further movement (or a
        /// window-size change) bumps the generation and the loop stops.</summary>
        void PrefetchWindow()
        {
            if (_hist == null || _ctx == null) return;
            EvictOutsideWindow();
            WindowBounds(_shown, out int lo, out int hi);
            var missing = new List<int>();
            int center = Mathf.Clamp(_shown, lo, hi);
            for (int d = 0; d <= hi - lo; d++) // nearest-first, alternating outward
            {
                int a = center - d, b = center + d;
                if (a >= lo && _cache[a] == null) missing.Add(a);
                if (b != a && b <= hi && _cache[b] == null) missing.Add(b);
            }
            if (missing.Count > 0) FetchIndices(missing, _fetchGen);
        }

        /// <summary>Background fetch of revision contents in the given
        /// order. Results land in the cache only while they are still inside
        /// the current window; a stale generation stops the loop early (the
        /// slider moved on — a new prefetch or demand fetch has taken over).</summary>
        void FetchIndices(List<int> indices, int gen)
        {
            string path = _filePath;
            var ctx = _ctx;
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (int idx in indices)
                {
                    if (gen != _fetchGen) return; // benign race: worst case one extra fetch
                    if (_cache != null && _cache[idx] != null) continue;
                    string content = null;
                    try { content = GitService.ShowFileAt(path, _hist[idx].Hash); }
                    catch (System.Exception) { }
                    string norm = Normalize(content ?? "");
                    ctx.Post(_ =>
                    {
                        if (this == null || _view == null || _view.panel == null || _cache == null) return;
                        WindowBounds(_shown, out int lo, out int hi);
                        if (idx < lo || idx > hi) return; // slid away — don't resurrect evicted slots
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
            _fetchGen++; // outdate any running fetch loop
            _pauseTimer?.ExecuteLater(PauseMs); // re-arm the pause prefetch
            if (Ready(pos)) { _pending = -1; Render(pos); return; }

            // Outran the window: fetch just what this position needs, now.
            // The user pays this wait only when they didn't pause long
            // enough for the window prefetch to get here.
            _pending = pos;
            _header.text = L10n.Tr("Loading git history…");
            var need = new List<int>();
            int n = PosCount();
            if (pos < n && _cache[pos] == null) need.Add(pos);
            if (pos > 0 && pos - 1 < n && _cache[pos - 1] == null) need.Add(pos - 1);
            _shown = pos; // anchor the window here so the demand results are kept
            if (need.Count > 0) FetchIndices(need, _fetchGen);
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
            if (_sizeField != null && e.target is VisualElement fe &&
                (fe == _sizeField || _sizeField.Contains(fe)))
                return; // typing a number must not scrub the timeline
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
