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
    // Menu bar: builders for every top-level menu; labels take their shortcut hints from the command table.
    public partial class TextEditorWindow
    {
        static ToolbarButton MenuButton(string title, System.Action<GenericMenu> fill)
        {
            ToolbarButton btn = null;
            btn = new ToolbarButton(() =>
            {
                var m = new GenericMenu();
                fill(m);
                m.DropDown(btn.worldBound);
            }) { text = title };
            return btn;
        }

        void FillFileMenu(GenericMenu m)
        {
            m.AddItem(new GUIContent(WithSc("New", Dsp("new-file"))), false, NewFile);
            m.AddItem(new GUIContent(WithSc("Open...", Dsp("open-file"))), false, OpenFile);
            m.AddSeparator("");
            string save = WithSc("Save", Dsp("save"));
            if (CanEditDoc)
            {
                m.AddItem(new GUIContent(save), false, () => SaveFile(false));
                m.AddItem(new GUIContent(L10n.Tr("Save As...")), false, () => SaveFile(true));
            }
            else
            {
                m.AddDisabledItem(new GUIContent(save));
                m.AddDisabledItem(new GUIContent(L10n.Tr("Save As...")));
            }
            m.AddItem(new GUIContent(WithSc("Save All", Dsp("save-all"))), false, SaveAll);
            m.AddSeparator("");
            string closeTab = WithSc("Close Tab", Dsp("close-tab"));
            if (HasDocs) m.AddItem(new GUIContent(closeTab), false, () => CloseTab(_active));
            else m.AddDisabledItem(new GUIContent(closeTab));
            m.AddSeparator("");
            var recent = EditorConfig.RecentFiles;
            if (recent.Count == 0)
                m.AddDisabledItem(new GUIContent(L10n.Tr("Recent Files")));
            else
            {
                for (int i = 0; i < recent.Count; i++)
                {
                    string p = recent[i];
                    // GenericMenu treats '/' as a submenu separator, so the
                    // label carries only the file name; ∕ fakes the dir path.
                    string dir = Path.GetDirectoryName(p)?.Replace('\\', '∕').Replace('/', '∕') ?? "";
                    string label = $"{L10n.Tr("Recent Files")}/{i + 1}  {Path.GetFileName(p)}   ({dir})";
                    m.AddItem(new GUIContent(label), false, () => OpenRecent(p));
                }
                m.AddSeparator(L10n.Tr("Recent Files") + "/");
                m.AddItem(new GUIContent(L10n.Tr("Recent Files") + "/" + L10n.Tr("Clear Recent Files")), false,
                    EditorConfig.ClearRecentFiles);
            }
            m.AddSeparator("");
            m.AddItem(new GUIContent(L10n.Tr("Close Window")), false, Close);
        }

        void FillEditMenu(GenericMenu m)
        {
            bool edit = CanEditDoc;
            void Item(string label, bool enabled, System.Action a)
            {
                if (enabled) m.AddItem(new GUIContent(label), false, () => a());
                else m.AddDisabledItem(new GUIContent(label));
            }
            Item(WithSc("Undo", Dsp("undo")), edit && _code.CanUndo, _code.Undo);
            Item(WithSc("Redo", Dsp("redo")), edit && _code.CanRedo, _code.Redo);
            m.AddSeparator("");
            // Enabled without a selection: empty-selection Cut/Copy act on
            // the whole current line (VS / VS Code / Rider standard).
            Item(WithSc("Cut", Dsp("cut")), edit, _code.Cut);
            Item(WithSc("Copy", Dsp("copy")), edit, _code.Copy);
            Item(WithSc("Paste", Dsp("paste")), edit && !string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer), _code.Paste);
            Item(WithSc("Select All", Dsp("select-all")), edit, _code.SelectAll);
            Item(WithSc("Goto Line...", Dsp("goto-line")), edit, GotoLineCommand);
            m.AddSeparator("");
            Item(WithSc("Duplicate Line", Dsp("duplicate-line")), edit, DuplicateLine);
            Item(WithSc("Delete Line", Dsp("delete-line")), edit, DeleteLine);
            Item(WithSc("Move Line Up", Dsp("move-line-up")), edit, () => MoveLine(-1));
            Item(WithSc("Move Line Down", Dsp("move-line-down")), edit, () => MoveLine(1));
            Item(WithSc("Toggle Comment", Dsp("toggle-comment")), edit, ToggleComment);
            Item(WithSc("Indent", Dsp("indent")), edit, InsertTab);
            Item(WithSc("Unindent", Dsp("unindent")), edit, UnindentSelection);
            m.AddSeparator("");
            Item(WithSc("Insert Line Above", Dsp("insert-line-above")), edit, () => _code.InsertLineAbove());
            Item(WithSc("Insert Line Below", Dsp("insert-line-below")), edit, () => _code.InsertLineBelow());
            Item(WithSc("Join Lines", Dsp("join-lines")), edit, () => _code.JoinLines());
            Item(WithSc("Select Line", Dsp("select-line")), edit, () => _code.SelectCurrentLine());
            bool sel = edit && _code.HasSelectionPublic;
            Item(L10n.Tr("Transform") + "/" + L10n.Tr("UPPERCASE"), sel,
                () => _code.TransformSelection(s => s.ToUpperInvariant()));
            Item(L10n.Tr("Transform") + "/" + L10n.Tr("lowercase"), sel,
                () => _code.TransformSelection(s => s.ToLowerInvariant()));
            Item(WithSc("Sort Selected Lines", null), sel, () => _code.SortSelectedLines());
            m.AddSeparator("");
            m.AddItem(new GUIContent(WithSc("Find...", Dsp("find"))), false, () => FindReplaceWindow.Open(this, false, false));
            m.AddItem(new GUIContent(WithSc("Find in Tabs...", Dsp("find-in-tabs"))), false, () => FindReplaceWindow.Open(this, false, true));
            m.AddItem(new GUIContent(WithSc("Replace...", Dsp("replace"))), false, () => FindReplaceWindow.Open(this, true, false));
            m.AddItem(new GUIContent(WithSc("Replace in Tabs...", Dsp("replace-in-tabs"))), false, () => FindReplaceWindow.Open(this, true, true));
            m.AddItem(new GUIContent(WithSc("Find Next", Dsp("find-next"))), false, () => FindReplaceWindow.FindAgain(this, false));
            m.AddItem(new GUIContent(WithSc("Find Previous", Dsp("find-previous"))), false, () => FindReplaceWindow.FindAgain(this, true));
        }

        void FillViewMenu(GenericMenu m)
        {
            // Alphabetical: Console, Line Numbers, Minimap, Word Wrap.
            m.AddItem(new GUIContent(L10n.Tr("Console")), _consoleVisible, () => SetConsoleVisible(!_consoleVisible));
            m.AddItem(new GUIContent(L10n.Tr("Line Numbers")), _showLineNumbers, () =>
            {
                _showLineNumbers = !_showLineNumbers;
                _code.showLineNumbers = _showLineNumbers;
                SyncSettingsControls();
            });
            m.AddItem(new GUIContent(L10n.Tr("Minimap")), _minimapVisible, () =>
            {
                _minimapVisible = !_minimapVisible;
                _code.minimapVisible = _minimapVisible;
            });
            m.AddItem(new GUIContent(L10n.Tr("Word Wrap")), _wordWrap, () =>
            {
                _wordWrap = !_wordWrap;
                _code.wordWrap = _wordWrap;
                SyncSettingsControls();
            });
            m.AddSeparator("");
            foreach (var theme in HighlightTheme.All)
            {
                var t = theme;
                m.AddItem(new GUIContent(L10n.Tr("Theme") + "/" + t.Name), CurrentTheme == t,
                    () => { CurrentTheme = t; ApplyTheme(); SyncSettingsControls(); });
            }
            foreach (ThemeMode mode in System.Enum.GetValues(typeof(ThemeMode)))
            {
                var md = mode;
                m.AddItem(new GUIContent(L10n.Tr("Light-Dark Mode") + "/" + md), CurrentThemeMode == md,
                    () => { CurrentThemeMode = md; ApplyTheme(); SyncSettingsControls(); });
            }
        }

        void FillToolsMenu(GenericMenu m)
        {
            m.AddItem(new GUIContent(WithSc("Options...", Dsp("settings"))), false, OpenSettingsPage);
        }

        void FillWindowMenu(GenericMenu m)
        {
            bool multi = _docs.Count > 1;
            string next = WithSc("Next Tab", Dsp("next-tab"));
            string prev = WithSc("Previous Tab", Dsp("prev-tab"));
            if (multi)
            {
                m.AddItem(new GUIContent(next), false, () => StepTab(1));
                m.AddItem(new GUIContent(prev), false, () => StepTab(-1));
            }
            else
            {
                m.AddDisabledItem(new GUIContent(next));
                m.AddDisabledItem(new GUIContent(prev));
            }
            m.AddSeparator("");
            if (!HasDocs)
            {
                m.AddDisabledItem(new GUIContent(L10n.Tr("(no open tabs)")));
                return;
            }
            for (int i = 0; i < _docs.Count; i++)
            {
                int idx = i;
                m.AddItem(new GUIContent((_docs[i].IsDirty ? "*" : "") + _docs[i].DisplayName),
                    i == _active, () => SwitchTo(idx));
            }
        }

        void FillHelpMenu(GenericMenu m)
        {
            m.AddItem(new GUIContent(L10n.Tr("About ADKOM Text Editor...")), false, () =>
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(GetType().Assembly);
                EditorUtility.DisplayDialog("ADKOM Text Editor",
                    "ADKOM Text Editor " + (info != null ? info.version : L10n.Tr("(unknown version)")) +
                    "\n\n" + L10n.Tr("A real code editor, living right inside the Unity Editor.") +
                    "\n" + L10n.Tr("100% Editor-only — nothing ships in player builds.") +
                    "\n\n(c) 2026 A Different Kind Of Mind Games (MIT License)", L10n.Tr("OK"));
            });
            m.AddSeparator("");
            m.AddItem(new GUIContent(L10n.Tr("Repository")), false,
                () => Application.OpenURL("https://github.com/adkom-games/ADKOM_TextEditor"));
            m.AddItem(new GUIContent(L10n.Tr("Release Notes")), false,
                () => Application.OpenURL("https://github.com/adkom-games/ADKOM_TextEditor/blob/main/Packages/com.adkom.text-editor/RELEASE-NOTES.md"));
            m.AddItem(new GUIContent("Report an Issue"), false,
                () => Application.OpenURL("https://github.com/adkom-games/ADKOM_TextEditor/issues"));
        }

        static string WithSc(string label, string sc) =>
            string.IsNullOrEmpty(sc) ? L10n.Tr(label) : L10n.Tr(label) + "\t" + sc;
    }
}
#endif
