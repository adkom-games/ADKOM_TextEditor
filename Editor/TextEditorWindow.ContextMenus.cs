#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Linq;

namespace ADKOM.TextEditor
{
    // Right-click context menu inside the document area.
    public partial class TextEditorWindow
    {
        /// <summary>Right-click context menu inside the document area:
        /// selection/symbol commands on top, then clipboard, file, and
        /// language-specific entries.</summary>
        void OnCodeContextMenu(MouseUpEvent e)
        {
            if (e.button != 1 || !CanEditDoc) return;
            e.StopPropagation();

            // UITK mouse events report panel-space positions, which is what
            // HitTest's WorldToLocal expects.
            _code.HitTestPublic(e.mousePosition, out int line, out int col);
            int clickIdx = _code.LineColToIndex(line, col);
            int selA = Mathf.Min(_code.cursorIndex, _code.selectIndex);
            int selB = Mathf.Max(_code.cursorIndex, _code.selectIndex);
            bool clickInSelection = _code.HasSelectionPublic && clickIdx >= selA && clickIdx <= selB;
            if (!clickInSelection) _code.GoToLine(line + 1, col + 1);

            BuildCodeContextMenu(line, col).DropDown(new Rect(e.mousePosition, Vector2.zero));
        }

        GenericMenu BuildCodeContextMenu(int line, int col)
        {
            string query = _code.SelectedTextPublic;
            if (query != null && (query.Contains("\n") || query.Length > 200)) query = null;
            if (query == null) query = _code.WordAt(line, col, select: false);

            bool isCs = SemanticContextPath != null &&
                (Active.VirtualCSharp || Active.FilePath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase));
            var m = new GenericMenu();

            // --- Selection / symbol commands ---
            if (isCs)
                m.AddItem(new GUIContent(WithSc("Go to Definition", Dsp("goto-definition"))), false,
                    () => NavigateToDefinition(line, col));
            if (query != null)
            {
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Find Occurrences of '{0}'"), Truncate(query, 24))), false,
                    () => FindReplaceWindow.OpenWithQuery(this, query, allTabs: false));
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Find in Tabs '{0}'"), Truncate(query, 24))), false,
                    () => FindReplaceWindow.OpenWithQuery(this, query, allTabs: true));
            }
            else if (!isCs)
                m.AddDisabledItem(new GUIContent(L10n.Tr("Find Occurrences")));
            m.AddSeparator("");

            // --- Clipboard ---
            bool hasSel = _code.HasSelectionPublic;
            AddOrDisable(m, WithSc("Cut", Dsp("cut")), hasSel, _code.Cut);
            AddOrDisable(m, WithSc("Copy", Dsp("copy")), hasSel, _code.Copy);
            AddOrDisable(m, WithSc("Paste", Dsp("paste")),
                !string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer), _code.Paste);
            m.AddItem(new GUIContent(WithSc("Select All", Dsp("select-all"))), false, _code.SelectAll);
            m.AddSeparator("");
            AddOrDisable(m, WithSc("Undo", Dsp("undo")), _code.CanUndo, _code.Undo);
            AddOrDisable(m, WithSc("Redo", Dsp("redo")), _code.CanRedo, _code.Redo);
            m.AddSeparator("");

            // --- File ---
            m.AddItem(new GUIContent(WithSc("Save", Dsp("save"))), false, () => SaveFile(false));
            m.AddItem(new GUIContent(L10n.Tr("Save As...")), false, () => SaveFile(true));
            m.AddItem(new GUIContent(WithSc("Close Tab", Dsp("close-tab"))), false, () => CloseTab(_active));
            AddOrDisable(m, L10n.Tr("Show in File Explorer"), Active.HasFile,
                () => EditorUtility.RevealInFinder(Path.GetFullPath(Active.FilePath)));
            m.AddSeparator("");
            m.AddItem(new GUIContent(WithSc("Find...", Dsp("find"))), false, () => FindReplaceWindow.Open(this, false, false));
            m.AddItem(new GUIContent(WithSc("Replace...", Dsp("replace"))), false, () => FindReplaceWindow.Open(this, true, false));
            m.AddItem(new GUIContent(WithSc("Goto Line...", Dsp("goto-line"))), false, GotoLineCommand);

            // --- Language-specific ---
            if (isCs)
            {
                m.AddSeparator("");
                m.AddItem(new GUIContent(WithSc("Toggle Comment", Dsp("toggle-comment"))), false, ToggleComment);
            }
            if (ActiveIsMarkdown)
            {
                m.AddSeparator("");
                m.AddItem(new GUIContent(Active.MdRendered
                    ? L10n.Tr("Switch to Markdown Source") : L10n.Tr("Switch to Rendered Markdown")), false, ToggleMdMode);
            }

            return m;
        }

        static void AddOrDisable(GenericMenu m, string label, bool enabled, System.Action a)
        {
            if (enabled) m.AddItem(new GUIContent(label), false, () => a());
            else m.AddDisabledItem(new GUIContent(label));
        }

        static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
#endif
