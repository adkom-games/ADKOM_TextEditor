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
        [SerializeField] bool _wordWrap = true;
        [SerializeField] bool _showLineNumbers;

        TextField _textField;
        VisualElement _gutter;
        Label _gutterLabel;
        string _gutterSource;
        float _gutterWidth;
        bool _gutterWrap;
        VisualElement _tabBar;
        Label _statusLeft;
        Label _statusRight;

        VisualElement _editorRow;
        VisualElement _settingsPane;
        PopupField<string> _settingsTheme;
        EnumField _settingsMode;
        Toggle _settingsLines;
        Toggle _settingsWrap;
        IntegerField _settingsTabSize;
        EnumField _settingsKeymap;

        Label _highlight;
        VisualElement _highlightClip;
        string _highlightSource;
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

            // --- Editor area: gutter (future line numbers) + text ---
            var editorRow = _editorRow = new VisualElement { name = "editor-row" };
            editorRow.style.flexGrow = 1;
            editorRow.style.flexDirection = FlexDirection.Row;

            _gutter = new VisualElement { name = "gutter" };
            _gutterLabel = new Label();
            _gutter.Add(_gutterLabel);
            editorRow.Add(_gutter);

            _textField = new TextField { multiline = true, name = "text-area" };
            _textField.style.flexGrow = 1;
            _textField.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _textField.SetValueWithoutNotify(Active.Content);
            _textField.RegisterValueChangedCallback(OnTextChanged);
            _textField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            editorRow.Add(_textField);
            root.Add(editorRow);

            // Syntax-highlight overlay: an editable TextField cannot render rich
            // text, so the colored markup is drawn by a Label floated over the
            // field; when active, the editable glyphs go transparent and only
            // the caret/selection of the field remain visible.
            // The overlay lives entirely OUTSIDE the TextField hierarchy: both
            // TextInput and the inner editable element are TextElements in
            // Unity 6, and any TextElement nested in one joins its text
            // generation and hangs the editor. A clipping container tracks the
            // field's rect; the label tracks the inner element (scroll included).
            _highlightClip = new VisualElement { name = "highlight-clip" };
            _highlightClip.pickingMode = PickingMode.Ignore;
            _highlightClip.style.position = Position.Absolute;
            _highlightClip.style.overflow = Overflow.Hidden;
            _highlight = new Label { name = "highlight-overlay", enableRichText = true };
            _highlight.pickingMode = PickingMode.Ignore;
            _highlight.style.position = Position.Absolute;
            _highlightClip.Add(_highlight);
            // Insert BEFORE the field so it draws underneath: the field's glyphs
            // are transparent when highlighting, and its caret and selection
            // must render above the colored overlay to stay visible.
            editorRow.Insert(editorRow.IndexOf(_textField), _highlightClip);

            BuildSettingsPane(root);
            _textField.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                SyncHighlightRect();
                UpdateGutter(); // wrap width changed: recompute wrapped-row padding
            });

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
            ApplyTheme(); // includes RefreshHighlight
            SwitchTo(_active); // also restores settings-tab visibility state

            // The internal ScrollView exists once the field is built; mirror its
            // vertical offset onto the gutter so numbers track the text.
            var scrollView = _textField.Q<ScrollView>();
            if (scrollView != null)
            {
                scrollView.verticalScroller.valueChanged += _ => { SyncGutterScroll(); SyncHighlightRect(); };
                scrollView.horizontalScroller.valueChanged += _ => SyncHighlightRect();
            }

            // Caret moves don't fire value changes; poll cheaply for line:col.
            root.schedule.Execute(UpdateStatus).Every(200);
        }

        static ToolbarButton ToolbarBtn(string text, System.Action onClick) =>
            new ToolbarButton(onClick) { text = text };

        void OnTextChanged(ChangeEvent<string> e)
        {
            Active.Content = e.newValue;
            UpdateGutter();
            RefreshHighlight();
            if (!Active.IsDirty)
            {
                Active.IsDirty = true;
                UpdateTitle();
                RebuildTabs();
            }
        }

        // --- Keyboard commands ---
        // Two layouts (Settings → Keyboard Layout), covering the Visual Studio
        // and Rider defaults that apply to this editor's feature set.

        /// <summary>Window-level commands; works from any tab including Settings.</summary>
        void OnGlobalKeyDown(KeyDownEvent e)
        {
            bool ctrl = e.ctrlKey || e.commandKey;
            bool handled = false;
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

        /// <summary>Text-editing commands on the text field.</summary>
        void OnKeyDown(KeyDownEvent e)
        {
            // Unity 6 delivers keys as TWO events: keyCode-only, then
            // character-only. Our Tab handling acts on the keyCode event;
            // the character event would still insert a literal '\t' into the
            // text (which also skews click-to-caret mapping afterwards), so
            // swallow it here.
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
            else if (!ctrl && !e.altKey && !e.shiftKey &&
                     (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow))
            {
                handled = TryTabStopNavigate(e.keyCode == KeyCode.RightArrow ? 1 : -1);
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
            int saved = _active;
            for (int i = 0; i < _docs.Count; i++)
            {
                var doc = _docs[i];
                if (doc.IsSettings || !doc.IsDirty) continue;
                FileService.Save(doc); // prompts Save As for untitled docs
            }
            _active = saved;
            RebuildTabs();
            UpdateTitle();
            UpdateStatus();
        }

        // --- Text editing helpers (operate on the field; value-change events
        // keep the document, gutter, and highlight in sync) ---

        void GetSelection(out int start, out int end)
        {
            int a = _textField.cursorIndex, b = _textField.selectIndex;
            start = Mathf.Min(a, b);
            end = Mathf.Max(a, b);
        }

        void ReplaceRange(int start, int end, string replacement, int caret)
        {
            string v = _textField.value;
            _textField.value = v.Substring(0, start) + replacement + v.Substring(end);
            int c = Mathf.Clamp(caret, 0, _textField.value.Length);
            _textField.cursorIndex = c;
            _textField.selectIndex = c;
            // The text engine may clamp the caret against its pre-edit state
            // this frame; re-assert once it has processed the new value.
            _textField.schedule.Execute(() =>
            {
                _textField.cursorIndex = c;
                _textField.selectIndex = c;
            }).ExecuteLater(0);
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
            string v = _textField.value;
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
            string v = _textField.value;
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
            _textField.cursorIndex = first;
            _textField.selectIndex = Mathf.Min(_textField.value.Length,
                end + lineCount * tabSize);
        }

        void UnindentSelection()
        {
            GetSelection(out int start, out int end);
            string v = _textField.value;
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

        /// <summary>Caret left/right jumps whole tab stops while inside leading
        /// indentation, so space indents feel like tabs. Returns handled.</summary>
        bool TryTabStopNavigate(int dir)
        {
            GetSelection(out int start, out int end);
            if (start != end) return false;
            string v = _textField.value;
            int tabSize = EditorConfig.TabSize;
            int lineStart = LineStartOf(v, start);
            int col = start - lineStart;

            // Only act inside a pure-space leading run.
            for (int i = lineStart; i < start; i++)
                if (v[i] != ' ') return false;

            if (dir < 0)
            {
                if (col == 0) return false;
                int target = ((col - 1) / tabSize) * tabSize;
                _textField.cursorIndex = lineStart + target;
                _textField.selectIndex = lineStart + target;
                return true;
            }

            int need = tabSize - (col % tabSize);
            for (int i = 0; i < need; i++)
                if (start + i >= v.Length || v[start + i] != ' ') return false;
            _textField.cursorIndex = start + need;
            _textField.selectIndex = start + need;
            return true;
        }

        void DuplicateLine()
        {
            string v = _textField.value;
            GetSelection(out int start, out _);
            int ls = LineStartOf(v, start), le = LineEndOf(v, start);
            string line = v.Substring(ls, le - ls);
            ReplaceRange(le, le, "\n" + line, start + line.Length + 1);
        }

        void DeleteLine()
        {
            string v = _textField.value;
            GetSelection(out int start, out _);
            int ls = LineStartOf(v, start), le = LineEndOf(v, start);
            int removeEnd = le < v.Length ? le + 1 : le;
            int removeStart = le >= v.Length && ls > 0 ? ls - 1 : ls;
            int col = start - ls;
            ReplaceRange(removeStart, removeEnd, string.Empty, removeStart + col);
        }

        void MoveLine(int dir)
        {
            string v = _textField.value;
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
            string v = _textField.value;
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

        void ApplyWrap()
        {
            if (_textField == null) return;
            var input = _textField.Q(className: "unity-text-field__input") ?? _textField;
            input.style.whiteSpace = _wordWrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            if (_highlight != null)
                _highlight.style.whiteSpace = _wordWrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            UpdateGutter(); // wrap mode changes the gutter's row padding
        }

        // --- Theme ---

        void ApplyTheme()
        {
            HighlightTheme.Mode = CurrentThemeMode;
            var palette = CurrentTheme.Current;
            TextFormatters.Theme = CurrentTheme;

            // Background lives on the row (behind the overlay); the input is
            // transparent so the overlay underneath shows through it.
            var input = _textField?.Q(className: "unity-text-field__input");
            if (input != null) input.style.backgroundColor = Color.clear;
            if (_editorRow != null) _editorRow.style.backgroundColor = palette.BackgroundColor;
            if (_textField != null)
                _textField.textSelection.selectionColor = palette.SelectionColor;
            if (_gutter != null) _gutter.style.backgroundColor = palette.BackgroundColor;
            if (_gutterLabel != null) _gutterLabel.style.color = palette.TextColor;
            if (_highlight != null) _highlight.style.color = palette.TextColor;
            if (_textField != null) _textField.textSelection.cursorColor = palette.TextColor;

            _highlightSource = null; // token colors changed; re-tokenize
            RefreshHighlight();
        }

        // --- Syntax highlighting ---

        void SyncHighlightRect()
        {
            if (_highlight == null || _highlightClip == null || _textField == null) return;
            var textElement = _textField.Q<TextElement>();
            if (textElement == null) return;

            // Clip container covers the field (in editor-row space).
            var fieldRect = _textField.layout;
            if (float.IsNaN(fieldRect.width)) return; // first frame, no layout yet
            _highlightClip.style.left = fieldRect.x;
            _highlightClip.style.top = fieldRect.y;
            _highlightClip.style.width = fieldRect.width;
            _highlightClip.style.height = fieldRect.height;

            // Label tracks the editable element, scroll offset included, via
            // world positions (also absorbs the input's internal padding).
            var teWorld = textElement.worldBound;
            var fieldWorld = _textField.worldBound;
            _highlight.style.left = teWorld.x - fieldWorld.x;
            _highlight.style.top = teWorld.y - fieldWorld.y;
            _highlight.style.width = teWorld.width;
        }

        void RefreshHighlight()
        {
            _formatter = TextFormatters.ForPath(Active.HasFile ? Active.FilePath : null);
            var textElement = _textField?.Q<TextElement>();
            if (textElement == null || _highlight == null) return;

            // Very large files fall back to plain rendering: a single rich-text
            // label with hundreds of KB of markup is asking too much of the
            // text engine, and editing responsiveness matters more there.
            const int MaxHighlightChars = 200_000;
            bool rich = !(_formatter is PlainTextFormatter) &&
                Active.Content.Length <= MaxHighlightChars;
            _highlightClip.style.display = rich ? DisplayStyle.Flex : DisplayStyle.None;
            if (rich)
            {
                textElement.style.color = Color.clear;
                if (!ReferenceEquals(_highlightSource, Active.Content))
                {
                    _highlightSource = Active.Content;
                    _highlight.text = _formatter.Format(Active.Content);
                }
                SyncHighlightRect();
                // Transparent glyphs must not take the caret with them.
                _textField.textSelection.cursorColor = CurrentTheme.Current.TextColor;
            }
            else
            {
                textElement.style.color = CurrentTheme.Current.TextColor;
                _highlight.text = string.Empty;
                _highlightSource = null;
            }
        }

        // --- Line numbers ---

        void ApplyLineNumbers()
        {
            if (_gutter == null) return;
            _gutter.style.display = _showLineNumbers ? DisplayStyle.Flex : DisplayStyle.None;
            _gutterSource = null; // force rebuild
            UpdateGutter();
            SyncGutterScroll();
        }

        void UpdateGutter()
        {
            if (!_showLineNumbers || _gutterLabel == null) return;

            var textElement = _textField?.Q<TextElement>();
            string content = Active.Content;
            float wrapWidth = textElement?.resolvedStyle.width ?? -1f;

            if (ReferenceEquals(_gutterSource, content) && _gutterWrap == _wordWrap &&
                (!_wordWrap || Mathf.Approximately(_gutterWidth, wrapWidth)))
                return;
            _gutterSource = content;
            _gutterWrap = _wordWrap;
            _gutterWidth = wrapWidth;

            string[] lines = content.Split('\n');
            var sb = new System.Text.StringBuilder(lines.Length * 4);

            // With wrap on, a logical line can occupy several visual rows; pad
            // with blank gutter rows so the next number sits beside the next
            // logical line. Measured per line, so cap it for huge files.
            bool perLine = _wordWrap && textElement != null && wrapWidth > 0 &&
                lines.Length <= 5000;
            float rowHeight = 0;
            if (perLine)
            {
                rowHeight = textElement.MeasureTextSize("0", 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined).y;
                if (rowHeight <= 0) perLine = false;
            }

            for (int n = 0; n < lines.Length; n++)
            {
                sb.Append(n + 1);
                int rows = 1;
                if (perLine && lines[n].Length > 0)
                {
                    float h = textElement.MeasureTextSize(lines[n], wrapWidth,
                        VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
                    rows = Mathf.Max(1, Mathf.RoundToInt(h / rowHeight));
                }
                for (int r = 0; r < rows && n < lines.Length - 1; r++) sb.Append('\n');
            }
            _gutterLabel.text = sb.ToString();

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
                ApplyLineNumbers();
            });
            _settingsPane.Add(_settingsLines);

            _settingsWrap = new Toggle("Word Wrap") { value = _wordWrap };
            _settingsWrap.RegisterValueChangedCallback(e =>
            {
                _wordWrap = e.newValue;
                ApplyWrap();
            });
            _settingsPane.Add(_settingsWrap);

            _settingsTabSize = new IntegerField("Tab Size") { value = EditorConfig.TabSize };
            _settingsTabSize.RegisterValueChangedCallback(e =>
            {
                EditorConfig.TabSize = e.newValue;
                _settingsTabSize.SetValueWithoutNotify(EditorConfig.TabSize); // clamp echo
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
            if (_editorRow != null)
                _editorRow.style.display = settings ? DisplayStyle.None : DisplayStyle.Flex;
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
            _textField?.SetValueWithoutNotify(Active.Content);
            _gutterSource = null;
            UpdateGutter();
            RefreshHighlight();
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
            if (Active.IsSettings) return;
            bool saved = saveAs ? FileService.SaveAs(Active) : FileService.Save(Active);
            if (saved)
            {
                RefreshHighlight(); // Save As can change the extension/language
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
                _textField?.SetValueWithoutNotify(Active.Content);
                _gutterSource = null;
                UpdateGutter();
                RefreshHighlight();
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
            titleContent = new GUIContent("ATE - " + (Active.IsDirty ? "*" : "") + Active.DisplayName,
                Active.HasFile ? Active.FilePath : "New unsaved document");
        }

        void UpdateStatus()
        {
            if (_statusLeft == null || _textField == null) return;

            if (Active.IsSettings)
            {
                _statusLeft.text = "Settings";
                _statusRight.text = string.Empty;
                return;
            }

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
