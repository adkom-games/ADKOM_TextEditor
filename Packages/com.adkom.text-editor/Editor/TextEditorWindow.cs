using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Dockable Unity Editor text editor window. UIToolkit-based; the layout
    /// reserves a gutter pane for future line numbers, and display rendering
    /// routes through ITextFormatter for future syntax highlighting.
    /// Holds any number of open documents, presented as tabs.
    /// </summary>
    public class TextEditorWindow : EditorWindow
    {
        const string UssPath = "Packages/com.adkom.text-editor/Editor/UI/TextEditor.uss";

        [SerializeField] List<TextDocument> _docs = new List<TextDocument>();
        [SerializeField] int _active;
        [SerializeField] bool _wordWrap = true;
        [SerializeField] bool _showLineNumbers;

        TextField _textField;
        VisualElement _gutter;
        Label _gutterLabel;
        int _gutterLineCount = -1;
        VisualElement _tabBar;
        Label _statusLeft;
        Label _statusRight;

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

            // --- Toolbar ---
            var toolbar = new Toolbar();
            toolbar.Add(ToolbarBtn("New", NewFile));
            toolbar.Add(ToolbarBtn("Open", OpenFile));
            toolbar.Add(ToolbarBtn("Save", () => SaveFile(false)));
            toolbar.Add(ToolbarBtn("Save As", () => SaveFile(true)));
            toolbar.Add(new ToolbarSpacer { flex = true });
            var linesToggle = new ToolbarToggle { text = "Lines", value = _showLineNumbers };
            linesToggle.RegisterValueChangedCallback(e => { _showLineNumbers = e.newValue; ApplyLineNumbers(); });
            toolbar.Add(linesToggle);
            var wrapToggle = new ToolbarToggle { text = "Wrap", value = _wordWrap };
            wrapToggle.RegisterValueChangedCallback(e => { _wordWrap = e.newValue; ApplyWrap(); });
            toolbar.Add(wrapToggle);
            root.Add(toolbar);

            // --- Tab bar ---
            _tabBar = new VisualElement { name = "tab-bar" };
            root.Add(_tabBar);

            // --- Editor area: gutter (future line numbers) + text ---
            var editorRow = new VisualElement { name = "editor-row" };
            editorRow.style.flexGrow = 1;
            editorRow.style.flexDirection = FlexDirection.Row;

            _gutter = new VisualElement { name = "gutter" };
            _gutterLabel = new Label();
            _gutter.Add(_gutterLabel);
            editorRow.Add(_gutter);

            _textField = new TextField { multiline = true, name = "text-area" };
            _textField.style.flexGrow = 1;
            _textField.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _textField.SetValueWithoutNotify(_formatter.Format(Active.Content));
            _textField.RegisterValueChangedCallback(OnTextChanged);
            _textField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            editorRow.Add(_textField);
            root.Add(editorRow);

            // --- Status bar ---
            var status = new VisualElement { name = "status-bar" };
            status.style.flexDirection = FlexDirection.Row;
            status.style.justifyContent = Justify.SpaceBetween;
            _statusLeft = new Label();
            _statusRight = new Label();
            status.Add(_statusLeft);
            status.Add(_statusRight);
            root.Add(status);

            ApplyWrap();
            ApplyLineNumbers();
            RebuildTabs();
            UpdateTitle();
            UpdateStatus();

            // The internal ScrollView exists once the field is built; mirror its
            // vertical offset onto the gutter so numbers track the text.
            var scrollView = _textField.Q<ScrollView>();
            if (scrollView != null)
                scrollView.verticalScroller.valueChanged += _ => SyncGutterScroll();

            // Caret moves don't fire value changes; poll cheaply for line:col.
            root.schedule.Execute(UpdateStatus).Every(200);
        }

        static ToolbarButton ToolbarBtn(string text, System.Action onClick) =>
            new ToolbarButton(onClick) { text = text };

        void OnTextChanged(ChangeEvent<string> e)
        {
            Active.Content = e.newValue;
            UpdateGutter();
            if (!Active.IsDirty)
            {
                Active.IsDirty = true;
                UpdateTitle();
                RebuildTabs();
            }
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.S && (e.ctrlKey || e.commandKey))
            {
                SaveFile(e.shiftKey);
                e.StopPropagation();
            }
        }

        void ApplyWrap()
        {
            if (_textField == null) return;
            var input = _textField.Q(className: "unity-text-field__input") ?? _textField;
            input.style.whiteSpace = _wordWrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
        }

        // --- Line numbers ---

        void ApplyLineNumbers()
        {
            if (_gutter == null) return;
            _gutter.style.display = _showLineNumbers ? DisplayStyle.Flex : DisplayStyle.None;
            _gutterLineCount = -1; // force rebuild
            UpdateGutter();
            SyncGutterScroll();
        }

        void UpdateGutter()
        {
            if (!_showLineNumbers || _gutterLabel == null) return;

            int lines = 1;
            string content = Active.Content;
            for (int i = 0; i < content.Length; i++)
                if (content[i] == '\n') lines++;
            if (lines == _gutterLineCount) return;
            _gutterLineCount = lines;

            var sb = new System.Text.StringBuilder(lines * 3);
            for (int n = 1; n <= lines; n++) sb.Append(n).Append('\n');
            _gutterLabel.text = sb.ToString(0, sb.Length - 1);

            // Match the text input's top padding so row 1 lines up with line 1.
            var input = _textField?.Q(className: "unity-text-field__input");
            if (input != null)
                _gutterLabel.style.marginTop = input.resolvedStyle.paddingTop;
        }

        void SyncGutterScroll()
        {
            if (!_showLineNumbers || _gutterLabel == null || _textField == null) return;
            var scrollView = _textField.Q<ScrollView>();
            if (scrollView != null)
                _gutterLabel.style.translate = new Translate(0, -scrollView.scrollOffset.y);
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
            CheckExternalChange(Active);
            _textField?.SetValueWithoutNotify(_formatter.Format(Active.Content));
            _gutterLineCount = -1;
            UpdateGutter();
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
        /// its tab if already open, reuses an untouched empty tab, or adds a new one.</summary>
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
            bool saved = saveAs ? FileService.SaveAs(Active) : FileService.Save(Active);
            if (saved)
            {
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
                _textField?.SetValueWithoutNotify(_formatter.Format(Active.Content));
                _gutterLineCount = -1;
                UpdateGutter();
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

        // --- Display ---

        void UpdateTitle()
        {
            EnsureDocs();
            titleContent = new GUIContent((Active.IsDirty ? "*" : "") + Active.DisplayName,
                Active.HasFile ? Active.FilePath : "New unsaved document");
        }

        void UpdateStatus()
        {
            if (_statusLeft == null || _textField == null) return;

            int caret = Mathf.Clamp(_textField.cursorIndex, 0, Active.Content.Length);
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
