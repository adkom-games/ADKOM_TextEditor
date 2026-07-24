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

        TextField _textField;
        VisualElement _gutter;
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

        [MenuItem(AssetsMenuPath, true)]
        static bool ValidateOpenSelectedAsset()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return assetPath != null &&
                assetPath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase);
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
            _gutter.style.display = DisplayStyle.None; // reserved for line numbers
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
            RebuildTabs();
            UpdateTitle();
            UpdateStatus();

            // Caret moves don't fire value changes; poll cheaply for line:col.
            root.schedule.Execute(UpdateStatus).Every(200);
        }

        static ToolbarButton ToolbarBtn(string text, System.Action onClick) =>
            new ToolbarButton(onClick) { text = text };

        void OnTextChanged(ChangeEvent<string> e)
        {
            Active.Content = e.newValue;
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
            _textField?.SetValueWithoutNotify(_formatter.Format(Active.Content));
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

            bool activeIsBlank = !Active.HasFile && !Active.IsDirty && Active.Content.Length == 0;
            if (activeIsBlank) _docs[_active] = doc;
            else { _docs.Add(doc); _active = _docs.Count - 1; }
            SwitchTo(_active);
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

        void OnFocus()
        {
            if (_docs == null) return;
            bool any = false;
            foreach (var doc in _docs)
            {
                if (!doc.FileChangedOnDisk()) continue;
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
                any = true;
            }
            if (any)
            {
                _textField?.SetValueWithoutNotify(_formatter.Format(Active.Content));
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
