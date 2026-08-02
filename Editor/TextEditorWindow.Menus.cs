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
            btn.AddToClassList("menu-btn");
            return btn;
        }

        void FillFileMenu(GenericMenu m)
        {
            m.AddItem(new GUIContent(WithSc("New", Dsp("new-file"))), false, NewFile);
            m.AddItem(new GUIContent(WithSc("Open...", Dsp("open-file"))), false, OpenFile);
            m.AddItem(new GUIContent(WithSc("Quick Open...", Dsp("quick-open"))), false, ShowQuickOpen);
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
            bool sel = edit && _code.HasSelectionPublic;
            bool paste = edit && !string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer);

            // GenericMenu treats '/' in an item name as a submenu separator;
            // swap it for a lookalike (U+2215 division slash) at display time
            // so "Find / Replace" renders as ONE item.
            string MenuSafe(string s) => s.Replace('/', '∕');

            // ---- Common (flat) ----
            Item(WithSc("Undo", Dsp("undo")), edit && _code.CanUndo, _code.Undo);
            Item(WithSc("Redo", Dsp("redo")), edit && _code.CanRedo, _code.Redo);
            Item(WithSc("History...", Dsp("history")), edit, () => HistoryWindow.Open(this));
            m.AddSeparator("");
            // Empty-selection Cut/Copy act on the whole line (VS/VSCode/Rider).
            Item(WithSc("Cut", Dsp("cut")), edit, _code.Cut);
            Item(WithSc("Copy", Dsp("copy")), edit, _code.Copy);
            Item(WithSc("Paste", Dsp("paste")), paste, _code.Paste);
            Item(WithSc("Select All", Dsp("select-all")), edit, SelectAllCommand);
            m.AddSeparator("");
            Item(WithSc("Find in Files...", Dsp("find-in-files")), true, () => FindReplaceWindow.Open(this, FindReplaceWindow.FrTab.InFiles));
            m.AddSeparator("");

            // ---- Grouped submenus ----
            // (All-tabs find/replace live in the dialog now: "... in All
            // Opened Documents" on the Find and Replace tabs.)
            string find = L10n.Tr("Find") + "/";
            // One toggle for the whole tabbed dialog (checkmark = open).
            m.AddItem(new GUIContent(find + MenuSafe(WithSc("Find / Replace", Dsp("find")))), FindReplaceWindow.IsOpen,
                () => FindReplaceWindow.ToggleVisible(this));
            Item(find + WithSc("Find Next", Dsp("find-next")), true, () => FindReplaceWindow.FindAgain(this, false));
            Item(find + WithSc("Find Previous", Dsp("find-previous")), true, () => FindReplaceWindow.FindAgain(this, true));

            string selg = L10n.Tr("Selection") + "/";
            Item(selg + WithSc("Expand Selection", Dsp("expand-selection")), edit, () => _code.ExpandSelection());
            Item(selg + WithSc("Shrink Selection", Dsp("shrink-selection")), edit, () => _code.ShrinkSelection());
            Item(selg + WithSc("Add Next Occurrence", Dsp("add-next-occurrence")), edit, () => _code.AddNextOccurrence());
            Item(selg + WithSc("Select All Occurrences", Dsp("select-all-occurrences")), edit, () => _code.SelectAllOccurrences());
            Item(selg + WithSc("Add Caret Above", Dsp("add-caret-above")), edit, () => _code.AddCaretOnAdjacentLine(-1));
            Item(selg + WithSc("Add Caret Below", Dsp("add-caret-below")), edit, () => _code.AddCaretOnAdjacentLine(1));

            string lineg = L10n.Tr("Line") + "/";
            Item(lineg + WithSc("Duplicate Line", Dsp("duplicate-line")), edit, DuplicateLine);
            Item(lineg + WithSc("Delete Line", Dsp("delete-line")), edit, DeleteLine);
            Item(lineg + WithSc("Move Line Up", Dsp("move-line-up")), edit, () => MoveLine(-1));
            Item(lineg + WithSc("Move Line Down", Dsp("move-line-down")), edit, () => MoveLine(1));
            Item(lineg + WithSc("Insert Line Above", Dsp("insert-line-above")), edit, () => _code.InsertLineAbove());
            Item(lineg + WithSc("Insert Line Below", Dsp("insert-line-below")), edit, () => _code.InsertLineBelow());
            Item(lineg + WithSc("Join Lines", Dsp("join-lines")), edit, () => _code.JoinLines());
            Item(lineg + WithSc("Select Line", Dsp("select-line")), edit, () => _code.SelectCurrentLine());
            Item(lineg + WithSc("Sort Selected Lines", null), sel, () => _code.SortSelectedLines());
            Item(lineg + L10n.Tr("Transform") + "/" + L10n.Tr("UPPERCASE"), sel,
                () => _code.TransformSelection(s => s.ToUpperInvariant()));
            Item(lineg + L10n.Tr("Transform") + "/" + L10n.Tr("lowercase"), sel,
                () => _code.TransformSelection(s => s.ToLowerInvariant()));

            string codeg = L10n.Tr("Code") + "/";
            Item(codeg + WithSc("Toggle Comment", Dsp("toggle-comment")), edit, ToggleComment);
            Item(codeg + WithSc("Toggle Block Comment", Dsp("block-comment")), edit, ToggleBlockComment);
            Item(codeg + WithSc("Indent", Dsp("indent")), edit, InsertTab);
            Item(codeg + WithSc("Unindent", Dsp("unindent")), edit, UnindentSelection);
            string regg = codeg + L10n.Tr("Region") + "/";
            Item(regg + WithSc("Fold Region", Dsp("fold-region")), edit, () => _code.FoldAtCaret());
            Item(regg + WithSc("Unfold Region", Dsp("unfold-region")), edit, () => _code.UnfoldAtCaret());
            Item(regg + WithSc("Unfold All", null), edit, () => _code.UnfoldAll());
            Item(codeg + WithSc("Format Document", Dsp("format-document")), edit, FormatDocument);
            Item(codeg + WithSc("Autocomplete", "Ctrl+Space"), edit, () => _code.ShowCompletion(true));
            Item(codeg + WithSc("Rename Symbol...", Dsp("rename-symbol")), edit, RenameSymbolAtCaret);
            Item(codeg + WithSc("Find All References", Dsp("find-references")), edit, FindAllReferences);
            foreach (var msg in UnityMessages.All)
            {
                var mm = msg;
                Item(codeg + L10n.Tr("Generate Unity Method") + "/" + L10n.Tr(mm.Group) + "/" + mm.Signature,
                    edit, () => GenerateUnityMessage(mm));
            }
            Item(codeg + WithSc("Override Method...", null), edit, GenerateOverrideCommand);

            string gog = L10n.Tr("Go To") + "/";
            Item(gog + WithSc("Goto Line...", Dsp("goto-line")), edit, GotoLineCommand);
            Item(gog + WithSc("Go to #region...", Dsp("goto-region")), edit, GoToRegionCommand);
            Item(gog + WithSc("Go to Matching Bracket", Dsp("goto-bracket")), edit, () => _code.GoToMatchingBracket());
            Item(gog + WithSc("Match Previous Bracket", Dsp("goto-bracket-prev")), edit, () => _code.GoToMatchingBracketPrev());
            Item(gog + WithSc("Navigate Back", Dsp("nav-back")), true, NavigateBack);
            Item(gog + WithSc("Navigate Forward", Dsp("nav-forward")), true, NavigateForward);

            string bmg = L10n.Tr("Bookmarks") + "/";
            Item(bmg + WithSc("Toggle Bookmark", Dsp("toggle-bookmark")), edit, ToggleBookmark);
            Item(bmg + WithSc("Next Bookmark", Dsp("next-bookmark")), edit, () => JumpBookmark(1));
            Item(bmg + WithSc("Previous Bookmark", Dsp("prev-bookmark")), edit, () => JumpBookmark(-1));
            Item(bmg + WithSc("View Bookmarks", null), true, ShowBookmarksTab);
            Item(bmg + WithSc("Clear Bookmarks", null), edit, ClearBookmarks);
        }

        /// <summary>Select All: the rendered locked Markdown view selects the
        /// whole DOCUMENT (view-level block selection, since native label
        /// selection cannot span blocks); otherwise the code view.</summary>
        void SelectAllCommand()
        {
            if (ActiveIsMarkdown && Active.MdRendered && Active.MdLocked && _mdView != null)
                _mdView.SelectAllDoc();
            else
                _code.SelectAll();
        }

        /// <summary>View → Hidden Characters: renders non-printing
        /// characters (spaces, tabs, NBSP, zero-width, line ends) as faint
        /// glyphs. Per-window view state, like line numbers and word wrap.</summary>
        void ToggleShowHiddenChars()
        {
            _showHiddenChars = !_showHiddenChars;
            if (_code != null) _code.showHiddenChars = _showHiddenChars;
            PostStatus(_showHiddenChars
                ? L10n.Tr("Hidden characters shown.")
                : L10n.Tr("Hidden characters hidden."));
        }

        void FillViewMenu(GenericMenu m)
        {
            // ---- Top group: every view toggle, sorted by localized label.
            // Console-area tabs each toggle THEIR tab's visibility (checkmark
            // = the tab is offered in the strip); the rest are view state, so
            // all stay enabled even while the tab itself is read-only. ----
            var toggles = new List<KeyValuePair<string, KeyValuePair<bool, System.Action>>>();
            void Toggle(string label, bool on, System.Action act) =>
                toggles.Add(new KeyValuePair<string, KeyValuePair<bool, System.Action>>(
                    label, new KeyValuePair<bool, System.Action>(on, act)));
            Toggle(L10n.Tr("Console"), _consoleTabVisible, () => ToggleConsoleAreaTab(0));
            // '/' would nest a submenu — display with the lookalike slash.
            Toggle(L10n.Tr("Find / Replace").Replace('/', '∕'), FindReplaceWindow.IsOpen,
                () => FindReplaceWindow.ToggleVisible(this));
            Toggle(L10n.Tr("Search Results"), _searchTabVisible, () => ToggleConsoleAreaTab(1));
            Toggle(L10n.Tr("Bookmarks"), _bmTabVisible, () => ToggleConsoleAreaTab(4));
            Toggle(L10n.Tr("Line Numbers"), _showLineNumbers, () =>
            {
                _showLineNumbers = !_showLineNumbers;
                ApplyViewChrome();
                SyncSettingsControls();
            });
            Toggle(L10n.Tr("Indentation Guides"), _indentGuides, () =>
            {
                _indentGuides = !_indentGuides;
                ApplyViewChrome();
            });
            Toggle(L10n.Tr("Minimap"), _minimapVisible, () =>
            {
                _minimapVisible = !_minimapVisible;
                ApplyViewChrome();
            });
            Toggle(L10n.Tr("Word Wrap"), _wordWrap, () =>
            {
                _wordWrap = !_wordWrap;
                _code.wordWrap = _wordWrap;
                SyncSettingsControls();
            });
            Toggle(L10n.Tr("Hidden Characters"), _showHiddenChars, ToggleShowHiddenChars);
            toggles.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.CurrentCultureIgnoreCase));
            foreach (var t in toggles)
            {
                var tt = t;
                m.AddItem(new GUIContent(tt.Key), tt.Value.Key, () => tt.Value.Value());
            }
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
            FillAddonsMenu(m);
            if (UnityAiBridge.Available && CanEditDoc)
            {
                if (_code != null && _code.HasSelectionPublic)
                    m.AddItem(new GUIContent(L10n.Tr("Ask Unity AI About Selection...")), false, AskUnityAiSelection);
                else
                    m.AddDisabledItem(new GUIContent(L10n.Tr("Ask Unity AI About Selection...")));
                m.AddItem(new GUIContent(L10n.Tr("Ask Unity AI About This File...")), false, AskUnityAiDocument);
                m.AddSeparator("");
            }
            string gitRoot = L10n.Tr("Git") + "/";
            m.AddItem(new GUIContent(gitRoot + L10n.Tr("Git Panel...")), false, () =>
            {
                string repo = HasDocs && Active.HasFile
                    ? GitService.RepoRoot(Active.FilePath)
                    : GitService.RepoRoot(UnityEngine.Application.dataPath);
                if (repo == null) { PostStatus(L10n.Tr("Not inside a git repository.")); return; }
                GitWindow.Open(this, repo);
            });
            bool gitFile = HasDocs && Active != null && Active.HasFile;
            if (gitFile)
            {
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("Blame Current File")), false, GitBlameCurrent);
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("File History...")), false, GitFileHistory);
            }
            else
            {
                m.AddDisabledItem(new GUIContent(gitRoot + L10n.Tr("Blame Current File")));
                m.AddDisabledItem(new GUIContent(gitRoot + L10n.Tr("File History...")));
            }
            m.AddSeparator("");
            m.AddItem(new GUIContent(L10n.Tr("Diff / Merge...")), false, AteDiffWindow.OpenSetup);
            // Snippets are one plain-text file the user edits in ATE itself;
            // saving it hot-reloads the set (timestamp watch in SnippetStore).
            m.AddItem(new GUIContent(L10n.Tr("Edit Snippets...")), false, () =>
            {
                var unused = SnippetStore.All; // materializes the default file on first use
                OpenExternal(SnippetStore.SnippetsPath, 1, 1);
            });
            m.AddSeparator("");
            m.AddItem(new GUIContent(WithSc("Options...", Dsp("settings"))), false, OpenSettingsPage);
        }

        /// <summary>Tools → Addons: entries from the shared addons folder,
        /// grouped by case-insensitive category. Incompatible or broken
        /// addons stay visible but disabled, with the reason.</summary>
        void FillAddonsMenu(GenericMenu m)
        {
            const string root = "Addons/";
            if (!Scripting.AteAddonManager.CompilerAvailable)
            {
                m.AddDisabledItem(new GUIContent(root + L10n.Tr("Addons need Semantic Features (enable in Settings)")));
            }
            else
            {
                // Case-insensitive categories: first-seen casing wins.
                var canon = new System.Collections.Generic.Dictionary<string, string>(
                    System.StringComparer.OrdinalIgnoreCase);
                foreach (var e in Scripting.AteAddonManager.Entries)
                {
                    if (!canon.TryGetValue(e.Category, out string cat))
                        canon[e.Category] = cat = e.Category;
                    string item = root + cat.Replace('/', '∕') + "/" + e.Name.Replace('/', '∕');
                    if (e.Compatible)
                    {
                        var entry = e;
                        if (!e.Approved) item += " ⚠"; // consent pending (AddonSecurity)
                        m.AddItem(new GUIContent(item), false,
                            () => Scripting.AteAddonManager.Run(entry));
                    }
                    else
                        m.AddDisabledItem(new GUIContent(item + " — " + FirstLine(e.Error)));
                }
                if (Scripting.AteAddonManager.Entries.Count > 0) m.AddSeparator(root);
            }
            Scripting.AddonSigningMenu.Fill(m, root);
            m.AddSeparator(root);
            m.AddItem(new GUIContent(root + L10n.Tr("Install Sample Addons")), false,
                Scripting.AteAddonManager.InstallSamples);
            m.AddItem(new GUIContent(root + L10n.Tr("Reload Addons")), false,
                Scripting.AteAddonManager.Reload);
            m.AddItem(new GUIContent(root + L10n.Tr("Open Addons Folder...")), false,
                Scripting.AteAddonManager.OpenFolder);
            m.AddSeparator("");
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        /// <summary>Send-to-AI commands: open Unity Assistant's prompt popup
        /// with the selection/document attached as a text file. No AI call
        /// (no points) until the user submits the prompt.</summary>
        void AskUnityAiSelection()
        {
            string sel = _code?.SelectedTextPublic;
            if (string.IsNullOrEmpty(sel))
            {
                PostStatus(L10n.Tr("Select some text first."));
                return;
            }
            UnityAiBridge.Ask(rootVisualElement,
                L10n.Tr("Ask about the attached selection…"),
                sel, Active.DisplayName + " (selection)");
        }

        void AskUnityAiDocument()
        {
            if (!CanEditDoc) return;
            UnityAiBridge.Ask(rootVisualElement,
                L10n.Tr("Ask about the attached document…"),
                _code.value, Active.DisplayName);
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
            // Tabs listed alphabetically (ties in tab order), not strip order.
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < _docs.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byName = string.Compare(_docs[a].DisplayName, _docs[b].DisplayName,
                    System.StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.CompareTo(b);
            });
            foreach (int i in order)
            {
                int idx = i;
                m.AddItem(new GUIContent((_docs[i].IsDirty ? "*" : "") + _docs[i].DisplayName.Replace('/', '∕')),
                    i == _active, () => SwitchTo(idx));
            }
        }

        /// <summary>Games menu: the player guide, the Z-Machine, then every
        /// installed addon game (Category "Games") sorted by name.</summary>
        void FillGamesMenu(GenericMenu m)
        {
            // A second link to the player guide on purpose — the Games menu
            // is where a player looks first; Help > Documentation keeps its
            // copy as part of the full reference shelf.
            m.AddItem(new GUIContent(L10n.Tr("How to Play")), false,
                () => OpenPackageDoc("Documentation~/Games.md"));
            m.AddSeparator("");
            AteZMachine.ZMachineGame.AddMenuItems(m, L10n.Tr("Z-Machine (Zork)") + "/", this);
            m.AddSeparator("");
            if (!Scripting.AteAddonManager.CompilerAvailable)
            {
                m.AddDisabledItem(new GUIContent(L10n.Tr("Addons need Semantic Features (enable in Settings)")));
                return;
            }
            var games = new List<Scripting.AteAddonManager.Entry>();
            foreach (var e in Scripting.AteAddonManager.Entries)
                if (string.Equals(e.Category, "Games", System.StringComparison.OrdinalIgnoreCase))
                    games.Add(e);
            if (games.Count == 0)
            {
                m.AddDisabledItem(new GUIContent(L10n.Tr("(no games installed)")));
                return;
            }
            games.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.CurrentCultureIgnoreCase));
            foreach (var g in games)
            {
                var entry = g;
                string item = g.Name.Replace('/', '∕');
                if (g.Compatible)
                {
                    if (!g.Approved) item += " ⚠"; // consent pending (AddonSecurity)
                    m.AddItem(new GUIContent(item), false, () => Scripting.AteAddonManager.Run(entry));
                }
                else
                    m.AddDisabledItem(new GUIContent(item + " — " + FirstLine(g.Error)));
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
            m.AddSeparator("");
            // Help > Documentation: the user-level Games guide pinned on
            // top, then every reference doc sorted by its localized label.
            string docg = L10n.Tr("Documentation") + "/";
            m.AddItem(new GUIContent(docg + L10n.Tr("Games").Replace('/', '∕')), false,
                () => OpenPackageDoc("Documentation~/Games.md"));
            m.AddSeparator(docg);
            var docs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(L10n.Tr("ATE Manual"), "Manual.md"),
                new KeyValuePair<string, string>(L10n.Tr("Scripting Reference"), "Documentation~/Scripting.md"),
                new KeyValuePair<string, string>(L10n.Tr("AteApi Design"), "Documentation~/ATEAPI Design.md"),
                new KeyValuePair<string, string>(L10n.Tr("Game API Design"), "Documentation~/Game API Design.md"),
                new KeyValuePair<string, string>(L10n.Tr("Addon Signing"), "Documentation~/Addon Signing.md"),
                new KeyValuePair<string, string>(L10n.Tr("Keyboard Shortcuts"), "Documentation~/Keyboard Shortcuts.md"),
                new KeyValuePair<string, string>(L10n.Tr("Localization"), "Documentation~/Localization.md"),
                new KeyValuePair<string, string>(L10n.Tr("Snippets"), "Documentation~/Snippets.md"),
                new KeyValuePair<string, string>(L10n.Tr("Troubleshooting"), "Documentation~/Troubleshooting.md"),
            };
            docs.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.CurrentCultureIgnoreCase));
            foreach (var d in docs)
            {
                var doc = d;
                m.AddItem(new GUIContent(docg + doc.Key.Replace('/', '∕')), false,
                    () => OpenPackageDoc(doc.Value));
            }
        }

        /// <summary>Help > Documentation: opens a package-relative reference
        /// doc in a regular tab (rendered per the Markdown default-view
        /// setting).</summary>
        void OpenPackageDoc(string relPath)
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(GetType().Assembly);
            string path = pkg != null ? System.IO.Path.Combine(pkg.resolvedPath, relPath) : null;
            if (path != null && System.IO.File.Exists(path))
            {
                PushNavLocation();
                OpenPath(System.IO.Path.GetFullPath(path));
            }
            else PostStatus(string.Format(L10n.Tr("{0} not found."),
                System.IO.Path.GetFileName(relPath)));
        }

        // GenericMenu treats '/' anywhere in an item name as a submenu
        // separator — shortcut hints like "Ctrl+/" made Toggle Comment an
        // EMPTY submenu. Escape to the U+2215 lookalike here so every menu
        // label and hint is safe; submenu prefixes are appended by callers
        // AFTER this, so real hierarchy still works.
        static string WithSc(string label, string sc) =>
            (string.IsNullOrEmpty(sc) ? L10n.Tr(label) : L10n.Tr(label) + "\t" + sc)
            .Replace('/', '∕');
    }
}
#endif
