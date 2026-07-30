#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// History navigation: a visual timeline of the active document's
    /// undo/redo history. One row per undo step — future (redo) entries on
    /// top, the current state, then past entries down to the original —
    /// each showing a summary of the edit and its line. Selecting a row
    /// previews the document EXACTLY as it looked at that point (changed
    /// line highlighted); Restore walks Undo/Redo to return there (itself
    /// still undoable), and a snapshot can be opened as a new tab or copied.
    /// Modeless, and it follows the active tab.
    /// </summary>
    public class HistoryWindow : EditorWindow
    {
        static HistoryWindow _instance;

        TextEditorWindow _owner;
        ScrollView _rowScroll;
        ScrollView _previewScroll;
        Label _preview;
        Label _header;
        Button _restore, _openTab, _copy;
        readonly List<Label> _rowLabels = new List<Label>();

        List<CodeView.HistoryRow> _rows = new List<CodeView.HistoryRow>();
        int _sel = -1;
        string _selText;    // reconstructed state of the selected row

        // Rebuild detection.
        object _lastDoc;
        int _lastUndo = -1, _lastRedo = -1, _lastVersion = -1;

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
            _instance.BuildUI();
            _instance.Focus();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

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
            split.Add(_rowScroll);

            _previewScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1 } };
            _preview = new Label { enableRichText = true };
            _preview.AddToClassList("code-line");
            _preview.style.whiteSpace = WhiteSpace.Pre;
            _preview.style.paddingLeft = 6;
            _previewScroll.Add(_preview);
            split.Add(_previewScroll);
            root.Add(split);

            var buttons = new VisualElement
            { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd,
                        marginTop = 6, marginBottom = 6, flexShrink = 0 } };
            _restore = new Button(RestoreSelected) { text = L10n.Tr("Restore to This Point") };
            _openTab = new Button(OpenSnapshotTab) { text = L10n.Tr("Open as New Tab") };
            _copy = new Button(() =>
            {
                if (_selText != null) EditorGUIUtility.systemCopyBuffer = _selText;
            }) { text = L10n.Tr("Copy to Clipboard") };
            buttons.Add(_restore);
            buttons.Add(_openTab);
            buttons.Add(_copy);
            root.Add(buttons);

            root.schedule.Execute(Poll).Every(400);
            _lastUndo = -1; // force first rebuild
            Poll();
        }

        void Poll()
        {
            if (_owner == null) { Close(); return; }
            var view = _owner.HistoryView;
            object doc = _owner.HistoryDocToken;
            if (view == null || doc == null)
            {
                _header.text = L10n.Tr("No edits recorded for this document yet.");
                return;
            }
            if (doc == _lastDoc && view.UndoDepth == _lastUndo &&
                view.RedoDepth == _lastRedo && view.DocVersion == _lastVersion) return;
            _lastDoc = doc;
            _lastUndo = view.UndoDepth;
            _lastRedo = view.RedoDepth;
            _lastVersion = view.DocVersion;
            Rebuild();
        }

        void Rebuild()
        {
            var view = _owner.HistoryView;
            _rows = view.HistoryRows();
            _header.text = _rows.Count == 0
                ? L10n.Tr("No edits recorded for this document yet.")
                : _owner.HistoryDocName + "   —   " +
                  string.Format(L10n.Tr("{0} past / {1} future edit steps"), view.UndoDepth, view.RedoDepth);

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
                    l.RegisterCallback<PointerDownEvent>(e => { Select(idx); e.StopPropagation(); });
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
            if (idx < 0 || idx >= _rows.Count) { _preview.text = ""; _selText = null; return; }

            var r = _rows[idx];
            var view = _owner.HistoryView;
            _selText = view.HistoryStateAt(r.UndoSteps, r.RedoSteps, out int changeLine);
            if (r.IsCurrent) changeLine = r.Line;

            // Highlight the changed line; keep literal '<' literal elsewhere.
            static string Np(string s) => s.IndexOf('<') >= 0 ? "<noparse>" + s + "</noparse>" : s;
            var lines = _selText.Split('\n');
            var sb = new System.Text.StringBuilder(_selText.Length + 64);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                if (i == changeLine) sb.Append("<color=#FFD75A>").Append(Np(lines[i])).Append("</color>");
                else sb.Append(Np(lines[i]));
            }
            _preview.text = sb.ToString();

            _restore.SetEnabled(!r.IsCurrent);
            // Scroll the preview to the changed line once it has a layout.
            int cl = changeLine, total = lines.Length;
            _previewScroll.schedule.Execute(() =>
            {
                float h = _preview.layout.height;
                if (float.IsNaN(h) || h <= 0 || total == 0) return;
                float y = h * cl / total - _previewScroll.contentViewport.layout.height * 0.5f;
                _previewScroll.verticalScroller.value =
                    Mathf.Clamp(y, _previewScroll.verticalScroller.lowValue, _previewScroll.verticalScroller.highValue);
            });
        }

        void RestoreSelected()
        {
            if (_sel < 0 || _sel >= _rows.Count || _owner == null) return;
            var r = _rows[_sel];
            if (r.IsCurrent) return;
            _owner.HistoryStep(r.UndoSteps, r.RedoSteps);
            Poll(); // reflect the new stacks immediately
        }

        void OpenSnapshotTab()
        {
            if (_selText == null || _owner == null || _sel < 0) return;
            var r = _rows[_sel];
            string suffix = r.IsCurrent ? "" : r.RedoSteps > 0 ? " (+" + r.RedoSteps + ")" : " (−" + r.UndoSteps + ")";
            _owner.HistoryOpenSnapshot(
                "History " + _owner.HistoryDocName + suffix, _selText);
        }
    }
}
#endif
