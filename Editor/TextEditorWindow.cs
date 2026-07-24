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
    /// </summary>
    public class TextEditorWindow : EditorWindow
    {
        const string UssPath = "Packages/com.adkom.text-editor/Editor/UI/TextEditor.uss";

        [SerializeField] TextDocument _doc = new TextDocument();
        [SerializeField] bool _wordWrap = true;

        TextField _textField;
        VisualElement _gutter;
        Label _statusLeft;
        Label _statusRight;

        ITextFormatter _formatter = new PlainTextFormatter();

        [MenuItem("Tools/ADKOM/Text Editor")]
        public static void Open()
        {
            var window = CreateWindow<TextEditorWindow>();
            window.UpdateTitle();
            window.Show();
        }

        void CreateGUI()
        {
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
            _textField.SetValueWithoutNotify(_doc.Content);
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
            UpdateTitle();
            UpdateStatus();

            // Caret moves don't fire value changes; poll cheaply for line:col.
            root.schedule.Execute(UpdateStatus).Every(200);
        }

        static ToolbarButton ToolbarBtn(string text, System.Action onClick) =>
            new ToolbarButton(onClick) { text = text };

        void OnTextChanged(ChangeEvent<string> e)
        {
            _doc.Content = e.newValue;
            if (!_doc.IsDirty)
            {
                _doc.IsDirty = true;
                UpdateTitle();
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

        // --- Commands ---

        void NewFile()
        {
            if (!ConfirmDiscardIfDirty()) return;
            _doc = new TextDocument();
            _textField.SetValueWithoutNotify(_doc.Content);
            UpdateTitle();
            UpdateStatus();
        }

        void OpenFile()
        {
            if (!ConfirmDiscardIfDirty()) return;
            string path = FileService.PromptOpen();
            if (path == null) return;
            _doc.LoadFrom(path);
            _textField.SetValueWithoutNotify(_formatter.Format(_doc.Content));
            UpdateTitle();
            UpdateStatus();
        }

        void SaveFile(bool saveAs)
        {
            bool saved = saveAs ? FileService.SaveAs(_doc) : FileService.Save(_doc);
            if (saved)
            {
                UpdateTitle();
                UpdateStatus();
            }
        }

        /// <summary>Returns true to proceed (saved or discarded), false to cancel.</summary>
        bool ConfirmDiscardIfDirty()
        {
            if (!_doc.IsDirty) return true;
            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Changes",
                $"'{_doc.DisplayName}' has unsaved changes.",
                "Save", "Cancel", "Discard");
            if (choice == 1) return false;               // Cancel
            if (choice == 0) return FileService.Save(_doc); // Save (false if dialog cancelled)
            return true;                                  // Discard
        }

        void OnFocus()
        {
            if (_doc == null || !_doc.FileChangedOnDisk()) return;
            bool reload = EditorUtility.DisplayDialog(
                "File Changed on Disk",
                $"'{_doc.DisplayName}' was modified outside the editor.\n\nReload it? Unsaved changes here will be lost.",
                "Reload", "Keep Mine");
            if (reload)
            {
                _doc.LoadFrom(_doc.FilePath);
                _textField?.SetValueWithoutNotify(_doc.Content);
                UpdateTitle();
            }
            else
            {
                // Stop re-prompting until it changes again.
                _doc.LastKnownWriteTimeUtcTicks = File.GetLastWriteTimeUtc(_doc.FilePath).Ticks;
                _doc.IsDirty = true;
                UpdateTitle();
            }
        }

        void OnDestroy()
        {
            if (_doc != null && _doc.IsDirty)
            {
                bool save = EditorUtility.DisplayDialog(
                    "Unsaved Changes",
                    $"'{_doc.DisplayName}' has unsaved changes.",
                    "Save", "Discard");
                if (save) FileService.Save(_doc);
            }
        }

        // --- Display ---

        void UpdateTitle()
        {
            titleContent = new GUIContent((_doc.IsDirty ? "*" : "") + _doc.DisplayName,
                _doc.HasFile ? _doc.FilePath : "New unsaved document");
        }

        void UpdateStatus()
        {
            if (_statusLeft == null || _textField == null) return;

            int caret = Mathf.Clamp(_textField.cursorIndex, 0, _doc.Content.Length);
            int line = 1, col = 1;
            for (int i = 0; i < caret; i++)
            {
                if (_doc.Content[i] == '\n') { line++; col = 1; }
                else col++;
            }
            _statusLeft.text = $"Ln {line}, Col {col}";
            _statusRight.text = $"{_formatter.Name}  |  UTF-8{(_doc.HasBom ? " BOM" : "")}  |  {_doc.EolLabel}";
        }
    }
}
