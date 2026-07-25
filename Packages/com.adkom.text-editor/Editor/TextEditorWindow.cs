using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Dockable Unity Editor text editor window. UIToolkit-based, built on the
    /// virtualized CodeView (only visible lines are rendered, so keystroke
    /// cost is independent of file size). Holds any number of open documents,
    /// presented as tabs; Settings opens as a special tab.
    /// </summary>
    public class TextEditorWindow : EditorWindow
    {
        const string UssPath = "Packages/com.adkom.text-editor/Editor/UI/TextEditor.uss";
        const string ThemePrefKey = "ADKOM.TextEditor.Theme";
        const string ThemeModePrefKey = "ADKOM.TextEditor.ThemeMode";

        static HighlightTheme CurrentTheme
        {
            get => HighlightTheme.ByName(EditorPrefs.GetString(ThemePrefKey, HighlightTheme.VSCode.Name));
            set => EditorPrefs.SetString(ThemePrefKey, value.Name);
        }

        static ThemeMode CurrentThemeMode
        {
            get => (ThemeMode)EditorPrefs.GetInt(ThemeModePrefKey, (int)ThemeMode.Auto);
            set => EditorPrefs.SetInt(ThemeModePrefKey, (int)value);
        }

        [SerializeField] List<TextDocument> _docs = new List<TextDocument>();
        [SerializeField] int _active;
        [SerializeField] bool _showLineNumbers;
        [SerializeField] bool _wordWrap;

        CodeView _code;
        VisualElement _tabBar;
        Label _statusLeft;
        Label _statusRight;

        VisualElement _editorArea;
        VisualElement _settingsPane;
        PopupField<string> _settingsTheme;
        EnumField _settingsMode;
        Toggle _settingsLines;
        Toggle _settingsWrap;
        IntegerField _settingsTabSize;
        EnumField _settingsKeymap;

        ITextFormatter _formatter = new PlainTextFormatter();

        TextDocument Active => _docs[_active];

        [MenuItem("Tools/ADKOM/Text Editor")]
        public static void Open()
        {
            var window = CreateWindow<TextEditorWindow>();
            window.UpdateTitle();
            window.Show();
        }

        const string AssetsMenuPath = "Assets/Open in ADKOM Text Editor";

        // Extensions Unity does not import as a text-like asset type but that
        // are still plain text. Anything imported as MonoScript/TextAsset/
        // ShaderInclude etc. is accepted by type instead.
        static readonly string[] TextExtensions =
        {
            ".md", ".yaml", ".yml", ".ini", ".cfg", ".log", ".uss", ".tss",
            ".uxml", ".asmdef", ".asmref", ".shader", ".cginc", ".hlsl",
            ".compute", ".gitignore", ".gitattributes"
        };

        [MenuItem(AssetsMenuPath, true)]
        static bool ValidateOpenSelectedAsset()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                return false;

            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (type == typeof(TextAsset) || type == typeof(MonoScript) ||
                typeof(TextAsset).IsAssignableFrom(type))
                return true;

            string ext = Path.GetExtension(assetPath).ToLowerInvariant();
            return System.Array.IndexOf(TextExtensions, ext) >= 0;
        }

        [MenuItem(AssetsMenuPath)]
        static void OpenSelectedAsset()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            var windows = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            var window = windows.Length > 0 ? windows[0] : CreateWindow<TextEditorWindow>();
            window.Show();
            window.Focus();
            window.OpenPath(Path.GetFullPath(assetPath));
        }

        void EnsureDocs()
        {
            if (_docs.Count == 0) _docs.Add(new TextDocument());
            _active = Mathf.Clamp(_active, 0, _docs.Count - 1);
        }

        void CreateGUI()
        {
            EnsureDocs();

            var root = rootVisualElement;
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);

            root.RegisterCallback<KeyDownEvent>(OnGlobalKeyDown, TrickleDown.TrickleDown);

            // --- Toolbar ---
            var toolbar = new Toolbar();
            toolbar.Add(ToolbarBtn("New", NewFile));
            toolbar.Add(ToolbarBtn("Open", OpenFile));
            toolbar.Add(ToolbarBtn("Save", () => SaveFile(false)));
            toolbar.Add(ToolbarBtn("Save As", () => SaveFile(true)));
            toolbar.Add(new ToolbarSpacer { flex = true });
            var gear = new ToolbarButton(OpenSettings) { tooltip = "Settings" };
            var gearTex = EditorGUIUtility.IconContent("SettingsIcon").image;
            if (gearTex != null)
            {
                var icon = new Image { image = gearTex, scaleMode = ScaleMode.ScaleToFit };
                icon.style.width = 16;
                icon.style.height = 16;
                gear.Add(icon);
            }
            else gear.text = "⚙";
            toolbar.Add(gear);
            root.Add(toolbar);

            // --- Tab bar ---
            _tabBar = new VisualElement { name = "tab-bar" };
            root.Add(_tabBar);

            // --- Editor area: virtualized code view ---
            _editorArea = new VisualElement { name = "editor-row" };
            _editorArea.style.flexGrow = 1;

            _code = new CodeView { TabSize = EditorConfig.TabSize };
            _code.SetValueWithoutNotify(Active.Content);
            _code.onValueChanged += OnTextChanged;
            _code.showLineNumbers = _showLineNumbers;
            _code.wordWrap = _wordWrap;
            _code.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _editorArea.Add(_code);
            root.Add(_editorArea);

            BuildSettingsPane(root);

            // --- Status bar ---
            var status = new VisualElement { name = "status-bar" };
            status.style.flexDirection = FlexDirection.Row;
            status.style.justifyContent = Justify.SpaceBetween;
            _statusLeft = new Label();
            _statusRight = new Label();
            status.Add(_statusLeft);
            status.Add(_statusRight);
            root.Add(status);

            ApplyTheme();
            SwitchTo(_active); // also restores settings-tab visibility state

            // Caret moves don't fire value changes; poll cheaply for line:col.
            root.schedule.Execute(UpdateStatus).Every(200);
        }

        static ToolbarButton ToolbarBtn(string text, System.Action onClick) =>
            new ToolbarButton(onClick) { text = text };

        void OnTextChanged(string newValue)
        {
            Active.Content = newValue;
            if (!Active.IsDirty)
            {
                Active.IsDirty = true;
                UpdateTitle();
                RebuildTabs();
            }
        }

        // --- Theme ---

        void ApplyTheme()
        {
            HighlightTheme.Mode = CurrentThemeMode;
            var palette = CurrentTheme.Current;
            TextFormatters.Theme = CurrentTheme;
            _code?.SetTheme(palette.TextColor, palette.BackgroundColor, palette.SelectionColor);
            RefreshFormatter();
        }

        void RefreshFormatter()
        {
            _formatter = TextFormatters.ForPath(Active.HasFile ? Active.FilePath : null);
            _code?.SetFormatter(_formatter);
        }

        // --- Settings tab ---

        void BuildSettingsPane(VisualElement root)
        {
            _settingsPane = new VisualElement { name = "settings-pane" };
            _settingsPane.style.flexGrow = 1;
            _settingsPane.style.display = DisplayStyle.None;

            var title = new Label("Editor Settings");
            title.AddToClassList("settings-title");
            _settingsPane.Add(title);

            var themeNames = new List<string>();
            foreach (var t in HighlightTheme.All) themeNames.Add(t.Name);
            _settingsTheme = new PopupField<string>("Color Theme", themeNames, CurrentTheme.Name);
            _settingsTheme.RegisterValueChangedCallback(e =>
            {
                CurrentTheme = HighlightTheme.ByName(e.newValue);
                ApplyTheme();
            });
            _settingsPane.Add(_settingsTheme);

            _settingsMode = new EnumField("Light/Dark Mode", CurrentThemeMode);
            _settingsMode.RegisterValueChangedCallback(e =>
            {
                CurrentThemeMode = (ThemeMode)e.newValue;
                ApplyTheme();
            });
            _settingsPane.Add(_settingsMode);

            _settingsLines = new Toggle("Line Numbers") { value = _showLineNumbers };
            _settingsLines.RegisterValueChangedCallback(e =>
            {
                _showLineNumbers = e.newValue;
                if (_code != null) _code.showLineNumbers = e.newValue;
            });
            _settingsPane.Add(_settingsLines);

            _settingsWrap = new Toggle("Word Wrap") { value = _wordWrap };
            _settingsWrap.RegisterValueChangedCallback(e =>
            {
                _wordWrap = e.newValue;
                if (_code != null) _code.wordWrap = e.newValue;
            });
            _settingsPane.Add(_settingsWrap);

            _settingsTabSize = new IntegerField("Tab Size") { value = EditorConfig.TabSize };
            _settingsTabSize.RegisterValueChangedCallback(e =>
            {
                EditorConfig.TabSize = e.newValue;
                _settingsTabSize.SetValueWithoutNotify(EditorConfig.TabSize); // clamp echo
                if (_code != null) _code.TabSize = EditorConfig.TabSize;
            });
            _settingsTabSize.tooltip = "Spaces a tab renders as. Applies to files opened after the change.";
            _settingsPane.Add(_settingsTabSize);

            _settingsKeymap = new EnumField("Keyboard Layout", EditorConfig.Keymap);
            _settingsKeymap.RegisterValueChangedCallback(e =>
                EditorConfig.Keymap = (KeymapLayout)e.newValue);
            _settingsKeymap.tooltip = "Which IDE's default shortcuts to use for the commands this editor supports.";
            _settingsPane.Add(_settingsKeymap);

            root.Add(_settingsPane);
        }

        /// <summary>Gear behavior: open the settings tab, bring it to the front
        /// if it exists in the background, or close it when already frontmost.</summary>
        void OpenSettings()
        {
            EnsureDocs();
            int existing = _docs.FindIndex(d => d.IsSettings);
            if (existing >= 0)
            {
                if (existing == _active) CloseTab(existing);
                else SwitchTo(existing);
                return;
            }
            _docs.Add(new TextDocument { IsSettings = true });
            SwitchTo(_docs.Count - 1);
        }

        void SyncSettingsControls()
        {
            _settingsTheme?.SetValueWithoutNotify(CurrentTheme.Name);
            _settingsMode?.SetValueWithoutNotify(CurrentThemeMode);
            _settingsLines?.SetValueWithoutNotify(_showLineNumbers);
            _settingsWrap?.SetValueWithoutNotify(_wordWrap);
            _settingsTabSize?.SetValueWithoutNotify(EditorConfig.TabSize);
            _settingsKeymap?.SetValueWithoutNotify(EditorConfig.Keymap);
        }

        // --- Tabs ---

        void RebuildTabs()
        {
            if (_tabBar == null) return;
            _tabBar.Clear();
            for (int i = 0; i < _docs.Count; i++)
            {
                int index = i;
                var doc = _docs[i];

                var tab = new VisualElement();
                tab.AddToClassList("tab");
                if (i == _active) tab.AddToClassList("tab--active");
                tab.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (e.button == 0) SwitchTo(index);
                    else if (e.button == 2) CloseTab(index); // middle-click close
                });

                var label = new Label((doc.IsDirty ? "*" : "") + doc.DisplayName)
                {
                    tooltip = doc.HasFile ? doc.FilePath : "New unsaved document"
                };
                tab.Add(label);

                var close = new Button(() => CloseTab(index)) { text = "×" };
                close.AddToClassList("tab__close");
                tab.Add(close);

                _tabBar.Add(tab);
            }
        }

        void SwitchTo(int index)
        {
            EnsureDocs();
            _active = Mathf.Clamp(index, 0, _docs.Count - 1);

            bool settings = Active.IsSettings;
            if (_editorArea != null)
                _editorArea.style.display = settings ? DisplayStyle.None : DisplayStyle.Flex;
            if (_settingsPane != null)
                _settingsPane.style.display = settings ? DisplayStyle.Flex : DisplayStyle.None;
            if (settings)
            {
                SyncSettingsControls();
                RebuildTabs();
                UpdateTitle();
                UpdateStatus();
                return;
            }

            CheckExternalChange(Active);
            _code?.SetValueWithoutNotify(Active.Content);
            RefreshFormatter();
            RebuildTabs();
            UpdateTitle();
            UpdateStatus();
        }

        void CloseTab(int index)
        {
            if (index < 0 || index >= _docs.Count) return;
            if (!ConfirmDiscardIfDirty(_docs[index])) return;
            _docs.RemoveAt(index);
            if (index < _active || _active >= _docs.Count)
                _active = Mathf.Max(0, _active - 1);
            EnsureDocs();
            SwitchTo(_active);
        }

        // --- Commands ---

        void NewFile()
        {
            _docs.Add(new TextDocument());
            SwitchTo(_docs.Count - 1);
        }

        void OpenFile()
        {
            string path = FileService.PromptOpen();
            if (path != null) OpenPath(path);
        }

        /// <summary>Opens the file at <paramref name="path"/> in a tab: switches to
        /// its tab if already open, otherwise adds a new one.</summary>
        public void OpenPath(string path)
        {
            EnsureDocs();
            string full = Path.GetFullPath(path);
            int existing = _docs.FindIndex(d => d.HasFile &&
                string.Equals(Path.GetFullPath(d.FilePath), full, System.StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                SwitchTo(existing);
                return;
            }

            var doc = new TextDocument();
            doc.LoadFrom(full);
            _docs.Add(doc);
            SwitchTo(_docs.Count - 1);
        }

        void SaveFile(bool saveAs)
        {
            if (Active.IsSettings) return;
            bool saved = saveAs ? FileService.SaveAs(Active) : FileService.Save(Active);
            if (saved)
            {
                RefreshFormatter(); // Save As can change the extension/language
                RebuildTabs();
                UpdateTitle();
                UpdateStatus();
            }
        }

        /// <summary>Returns true to proceed (saved or discarded), false to cancel.</summary>
        bool ConfirmDiscardIfDirty(TextDocument doc)
        {
            if (!doc.IsDirty) return true;
            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Changes",
                $"'{doc.DisplayName}' has unsaved changes.",
                "Save", "Cancel", "Discard");
            if (choice == 1) return false;                // Cancel
            if (choice == 0) return FileService.Save(doc); // Save (false if dialog cancelled)
            return true;                                   // Discard
        }

        /// <summary>Prompts to reload <paramref name="doc"/> if its backing file
        /// changed on disk. Returns true if the document was reloaded or marked.</summary>
        bool CheckExternalChange(TextDocument doc)
        {
            if (doc == null || !doc.FileChangedOnDisk()) return false;
            bool reload = EditorUtility.DisplayDialog(
                "File Changed on Disk",
                $"'{doc.DisplayName}' was modified outside the editor.\n\nReload it? Unsaved changes here will be lost.",
                "Reload", "Keep Mine");
            if (reload)
            {
                doc.LoadFrom(doc.FilePath);
            }
            else
            {
                // Stop re-prompting until it changes again.
                doc.LastKnownWriteTimeUtcTicks = File.GetLastWriteTimeUtc(doc.FilePath).Ticks;
                doc.IsDirty = true;
            }
            return true;
        }

        void OnFocus()
        {
            // Inactive tabs are checked when they are activated (SwitchTo).
            if (_docs == null || _docs.Count == 0) return;
            if (CheckExternalChange(Active))
            {
                _code?.SetValueWithoutNotify(Active.Content);
                RefreshFormatter();
                RebuildTabs();
                UpdateTitle();
            }
        }

        void OnDestroy()
        {
            if (_docs == null) return;
            foreach (var doc in _docs)
            {
                if (!doc.IsDirty) continue;
                bool save = EditorUtility.DisplayDialog(
                    "Unsaved Changes",
                    $"'{doc.DisplayName}' has unsaved changes.",
                    "Save", "Discard");
                if (save) FileService.Save(doc);
            }
        }

        // --- Keyboard commands ---
        // Three layouts (Settings → Keyboard Layout): Visual Studio, VS Code,
        // Rider — covering the defaults that apply to this editor's features.

        /// <summary>Window-level commands; works from any tab including Settings.</summary>
        void OnGlobalKeyDown(KeyDownEvent e)
        {
            bool ctrl = e.ctrlKey || e.commandKey;
            bool handled = false;

            // Find/Replace — common across layouts (Rider uses Ctrl+R for replace).
            bool rider = EditorConfig.Keymap == KeymapLayout.Rider;
            if (ctrl && !e.altKey && e.keyCode == KeyCode.F)
            {
                FindReplaceWindow.Open(this, replaceFocus: false, allTabs: e.shiftKey);
                handled = true;
            }
            else if (!rider && ctrl && !e.altKey && e.keyCode == KeyCode.H)
            {
                FindReplaceWindow.Open(this, replaceFocus: true, allTabs: e.shiftKey);
                handled = true;
            }
            else if (rider && ctrl && !e.altKey && e.keyCode == KeyCode.R)
            {
                FindReplaceWindow.Open(this, replaceFocus: true, allTabs: e.shiftKey);
                handled = true;
            }
            else if (e.keyCode == KeyCode.F3 && !ctrl && !e.altKey)
            {
                handled = FindReplaceWindow.FindAgain(this, reverse: e.shiftKey);
            }

            if (handled)
            {
                e.PreventDefault();
                e.StopImmediatePropagation();
                return;
            }

            if (EditorConfig.Keymap == KeymapLayout.VisualStudio)
            {
                if (ctrl && !e.altKey && e.keyCode == KeyCode.S)
                {
                    if (e.shiftKey) SaveAll(); else SaveFile(false);
                    handled = true;
                }
                else if (ctrl && e.keyCode == KeyCode.N && !e.shiftKey) { NewFile(); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.O && !e.shiftKey) { OpenFile(); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.F4) { CloseTab(_active); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.Tab)
                {
                    StepTab(e.shiftKey ? -1 : 1);
                    handled = true;
                }
            }
            else if (EditorConfig.Keymap == KeymapLayout.Rider)
            {
                if (ctrl && !e.altKey && !e.shiftKey && e.keyCode == KeyCode.S) { SaveAll(); handled = true; }
                else if (ctrl && e.altKey && e.keyCode == KeyCode.S) { OpenSettings(); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.F4) { CloseTab(_active); handled = true; }
                else if (e.altKey && !ctrl && e.keyCode == KeyCode.RightArrow) { StepTab(1); handled = true; }
                else if (e.altKey && !ctrl && e.keyCode == KeyCode.LeftArrow) { StepTab(-1); handled = true; }
            }
            else // VS Code
            {
                if (ctrl && !e.altKey && !e.shiftKey && e.keyCode == KeyCode.S) { SaveFile(false); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.N && !e.shiftKey) { NewFile(); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.O && !e.shiftKey) { OpenFile(); handled = true; }
                else if (ctrl && (e.keyCode == KeyCode.W || e.keyCode == KeyCode.F4)) { CloseTab(_active); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.PageDown) { StepTab(1); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.PageUp) { StepTab(-1); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.Tab) { StepTab(e.shiftKey ? -1 : 1); handled = true; }
                else if (ctrl && e.keyCode == KeyCode.Comma) { OpenSettings(); handled = true; }
            }
            if (handled)
            {
                e.PreventDefault();
                e.StopImmediatePropagation();
            }
        }

        /// <summary>Text-editing commands on the code view (trickle-down, so
        /// they win over CodeView's own typing/navigation handling).</summary>
        void OnKeyDown(KeyDownEvent e)
        {
            // Swallow the character-only Tab event; the keyCode event acts.
            if (e.keyCode == KeyCode.None && e.character == '\t')
            {
                e.PreventDefault();
                e.StopImmediatePropagation();
                return;
            }

            bool ctrl = e.ctrlKey || e.commandKey;
            var layout = EditorConfig.Keymap;
            bool vs = layout == KeymapLayout.VisualStudio;
            bool rider = layout == KeymapLayout.Rider;
            bool vscode = layout == KeymapLayout.VSCode;
            bool handled = false;

            if (e.keyCode == KeyCode.Tab && !ctrl && !e.altKey)
            {
                if (e.shiftKey) UnindentSelection(); else InsertTab();
                handled = true;
            }
            else if ((vs || rider) && ctrl && !e.altKey && !e.shiftKey && e.keyCode == KeyCode.D)
            {
                DuplicateLine();
                handled = true;
            }
            else if (vscode && e.altKey && e.shiftKey && !ctrl &&
                     (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.UpArrow))
            {
                DuplicateLine(); // VS Code: Copy Line Down/Up
                handled = true;
            }
            else if (vs && ctrl && !e.shiftKey && e.keyCode == KeyCode.L) { DeleteLine(); handled = true; }
            else if (rider && ctrl && !e.shiftKey && e.keyCode == KeyCode.Y) { DeleteLine(); handled = true; }
            else if (vscode && ctrl && e.shiftKey && e.keyCode == KeyCode.K) { DeleteLine(); handled = true; }
            else if ((vs || vscode) && e.altKey && !ctrl && !e.shiftKey && e.keyCode == KeyCode.UpArrow) { MoveLine(-1); handled = true; }
            else if ((vs || vscode) && e.altKey && !ctrl && !e.shiftKey && e.keyCode == KeyCode.DownArrow) { MoveLine(1); handled = true; }
            else if (rider && e.altKey && e.shiftKey && !ctrl && e.keyCode == KeyCode.UpArrow) { MoveLine(-1); handled = true; }
            else if (rider && e.altKey && e.shiftKey && !ctrl && e.keyCode == KeyCode.DownArrow) { MoveLine(1); handled = true; }
            else if (ctrl && !e.shiftKey && !e.altKey && e.keyCode == KeyCode.Slash) { ToggleComment(); handled = true; }

            if (handled)
            {
                e.PreventDefault();
                e.StopImmediatePropagation();
            }
        }

        void StepTab(int dir)
        {
            EnsureDocs();
            SwitchTo((_active + dir + _docs.Count) % _docs.Count);
        }

        void SaveAll()
        {
            EnsureDocs();
            for (int i = 0; i < _docs.Count; i++)
            {
                var doc = _docs[i];
                if (doc.IsSettings || !doc.IsDirty) continue;
                FileService.Save(doc); // prompts Save As for untitled docs
            }
            RebuildTabs();
            UpdateTitle();
            UpdateStatus();
        }

        // --- Text editing helpers (operate on the code view; value-change
        // events keep the document and tabs in sync) ---

        void GetSelection(out int start, out int end)
        {
            int a = _code.cursorIndex, b = _code.selectIndex;
            start = Mathf.Min(a, b);
            end = Mathf.Max(a, b);
        }

        void ReplaceRange(int start, int end, string replacement, int caret)
        {
            _code.ReplaceRangeInternal(start, end, replacement, caret, typing: false);
        }

        int LineStartOf(string text, int index)
        {
            int i = Mathf.Clamp(index, 0, text.Length);
            while (i > 0 && text[i - 1] != '\n') i--;
            return i;
        }

        int LineEndOf(string text, int index)
        {
            int i = Mathf.Clamp(index, 0, text.Length);
            while (i < text.Length && text[i] != '\n') i++;
            return i;
        }

        void InsertTab()
        {
            GetSelection(out int start, out int end);
            string v = _code.value;
            int tabSize = EditorConfig.TabSize;
            if (start != end && v.IndexOf('\n', start, end - start) >= 0)
            {
                IndentLines(start, end, tabSize);
                return;
            }
            int col = start - LineStartOf(v, start);
            int add = tabSize - (col % tabSize);
            ReplaceRange(start, end, new string(' ', add), start + add);
        }

        void IndentLines(int start, int end, int tabSize)
        {
            string v = _code.value;
            int first = LineStartOf(v, start);
            var sb = new System.Text.StringBuilder();
            string indent = new string(' ', tabSize);
            int lineCount = 0;
            for (int i = first; i < end;)
            {
                int le = LineEndOf(v, i);
                sb.Append(indent).Append(v, i, le - i);
                if (le < v.Length && le < end) sb.Append('\n');
                lineCount++;
                i = le + 1;
            }
            ReplaceRange(first, LineEndOf(v, end == first ? first : end - 1), sb.ToString(), first);
            _code.cursorIndex = first;
            _code.selectIndex = Mathf.Min(_code.value.Length, end + lineCount * tabSize);
        }

        void UnindentSelection()
        {
            GetSelection(out int start, out int end);
            string v = _code.value;
            int tabSize = EditorConfig.TabSize;
            int first = LineStartOf(v, start);
            int last = LineEndOf(v, end == start ? start : end - 1);
            var sb = new System.Text.StringBuilder();
            int removedTotal = 0;
            for (int i = first; i <= last && i <= v.Length;)
            {
                int le = LineEndOf(v, i);
                int remove = 0;
                while (remove < tabSize && i + remove < le && v[i + remove] == ' ') remove++;
                removedTotal += remove;
                sb.Append(v, i + remove, le - i - remove);
                if (le < v.Length && le < last) sb.Append('\n');
                i = le + 1;
            }
            ReplaceRange(first, last, sb.ToString(), Mathf.Max(first, start - Mathf.Min(removedTotal, tabSize)));
        }

        void DuplicateLine()
        {
            string v = _code.value;
            GetSelection(out int start, out _);
            int ls = LineStartOf(v, start), le = LineEndOf(v, start);
            string line = v.Substring(ls, le - ls);
            ReplaceRange(le, le, "\n" + line, start + line.Length + 1);
        }

        void DeleteLine()
        {
            string v = _code.value;
            GetSelection(out int start, out _);
            int ls = LineStartOf(v, start), le = LineEndOf(v, start);
            int removeEnd = le < v.Length ? le + 1 : le;
            int removeStart = le >= v.Length && ls > 0 ? ls - 1 : ls;
            int col = start - ls;
            ReplaceRange(removeStart, removeEnd, string.Empty, removeStart + col);
        }

        void MoveLine(int dir)
        {
            string v = _code.value;
            GetSelection(out int start, out _);
            int ls = LineStartOf(v, start), le = LineEndOf(v, start);
            int col = start - ls;
            if (dir < 0)
            {
                if (ls == 0) return;
                int pls = LineStartOf(v, ls - 1);
                string cur = v.Substring(ls, le - ls);
                string prev = v.Substring(pls, ls - 1 - pls);
                ReplaceRange(pls, le, cur + "\n" + prev, pls + col);
            }
            else
            {
                if (le >= v.Length) return;
                int nle = LineEndOf(v, le + 1);
                string cur = v.Substring(ls, le - ls);
                string next = v.Substring(le + 1, nle - le - 1);
                ReplaceRange(ls, nle, next + "\n" + cur, ls + next.Length + 1 + col);
            }
        }

        void ToggleComment()
        {
            GetSelection(out int start, out int end);
            string v = _code.value;
            int first = LineStartOf(v, start);
            int last = LineEndOf(v, end == start ? start : end - 1);

            // Uncomment only when every non-blank selected line is commented.
            bool allCommented = true;
            for (int i = first; i <= last && i < v.Length;)
            {
                int le = LineEndOf(v, i);
                int ns = i;
                while (ns < le && v[ns] == ' ') ns++;
                if (ns < le && (ns + 1 >= le || v[ns] != '/' || v[ns + 1] != '/'))
                    allCommented = false;
                i = le + 1;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = first; i <= last && i <= v.Length;)
            {
                int le = LineEndOf(v, i);
                int ns = i;
                while (ns < le && v[ns] == ' ') ns++;
                sb.Append(v, i, ns - i);
                if (allCommented)
                {
                    int skip = ns;
                    if (skip + 1 < le && v[skip] == '/' && v[skip + 1] == '/')
                    {
                        skip += 2;
                        if (skip < le && v[skip] == ' ') skip++;
                    }
                    sb.Append(v, skip, le - skip);
                }
                else
                {
                    if (ns < le) sb.Append("// ");
                    sb.Append(v, ns, le - ns);
                }
                if (le < v.Length && le < last) sb.Append('\n');
                i = le + 1;
            }
            ReplaceRange(first, last, sb.ToString(), first);
        }

        // --- Find/Replace surface (used by FindReplaceWindow) ---

        public int DocCount => _docs.Count;
        public int ActiveIndex => _active;
        public bool IsSettingsTab(int i) => _docs[i].IsSettings;
        public string GetDocName(int i) => _docs[i].DisplayName;

        public string GetDocContent(int i) =>
            i == _active && !Active.IsSettings ? _code.value : _docs[i].Content;

        public void SwitchTab(int i) => SwitchTo(i);

        public void GetSelectionSpan(out int start, out int end) => GetSelection(out start, out end);

        /// <summary>Selects [start, end) in the active document, caret at end.</summary>
        public void SelectSpan(int start, int end)
        {
            _code.selectIndex = start;
            _code.cursorIndex = end;
            Focus();
            _code.Focus();
        }

        /// <summary>Replaces [start, end) in the active document (undoable).</summary>
        public void ReplaceSpanInActive(int start, int end, string text) =>
            _code.ReplaceRangeInternal(start, end, text, start + text.Length, typing: false);

        /// <summary>Replaces a document's entire content. The active document
        /// goes through the code view (undoable); background documents are
        /// updated directly and marked dirty.</summary>
        public void SetDocContent(int i, string content)
        {
            if (i == _active && !Active.IsSettings)
            {
                _code.ReplaceRangeInternal(0, _code.value.Length, content, 0, typing: false);
            }
            else
            {
                _docs[i].Content = content;
                _docs[i].IsDirty = true;
                RebuildTabs();
            }
        }

        // --- Display ---

        void UpdateTitle()
        {
            EnsureDocs();
            titleContent = new GUIContent("ATE - " + (Active.IsDirty ? "*" : "") + Active.DisplayName,
                Active.HasFile ? Active.FilePath : "New unsaved document");
        }

        void UpdateStatus()
        {
            if (_statusLeft == null || _code == null) return;

            if (Active.IsSettings)
            {
                _statusLeft.text = "Settings";
                _statusRight.text = string.Empty;
                return;
            }

            int caret = Mathf.Clamp(_code.cursorIndex, 0, Active.Content.Length);
            int line = 1, col = 1;
            for (int i = 0; i < caret; i++)
            {
                if (Active.Content[i] == '\n') { line++; col = 1; }
                else col++;
            }
            _statusLeft.text = $"Ln {line}, Col {col}";
            _statusRight.text = $"{_formatter.Name}  |  UTF-8{(Active.HasBom ? " BOM" : "")}  |  {Active.EolLabel}";
        }
    }
}
