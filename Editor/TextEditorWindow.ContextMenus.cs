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
    // Right-click context menus for the document area and the Settings tab.
    // TRUE context sensitivity: items that do not apply to the current tab
    // are HIDDEN, not disabled — a virtual tab (blame, revision snapshots,
    // release notes) gets a read-oriented menu without edit/file/git
    // entries, a file-backed tab gets the full set, and the Settings tab
    // gets tab navigation.
    public partial class TextEditorWindow
    {
        /// <summary>Right-click context menu inside the document area.</summary>
        void OnCodeContextMenu(MouseUpEvent e)
        {
            if (e.button != 1 || !CanEditDoc) return;
            if (HasDocs && Active.GameMode) { e.StopPropagation(); return; } // games own the mouse
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

        /// <summary>The Tabs submenu (jump to any open tab) — shared with
        /// the Settings tab's context menu.</summary>
        void AddTabsSubmenu(GenericMenu m)
        {
            for (int i = 0; i < _docs.Count; i++)
            {
                int idx = i;
                string name = (_docs[i].IsDirty ? "*" : "") + _docs[i].DisplayName.Replace('/', '∕');
                m.AddItem(new GUIContent(L10n.Tr("Tabs") + $"/{i + 1}  {name}"),
                    i == _active, () => SwitchTo(idx));
            }
        }

        GenericMenu BuildCodeContextMenu(int line, int col)
        {
            string query = _code.SelectedTextPublic;
            if (query != null && (query.Contains("\n") || query.Length > 200)) query = null;
            if (query == null) query = _code.WordAt(line, col, select: false);

            // A virtual tab (blame output, revision snapshot, release notes…)
            // is read-oriented: no edit, file, or git entries.
            bool isVirtual = !string.IsNullOrEmpty(Active.VirtualName);
            bool isCs = SemanticContextPath != null &&
                (Active.VirtualCSharp || (Active.HasFile &&
                    Active.FilePath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase)));
            var m = new GenericMenu();

            // --- Tabs submenu (very top): jump to any open tab ---
            AddTabsSubmenu(m);
            m.AddSeparator("");

            // --- Selection / symbol commands ---
            if (isCs)
            {
                m.AddItem(new GUIContent(WithSc("Go to Definition", Dsp("goto-definition"))), false,
                    () => NavigateToDefinition(line, col));
                if (!isVirtual)
                    m.AddItem(new GUIContent(WithSc("Rename Symbol...", Dsp("rename-symbol"))), false, RenameSymbolAtCaret);
                m.AddItem(new GUIContent(WithSc("Find All References", Dsp("find-references"))), false, FindAllReferences);
                m.AddItem(new GUIContent(L10n.Tr("Inspect Symbol...")), false,
                    () => InspectSymbolAtCaret(line, col));
            }
            if (query != null)
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Find Occurrences of '{0}'"), Truncate(query, 24))), false,
                    () => FindReplaceWindow.OpenWithQuery(this, query));

            // --- Spell check: add the flagged word under the cursor ---
            string misspelled = _code.MisspelledWordAt(line, col);
            if (misspelled != null)
            {
                m.AddSeparator("");
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Add '{0}' to User Dictionary"), misspelled)),
                    false, () => { SpellChecker.Add(misspelled, project: false); _code.RespellNow(); });
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Add '{0}' to Project Dictionary"), misspelled)),
                    false, () => { SpellChecker.Add(misspelled, project: true); _code.RespellNow(); });
            }

            // --- Send to AI (only when Unity AI Assistant is installed) ---
            if (UnityAiBridge.Available)
            {
                m.AddSeparator("");
                if (_code.HasSelectionPublic)
                    m.AddItem(new GUIContent(L10n.Tr("Ask Unity AI About Selection...")), false, AskUnityAiSelection);
                m.AddItem(new GUIContent(L10n.Tr("Ask Unity AI About This File...")), false, AskUnityAiDocument);
            }
            m.AddSeparator("");

            // --- Clipboard ---
            // Empty selection = whole-line cut/copy, so always applicable.
            if (!isVirtual)
                m.AddItem(new GUIContent(WithSc("Cut", Dsp("cut"))), false, _code.Cut);
            m.AddItem(new GUIContent(WithSc("Copy", Dsp("copy"))), false, _code.Copy);
            if (!isVirtual && !string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer))
                m.AddItem(new GUIContent(WithSc("Paste", Dsp("paste"))), false, _code.Paste);
            m.AddItem(new GUIContent(WithSc("Select All", Dsp("select-all"))), false, _code.SelectAll);
            if (!isVirtual && (_code.CanUndo || _code.CanRedo))
            {
                m.AddSeparator("");
                if (_code.CanUndo)
                    m.AddItem(new GUIContent(WithSc("Undo", Dsp("undo"))), false, _code.Undo);
                if (_code.CanRedo)
                    m.AddItem(new GUIContent(WithSc("Redo", Dsp("redo"))), false, _code.Redo);
            }
            m.AddSeparator("");

            // --- File ---
            if (!isVirtual)
                m.AddItem(new GUIContent(WithSc("Save", Dsp("save"))), false, () => SaveFile(false));
            m.AddItem(new GUIContent(L10n.Tr("Save As...")), false, () => SaveFile(true));
            m.AddItem(new GUIContent(WithSc("Close Tab", Dsp("close-tab"))), false, () => CloseTab(_active));
            if (Active.HasFile)
                m.AddItem(new GUIContent(L10n.Tr("Show in File Explorer")), false,
                    () => EditorUtility.RevealInFinder(Path.GetFullPath(Active.FilePath)));
            m.AddSeparator("");

            // --- Git (the same submenu as Tools → Git): file-backed only ---
            if (Active.HasFile)
            {
                FillGitMenu(m);
                m.AddSeparator("");
            }

            m.AddItem(new GUIContent(WithSc("Find...", Dsp("find"))), false, () => FindReplaceWindow.Open(this, FindReplaceWindow.FrTab.Find));
            if (!isVirtual)
                m.AddItem(new GUIContent(WithSc("Replace...", Dsp("replace"))), false, () => FindReplaceWindow.Open(this, FindReplaceWindow.FrTab.Replace));
            m.AddItem(new GUIContent(WithSc("Goto Line...", Dsp("goto-line"))), false, GotoLineCommand);

            // --- Language-specific ---
            if (isCs && !isVirtual)
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

        /// <summary>Shared entries appended to the rendered-Markdown view's
        /// context menu — a rendered .md is still a document (usually a file
        /// in the repo), so tab navigation, file ops, and the Git submenu
        /// stay reachable there; the view itself only contributes its
        /// clipboard/lock items. Virtual rendered docs (release notes) get
        /// no file/git entries, matching the source-view tailoring.</summary>
        internal void ExtendMdContextMenu(GenericMenu m)
        {
            if (!HasDocs || Active.IsSettings) return;
            if (m.GetItemCount() > 0) m.AddSeparator("");
            AddTabsSubmenu(m);
            m.AddSeparator("");
            if (ActiveIsMarkdown && Active.MdRendered)
                m.AddItem(new GUIContent(L10n.Tr("Switch to Markdown Source")), false, ToggleMdMode);
            m.AddItem(new GUIContent(L10n.Tr("Save As...")), false, () => SaveFile(true));
            m.AddItem(new GUIContent(WithSc("Close Tab", Dsp("close-tab"))), false, () => CloseTab(_active));
            if (Active.HasFile)
            {
                m.AddItem(new GUIContent(L10n.Tr("Show in File Explorer")), false,
                    () => EditorUtility.RevealInFinder(Path.GetFullPath(Active.FilePath)));
                m.AddSeparator("");
                FillGitMenu(m);
            }
        }

        /// <summary>The Settings tab's context menu: tab navigation and
        /// closing — the only document actions that apply to it.</summary>
        internal void OnSettingsContextMenu(MouseUpEvent e)
        {
            if (e.button != 1) return;
            e.StopPropagation();
            var m = new GenericMenu();
            AddTabsSubmenu(m);
            m.AddSeparator("");
            m.AddItem(new GUIContent(WithSc("Close Tab", Dsp("close-tab"))), false, () => CloseTab(_active));
            m.ShowAsContext();
        }

        static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
#endif
