#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Context menus for the read-only CodeViews in auxiliary windows (Time
    /// Lapse, History preview): clipboard and navigation plus the Git
    /// submenu for the viewed file — PRUNED of whatever would recurse (a
    /// Time Lapse window's menu must not offer Time Lapse). The file/name
    /// come from providers because some hosts (History) can switch documents
    /// after the menu is attached.
    /// </summary>
    internal static class AuxTextMenu
    {
        [System.Flags]
        internal enum Prune { None = 0, TimeLapse = 1 }

        internal static void Attach(CodeView view, TextEditorWindow owner,
            System.Func<string> filePath, System.Func<string> displayName, Prune prune)
        {
            view.RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button != 1) return;
                e.StopPropagation();
                var m = Build(view, owner, filePath?.Invoke(), displayName?.Invoke(), prune, e.mousePosition);
                m.DropDown(new Rect(e.mousePosition, Vector2.zero));
            });
        }

        static GenericMenu Build(CodeView view, TextEditorWindow owner,
            string path, string name, Prune prune, Vector2 mousePos)
        {
            var m = new GenericMenu();

            m.AddItem(new GUIContent(L10n.Tr("Copy")), false, view.Copy);
            m.AddItem(new GUIContent(L10n.Tr("Select All")), false, view.SelectAll);
            m.AddSeparator("");

            // Find Occurrences of the selection (or the word under the
            // click) — opens the main window's Find with the query.
            string query = view.SelectedTextPublic;
            if (query != null && (query.Contains("\n") || query.Length > 200)) query = null;
            if (query == null)
            {
                view.HitTestPublic(mousePos, out int line, out int col);
                query = view.WordAt(line, col, select: false);
            }
            if (query != null && owner != null)
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Find Occurrences of '{0}'"), Trunc(query))),
                    false, () => FindReplaceWindow.OpenWithQuery(owner, query));
            m.AddItem(new GUIContent(L10n.Tr("Goto Line...")), false, () => AuxGotoPrompt.Open(view));

            // Git submenu for the viewed file, minus recursive entries.
            if (owner != null && !string.IsNullOrEmpty(path))
            {
                m.AddSeparator("");
                string gitRoot = L10n.Tr("Git") + "/";
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("Git Panel...")), false, () =>
                {
                    string repo = GitService.RepoRoot(path);
                    if (repo != null) GitWindow.Open(owner, repo);
                });
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("Blame Current File")), false,
                    () => owner.GitBlameFor(path, name));
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("File History...")), false,
                    () => owner.GitFileHistoryFor(path, name));
                if ((prune & Prune.TimeLapse) == 0)
                    m.AddItem(new GUIContent(gitRoot + L10n.Tr("Time Lapse Current File...")), false,
                        () => owner.GitTimeLapseFor(path, name));
            }
            return m;
        }

        static string Trunc(string s) => s.Length <= 24 ? s : s.Substring(0, 24) + "…";
    }

    /// <summary>Minimal Goto Line prompt for auxiliary read-only views (the
    /// main editor uses its status-bar prompt instead). Enter jumps,
    /// Escape closes; the target line is clamped to the document.</summary>
    internal class AuxGotoPrompt : EditorWindow
    {
        CodeView _target; // transient — a reload just closes the prompt

        internal static void Open(CodeView target)
        {
            var w = CreateInstance<AuxGotoPrompt>();
            w.titleContent = new GUIContent(L10n.Tr("Goto Line"));
            w._target = target;
            w.minSize = w.maxSize = new Vector2(240, 26);
            w.ShowUtility();
        }

        void CreateGUI()
        {
            if (_target == null) { Close(); return; }
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingTop = 3, paddingLeft = 4, paddingRight = 4 } };
            var field = new IntegerField(L10n.Tr("Goto Line:"))
            {
                tooltip = L10n.Tr("Line number to jump to (clamped to the document)."),
                style = { flexGrow = 1 }
            };
            field.labelElement.style.minWidth = StyleKeyword.Auto;
            field.labelElement.style.width = StyleKeyword.Auto;
            row.Add(field);
            var go = new Button(() => Go(field.value))
            { text = L10n.Tr("Go"), tooltip = L10n.Tr("Jump to the entered line."), style = { flexShrink = 0 } };
            row.Add(go);
            rootVisualElement.Add(row);
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Go(field.value); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            rootVisualElement.schedule.Execute(() => field.Focus());
        }

        void Go(int line)
        {
            if (_target != null && _target.panel != null)
                _target.GoToLine(Mathf.Clamp(line, 1, _target.LineCount), 1);
            Close();
        }
    }
}
#endif
