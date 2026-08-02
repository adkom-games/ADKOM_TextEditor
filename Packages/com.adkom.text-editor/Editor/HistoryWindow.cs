#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// History navigation: a visual timeline of a document's undo/redo
    /// history. One row per undo step — future (redo) entries on top, the
    /// current state, then past entries down to the original — each showing
    /// a summary of the edit and its line. Selecting a row previews the
    /// document EXACTLY as it looked at that point (changed line
    /// highlighted); Restore walks Undo/Redo to return there (itself still
    /// undoable for the active doc), and a snapshot can be opened as a new
    /// tab or copied. A tab bar above the preview picks WHICH document's
    /// history to browse — deliberately NOT synced with the editor's active
    /// tab, so histories can be inspected without switching the editor.
    /// </summary>
    public class HistoryWindow : EditorWindow
    {
        static HistoryWindow _instance;

        [SerializeField] TextEditorWindow _owner; // an object ref survives reloads
        ScrollView _rowScroll;
        ScrollView _tabScroll; // the window's OWN document tab bar
        CodeView _preview;   // the REAL editor view, read-only — full syntax
                             // coloring, line numbers, themes, selection/copy
        Label _header;
        Button _restore, _openTab, _copy;
        readonly List<Label> _rowLabels = new List<Label>();
        readonly List<Label> _tabLabels = new List<Label>();

        List<CodeView.HistoryRow> _rows = new List<CodeView.HistoryRow>();
        int _sel = -1;
        string _selText;    // reconstructed state of the selected row

        // The window's own document selection — independent of the editor's
        // active tab by design.
        TextDocument _doc;

        // Rebuild detection.
        object _lastDoc;
        int _lastUndo = -1, _lastRedo = -1, _lastStamp = -1;
        string _lastTabSig;

        public static void Open(TextEditorWindow owner)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<HistoryWindow>();
                _instance.titleContent = new GUIContent(L10n.Tr("Edit History"));
                _instance.minSize = new Vector2(560, 300);
                _instance.ShowUtility();
            }
            _instance._owner = owner;
            _instance._doc = owner.HistoryActiveDoc; // starting point only
            _instance.BuildUI();
            _instance.Focus();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void OnEnable() { if (_instance == null) _instance = this; }

        /// <summary>Reload survivor: rebind to the main window and rebuild —
        /// undo worlds are serialized with the documents now, so the timeline
        /// comes back intact. Deferred so the normal Open() path (which
        /// builds immediately) wins when both run.</summary>
        void CreateGUI()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                if (_rowScroll != null) return; // already built by Open()
                if (_owner == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
                    _owner = all.Length > 0 ? all[0] : null;
                }
                if (_owner == null) { Close(); return; }
                if (_doc == null) _doc = _owner.HistoryActiveDoc;
                BuildUI();
            });
        }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = 6;

            _header = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } };
            root.Add(_header);

            var split = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            _rowScroll = new ScrollView(ScrollViewMode.Vertical)
            { style = { width = 250, flexShrink = 0, borderRightWidth = 1,
                        borderRightColor = new Color(0.5f, 0.5f, 0.5f, 0.4f) } };
            _rowScroll.focusable = true; // arrow keys walk the timeline
            split.Add(_rowScroll);

            // Right pane: the window's own tab bar above the text buffer.
            var right = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            _tabScroll = new ScrollView(ScrollViewMode.Horizontal)
            { style = { flexShrink = 0, marginLeft = 2, borderBottomWidth = 1,
                        borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.4f) } };
            _tabScroll.contentContainer.style.flexDirection = FlexDirection.Row;
            right.Add(_tabScroll);

            _preview = new CodeView { readOnly = true, style = { flexGrow = 1 } };
            _owner.StyleAuxView(_preview, _doc);
            right.Add(_preview);
            split.Add(right);
            root.Add(split);

            var buttons = new VisualElement
            { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd,
                        marginTop = 6, marginBottom = 6, flexShrink = 0 } };
            _restore = new Button(RestoreSelected) { text = L10n.Tr("Restore to This Point"),
                tooltip = L10n.Tr("Rewind the document to this step. The restore is itself undoable.") };
            _openTab = new Button(OpenSnapshotTab) { text = L10n.Tr("Open as New Tab"),
                tooltip = L10n.Tr("Open this snapshot as a new untitled tab, leaving the document as is.") };
            _copy = new Button(() =>
            {
                if (_selText != null) EditorGUIUtility.systemCopyBuffer = _selText;
            }) { text = L10n.Tr("Copy to Clipboard"),
                tooltip = L10n.Tr("Copy this snapshot's full text to the clipboard.") };
            buttons.Add(_restore);
            buttons.Add(_openTab);
            buttons.Add(_copy);
            root.Add(buttons);

            // Keyboard: Up/Down (and Home/End) walk the timeline, Enter
            // restores the selected point — unless the read-only preview has
            // focus, which keeps its own caret navigation.
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);

            root.schedule.Execute(Poll).Every(400);
            _lastUndo = -1; // force first rebuild
            _lastTabSig = null;
            Poll();
            _rowScroll.schedule.Execute(() => _rowScroll.Focus());
        }

        void OnKey(KeyDownEvent e)
        {
            if (_preview != null && e.target is VisualElement ve &&
                (ve == _preview || _preview.Contains(ve)))
                return; // preview owns its keys while focused
            if (_rows.Count == 0) return;
            int target = e.keyCode switch
            {
                KeyCode.UpArrow => Mathf.Max(0, _sel - 1),
                KeyCode.DownArrow => Mathf.Min(_rows.Count - 1, _sel + 1),
                KeyCode.Home => 0,
                KeyCode.End => _rows.Count - 1,
                _ => -1
            };
            if (target >= 0)
            {
                Select(target);
                if (_sel >= 0 && _sel < _rowLabels.Count)
                    _rowScroll.ScrollTo(_rowLabels[_sel]);
                e.StopPropagation();
                return;
            }
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _sel >= 0)
            {
                RestoreSelected();
                e.StopPropagation();
            }
        }

        void Poll()
        {
            if (_owner == null) { Close(); return; }
            var docs = _owner.HistoryDocs();

            // Our doc may have been closed in the editor — fall back to its
            // active doc (or the first). We never follow the editor's tab
            // switches otherwise.
            if (_doc == null || !docs.Contains(_doc))
                _doc = _owner.HistoryActiveDoc ?? (docs.Count > 0 ? docs[0] : null);

            RefreshTabBar(docs);

            if (_doc == null)
            {
                _header.text = L10n.Tr("No edits recorded for this document yet.");
                _lastDoc = null;
                return;
            }
            var (undo, redo, stamp) = _owner.HistoryCountsFor(_doc);
            if (ReferenceEquals(_doc, _lastDoc) && undo == _lastUndo &&
                redo == _lastRedo && stamp == _lastStamp) return;
            _lastDoc = _doc;
            _lastUndo = undo;
            _lastRedo = redo;
            _lastStamp = stamp;
            Rebuild(undo, redo);
        }

        /// <summary>Mirrors the editor's document tabs (names + dirty stars),
        /// with OUR selection highlighted — clicking switches only which
        /// history this window shows, never the editor's active tab.</summary>
        void RefreshTabBar(List<TextDocument> docs)
        {
            // Cheap signature so we only rebuild the strip when it changed.
            var sb = new System.Text.StringBuilder();
            foreach (var d in docs)
                sb.Append(d.DisplayName).Append(d.IsDirty ? '*' : ' ').Append('');
            sb.Append('#').Append(docs.IndexOf(_doc));
            string sig = sb.ToString();
            if (sig == _lastTabSig) return;
            _lastTabSig = sig;

            for (int i = 0; i < docs.Count; i++)
            {
                if (i >= _tabLabels.Count)
                {
                    var l = new Label();
                    l.style.paddingLeft = l.style.paddingRight = 8;
                    l.style.paddingTop = 3;
                    l.style.paddingBottom = 3;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.style.borderRightWidth = 1;
                    l.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                    _tabScroll.Add(l);
                    _tabLabels.Add(l);
                }
                var lab = _tabLabels[i];
                var doc = docs[i];
                lab.text = doc.DisplayName + (doc.IsDirty ? " *" : "");
                bool selected = ReferenceEquals(doc, _doc);
                lab.style.backgroundColor = selected
                    ? new Color(0.25f, 0.42f, 0.6f, 0.45f) : Color.clear;
                lab.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
                lab.style.display = DisplayStyle.Flex;
                // Re-register cheaply: one shared handler reading the label's
                // userData avoids stale index captures across rebuilds.
                lab.userData = doc;
                if (!_wired.Contains(lab))
                {
                    _wired.Add(lab);
                    lab.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (lab.userData is TextDocument d) SelectDoc(d);
                        e.StopPropagation();
                    });
                }
            }
            for (int i = docs.Count; i < _tabLabels.Count; i++)
                _tabLabels[i].style.display = DisplayStyle.None;
        }

        readonly HashSet<Label> _wired = new HashSet<Label>();

        void SelectDoc(TextDocument d)
        {
            if (ReferenceEquals(d, _doc)) return;
            _doc = d;
            _lastUndo = -1;    // force rebuild
            _lastTabSig = null; // repaint selection highlight
            Poll();
            _rowScroll.Focus();
        }

        void Rebuild(int undo, int redo)
        {
            _rows = _owner.HistoryRowsFor(_doc);
            _header.text = _rows.Count == 0
                ? L10n.Tr("No edits recorded for this document yet.")
                : _doc.DisplayName + "   —   " +
                  string.Format(L10n.Tr("{0} past / {1} future edit steps"), undo, redo);

            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= _rowLabels.Count)
                {
                    var l = new Label();
                    l.AddToClassList("code-line");
                    l.style.paddingTop = 1;
                    l.style.paddingBottom = 1;
                    l.style.paddingLeft = 4;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    int idx = i;
                    l.RegisterCallback<PointerDownEvent>(e =>
                    { Select(idx); _rowScroll.Focus(); e.StopPropagation(); });
                    _rowScroll.Add(l);
                    _rowLabels.Add(l);
                }
                var r = _rows[i];
                string label =
                    r.IsOriginal ? L10n.Tr("Original (before recorded edits)")
                    : (r.IsCurrent ? "● " : r.RedoSteps > 0 ? "+" + r.RedoSteps + "  " : "−" + r.UndoSteps + "  ")
                      + r.Summary + "   Ln " + (r.Line + 1);
                _rowLabels[i].text = label;
                _rowLabels[i].style.opacity = r.RedoSteps > 0 ? 0.6f : 1f; // future = dimmed
                _rowLabels[i].style.display = DisplayStyle.Flex;
            }
            for (int i = _rows.Count; i < _rowLabels.Count; i++)
                _rowLabels[i].style.display = DisplayStyle.None;

            // Default to the current state's row.
            int cur = _rows.FindIndex(r => r.IsCurrent);
            Select(cur >= 0 ? cur : (_rows.Count > 0 ? 0 : -1));
        }

        void Select(int idx)
        {
            _sel = idx;
            for (int i = 0; i < _rows.Count && i < _rowLabels.Count; i++)
                _rowLabels[i].style.backgroundColor = i == _sel
                    ? new Color(0.25f, 0.42f, 0.6f, 0.45f) : Color.clear;
            if (idx < 0 || idx >= _rows.Count) { _preview.value = ""; _selText = null; return; }

            var r = _rows[idx];
            _selText = _owner.HistoryStateFor(_doc, r.UndoSteps, r.RedoSteps, out int changeLine);
            if (r.IsCurrent) changeLine = r.Line;

            _owner.StyleAuxView(_preview, _doc); // theme/language may have changed
            _preview.value = _selText;
            // Select the changed line — the editor's own selection highlight
            // marks it — and center it.
            _preview.GoToLine(changeLine + 1, 1);
            int lineStart = _preview.LineColToIndex(changeLine, 0);
            int lineEnd = changeLine + 1 < _selText.Split('\n').Length
                ? _preview.LineColToIndex(changeLine + 1, 0) : _selText.Length;
            _preview.selectIndex = lineStart;
            _preview.cursorIndex = lineEnd;
            _preview.CenterOnLine(changeLine);

            _restore.SetEnabled(!r.IsCurrent);
        }

        void RestoreSelected()
        {
            if (_sel < 0 || _sel >= _rows.Count || _owner == null) return;
            var r = _rows[_sel];
            if (r.IsCurrent) return;
            _owner.HistoryStepFor(_doc, r.UndoSteps, r.RedoSteps);
            Poll(); // reflect the new stacks immediately
        }

        void OpenSnapshotTab()
        {
            if (_selText == null || _owner == null || _sel < 0) return;
            var r = _rows[_sel];
            string suffix = r.IsCurrent ? "" : r.RedoSteps > 0 ? " (+" + r.RedoSteps + ")" : " (−" + r.UndoSteps + ")";
            _owner.HistoryOpenSnapshot(
                "History " + _doc.DisplayName + suffix, _selText, _doc);
        }
    }
}
#endif
