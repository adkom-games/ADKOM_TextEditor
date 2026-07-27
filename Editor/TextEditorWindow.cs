#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public partial class TextEditorWindow : EditorWindow
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
        // Defaults apply to fresh windows only: Unity's layout serialization
        // restores whatever an existing user already had configured.
        [SerializeField] bool _showLineNumbers = true;
        [SerializeField] bool _wordWrap = true;
        [SerializeField] bool _consoleVisible = true;
        [SerializeField] bool _minimapVisible = true;

        CodeView _code;
        VisualElement _tabBar;
        Label _statusLeft;
        Label _statusRight;

        VisualElement _editorArea;
        MarkdownView _mdView;
        UnityEditor.UIElements.ToolbarButton _mdToggle;
        VisualElement _mdFormatBar;
        Label _emptyHint;
        VisualElement _settingsPane;
        ScrollView _settingsScroll;
        PopupField<string> _settingsTheme;
        EnumField _settingsMode;
        Toggle _settingsLines;
        Toggle _settingsWrap;
        IntegerField _settingsTabSize;
        EnumField _settingsKeymap;
        Toggle _settingsAutoUpdate;
        IntegerField _settingsUpdateFreq;
        PopupField<string> _settingsFallback;
        PopupField<string> _settingsFont;
        IntegerField _settingsFontSize;
        Toggle _settingsSmooth;
        Toggle _settingsMdRendered;
        IntegerField _settingsRecentMax;
        Toggle _settingsTrimSave;
        Toggle _settingsFinalNewline;
        Toggle _settingsSemantics;

        IVisualElementScheduledItem _semanticPending;
        System.Threading.SynchronizationContext _mainCtx;
        VisualElement _updatingOverlay;
        VisualElement _notifyBar;
        Label _notifyLabel;
        VisualElement _notifyButtons;
        VisualElement _consolePane;
        ScrollView _consoleScroll;
        Label _consoleOutput;
        int _consoleVersionShown = -1;
        double _statusHoldUntil;

        // Defensive: clamp a stale index; null when no docs (callers guard
        // with HasDocs, this keeps a future unguarded call from throwing).
        TextDocument Active =>
            _docs.Count == 0 ? null : _docs[Mathf.Clamp(_active, 0, _docs.Count - 1)];

        [MenuItem("Tools/ADKOM/Text Editor %&8")] // Ctrl+Alt+8 (Cmd+Alt+8 on macOS)
        public static void Open()
        {
            var existing = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            var window = existing.Length > 0 ? existing[0] : CreateWindow<TextEditorWindow>();
            window.UpdateTitle();
            window.Show();
            window.Focus();
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

        /// <summary>External-editor entry point: opens (or focuses) the window,
        /// opens the file, and jumps to the 1-based line/column when given.</summary>
        public static void OpenExternal(string path, int line, int column)
        {
            var windows = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            var window = windows.Length > 0 ? windows[0] : CreateWindow<TextEditorWindow>();
            window.Show();
            window.Focus();
            window.OpenPath(Path.GetFullPath(path));
            if (line > 0) window._code?.GoToLine(line, Mathf.Max(1, column));
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

        // An empty window is a valid state: no Untitled doc is auto-created.
        bool HasDocs => _docs.Count > 0;
        bool CanEditDoc => HasDocs && !Active.IsSettings;

        void EnsureDocs()
        {
            _active = HasDocs ? Mathf.Clamp(_active, 0, _docs.Count - 1) : 0;
        }

        void CreateGUI()
        {
            if (_docs.Count == 0) RestoreSession();
            EnsureDocs();
            StartSessionAutosave();

            var root = rootVisualElement;
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);

            root.RegisterCallback<KeyDownEvent>(OnGlobalKeyDown, TrickleDown.TrickleDown);

            // --- Menu bar. GenericMenu.DropDown is Unity's native menu on
            // every platform (Windows/macOS/Linux), and building the menu on
            // click keeps item state (checks, enables, tab list) live. ---
            var toolbar = new Toolbar();
            toolbar.Add(MenuButton(L10n.Tr("File"), FillFileMenu));
            toolbar.Add(MenuButton(L10n.Tr("Edit"), FillEditMenu));
            toolbar.Add(MenuButton(L10n.Tr("View"), FillViewMenu));
            toolbar.Add(MenuButton(L10n.Tr("Tools"), FillToolsMenu));
            toolbar.Add(MenuButton(L10n.Tr("Window"), FillWindowMenu));
            toolbar.Add(MenuButton(L10n.Tr("Help"), FillHelpMenu));
            toolbar.Add(new ToolbarSpacer { flex = true });
            _mdFormatBar = BuildMdFormatBar();
            _mdFormatBar.style.display = DisplayStyle.None; // transient: rendered MD mode only
            toolbar.Add(_mdFormatBar);
            _mdToggle = new ToolbarButton(ToggleMdMode);
            _mdToggle.style.display = DisplayStyle.None; // transient: .md tabs only
            toolbar.Add(_mdToggle);
            var gear = new ToolbarButton(OpenSettings) { tooltip = L10n.Tr("Settings") };
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

            // --- Non-modal notification banner (e.g. file changed on disk).
            // Modal dialogs here would block Unity's main loop — including
            // background tooling — every time the window regains focus. ---
            _notifyBar = new VisualElement { name = "notify-bar" };
            _notifyBar.style.display = DisplayStyle.None;
            _notifyBar.style.flexDirection = FlexDirection.Row;
            _notifyBar.style.alignItems = Align.Center;
            _notifyBar.style.paddingLeft = 8;
            _notifyBar.style.paddingTop = 2;
            _notifyBar.style.paddingBottom = 2;
            _notifyBar.style.backgroundColor = new Color(0.55f, 0.45f, 0.15f, 0.25f);
            _notifyLabel = new Label();
            _notifyLabel.style.flexGrow = 1;
            _notifyLabel.style.whiteSpace = WhiteSpace.Normal;
            _notifyBar.Add(_notifyLabel);
            _notifyButtons = new VisualElement();
            _notifyButtons.style.flexDirection = FlexDirection.Row;
            _notifyBar.Add(_notifyButtons);
            root.Add(_notifyBar);

            // --- Editor area: virtualized code view ---
            _editorArea = new VisualElement { name = "editor-row" };
            _editorArea.style.flexGrow = 1;

            _code = new CodeView { TabSize = EditorConfig.TabSize };
            _code.SetValueWithoutNotify(HasDocs ? Active.Content : string.Empty);
            _code.onValueChanged += OnTextChanged;
            _code.showLineNumbers = _showLineNumbers;
            _code.wordWrap = _wordWrap;
            _code.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _code.onFontSizeChanged += SyncSettingsControls; // zoom gestures
            _code.onNavigateRequest += NavigateToDefinition;  // Ctrl+Click
            _code.onUndoStatus += PostStatus; // "Undid 12 char(s)." feedback
            _code.RegisterCallback<MouseUpEvent>(OnCodeContextMenu);
            _code.minimapVisible = _minimapVisible;
            _mainCtx = System.Threading.SynchronizationContext.Current;
            _editorArea.Add(_code);

            _mdView = new MarkdownView();
            _mdView.style.display = DisplayStyle.None;
            _mdView.onEditBlock += OnMdBlockEdited;
            _mdView.onInsertBlock += OnMdInsertBlock;
            _editorArea.Add(_mdView);

            _emptyHint = new Label(L10n.Tr("No file open.\nFile → New, File → Open…, or right-click a text asset in the Project window."));
            _emptyHint.name = "empty-hint";
            _emptyHint.style.flexGrow = 1;
            _emptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _emptyHint.style.opacity = 0.5f;
            _emptyHint.style.display = DisplayStyle.None;
            _editorArea.Add(_emptyHint);
            root.Add(_editorArea);

            BuildSettingsPane(root);
            BuildConsolePane(root);

            // --- Updating overlay: ATE-only modal. While a package update
            // installs, edits would be lost in the reload — block THIS window
            // without blocking Unity (our modality policy). ---
            _updatingOverlay = new VisualElement { name = "updating-overlay" };
            _updatingOverlay.style.position = Position.Absolute;
            _updatingOverlay.style.left = 0;
            _updatingOverlay.style.top = 0;
            _updatingOverlay.style.right = 0;
            _updatingOverlay.style.bottom = 0;
            _updatingOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            _updatingOverlay.style.display = DisplayStyle.None;
            _updatingOverlay.style.alignItems = Align.Center;
            _updatingOverlay.style.justifyContent = Justify.Center;
            _updatingOverlay.focusable = true;
            var updatingLabel = new Label(
                L10n.Tr("Updating ADKOM Text Editor…\nPlease wait — the editor will reload when the update completes."));
            updatingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            updatingLabel.style.whiteSpace = WhiteSpace.Normal;
            updatingLabel.style.fontSize = 14;
            _updatingOverlay.Add(updatingLabel);
            _updatingOverlay.RegisterCallback<KeyDownEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
            _updatingOverlay.RegisterCallback<PointerDownEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.Add(_updatingOverlay); // last child: renders above everything

            UpdateChecker.onInstallStateChanged += SetUpdatingOverlay;
            if (UpdateChecker.InstallInProgress) SetUpdatingOverlay(true);

            // --- Status bar ---
            var status = new VisualElement { name = "status-bar" };
            status.style.flexDirection = FlexDirection.Row;
            status.style.justifyContent = Justify.SpaceBetween;
            _statusLeft = new Label();
            _statusRight = new Label();
            status.Add(_statusLeft);
            BuildMiniBuffer(status); // emacs-style prompt, between left and right
            status.Add(_statusRight);
            root.Add(status);
            _updatingOverlay.BringToFront(); // above everything, incl. status bar

            ApplyTheme();
            SwitchTo(_active); // also restores settings-tab visibility state

            // Caret moves don't fire value changes; poll cheaply for line:col.
            root.schedule.Execute(UpdateStatus).Every(200);
        }

        static ToolbarButton ToolbarBtn(string text, System.Action onClick) =>
            new ToolbarButton(onClick) { text = text };

        // --- Menu bar ---


        /// <summary>Tools → Options…: opens (or switches to) the Settings tab —
        /// unlike the gear, it never closes it.</summary>
        void OpenSettingsPage()
        {
            int existing = _docs.FindIndex(d => d.IsSettings);
            if (existing >= 0) { SwitchTo(existing); return; }
            _docs.Add(new TextDocument { IsSettings = true });
            SwitchTo(_docs.Count - 1);
        }

        IVisualElementScheduledItem _apiTextPending;
        TextDocument _apiTextDoc;

        void OnTextChanged(string newValue)
        {
            if (!CanEditDoc) return;
            Active.Content = newValue;
            if (ActiveIsMarkdown && Active.MdRendered) _mdView?.Render(newValue);
            // Debounced AteApi.textChanged (typing coalesces into one event).
            _apiTextDoc = Active;
            if (_apiTextPending == null)
                _apiTextPending = rootVisualElement.schedule.Execute(() =>
                {
                    var d = _apiTextDoc;
                    if (d != null && _docs.Contains(d)) Scripting.AteApi.NotifyTextChanged(this, d);
                });
            _apiTextPending.ExecuteLater(400);
            ScheduleSemanticPass();
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
            _code?.SetPalette(palette);
            RefreshFormatter();
        }

        void RefreshFormatter()
        {
            string classifierPath = null;
            if (HasDocs)
            {
                if (Active.HasFile) classifierPath = Active.FilePath;
                else if (Active.VirtualCSharp) classifierPath = "virtual.cs";
                else if (Active.VirtualMarkdown) classifierPath = "virtual.md";
            }
            _code?.SetClassifier(SyntaxClassifiers.ForPath(classifierPath));
            ScheduleSemanticPass();
        }

        // --- Markdown rendered mode ---

        bool ActiveIsMarkdown =>
            HasDocs && !Active.IsSettings &&
            ((Active.HasFile && Active.FilePath.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase))
             || Active.VirtualMarkdown);

        void ToggleMdMode()
        {
            if (!ActiveIsMarkdown) return;
            Active.MdRendered = !Active.MdRendered;
            UpdateMdUi();
        }

        /// <summary>Shows/hides the transient toggle and swaps the code view
        /// against the rendered Markdown view for the active document.</summary>
        void UpdateMdUi()
        {
            bool isMd = ActiveIsMarkdown;
            if (_mdToggle != null)
            {
                _mdToggle.style.display = isMd ? DisplayStyle.Flex : DisplayStyle.None;
                if (isMd)
                {
                    // The label shows the CURRENT mode (status), the tooltip
                    // the action — labeling the action read as the wrong state.
                    _mdToggle.text = Active.MdRendered ? "MD" : "</>";
                    _mdToggle.tooltip = Active.MdRendered
                        ? L10n.Tr("Rendered Markdown — click to switch to source")
                        : L10n.Tr("Markdown source — click to switch to rendered (click a block to edit it)");
                }
            }
            bool rendered = isMd && Active.MdRendered;
            if (_mdFormatBar != null)
                _mdFormatBar.style.display = isMd ? DisplayStyle.Flex : DisplayStyle.None;
            if (_code != null) _code.style.display = rendered || !HasDocs ? DisplayStyle.None : DisplayStyle.Flex;
            if (_mdView != null)
            {
                _mdView.style.display = rendered ? DisplayStyle.Flex : DisplayStyle.None;
                if (rendered)
                {
                    _mdView.SetPalette(CurrentTheme.Current);
                    _mdView.Render(_code.value);
                }
            }
        }

        /// <summary>One button per Markdown element type, shown only while
        /// rendered (WYSIWYG) mode is active. Acts on the open block editor,
        /// or appends a new template block when none is being edited.</summary>
        VisualElement BuildMdFormatBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            (string id, string label, string tip)[] defs =
            {
                ("h1", "H1", "Heading 1"),
                ("h2", "H2", "Heading 2"),
                ("h3", "H3", "Heading 3"),
                ("bold", "B", "Bold"),
                ("italic", "I", "Italic"),
                ("strike", "S", "Strikethrough"),
                ("code", "<>", "Inline code"),
                ("link", "🔗", "Link"),
                ("image", "🖼", "Image"),
                ("ul", "•", "Bullet list"),
                ("ol", "1.", "Numbered list"),
                ("task", "☑", "Task list"),
                ("quote", "❝", "Blockquote"),
                ("codeblock", "{ }", "Code block"),
                ("table", "▦", "Table"),
                ("hr", "―", "Horizontal rule"),
            };
            foreach (var (id, label, tip) in defs)
            {
                string captured = id;
                var b = new ToolbarButton(() => ApplyMdFormat(captured)) { text = label, tooltip = tip };
                b.style.unityTextAlign = TextAnchor.MiddleCenter;
                b.style.paddingLeft = 4;
                b.style.paddingRight = 4;
                if (id == "bold") b.style.unityFontStyleAndWeight = FontStyle.Bold;
                if (id == "italic") b.style.unityFontStyleAndWeight = FontStyle.Italic;
                bar.Add(b);
            }
            return bar;
        }

        /// <summary>Routes a formatting button by mode: rendered mode goes to
        /// the MarkdownView (block editor or new block); source mode acts on
        /// the CodeView selection directly.</summary>
        void ApplyMdFormat(string id)
        {
            if (!ActiveIsMarkdown) return;
            if (Active.MdRendered) { _mdView?.ApplyFormat(id); return; }

            string v = _code.value;
            int a = Mathf.Clamp(Mathf.Min(_code.cursorIndex, _code.selectIndex), 0, v.Length);
            int b = Mathf.Clamp(Mathf.Max(_code.cursorIndex, _code.selectIndex), a, v.Length);

            if (MarkdownView.TryGetInlineWrap(id, out string pre, out string post, out string ph))
            {
                string inner = b > a ? v.Substring(a, b - a) : ph;
                _code.ReplaceRangeInternal(a, b, pre + inner + post,
                    a + pre.Length + inner.Length, typing: false);
                return;
            }
            if (id == "hr" || id == "table")
            {
                // Insert on its own lines after the current line.
                int le = v.IndexOf('\n', b);
                if (le < 0) le = v.Length;
                string ins = (le == 0 || v.Length == 0 ? "" : "\n") + "\n" + MarkdownView.TemplateFor(id) + "\n";
                _code.ReplaceRangeInternal(le, le, ins, le + ins.Length, typing: false);
                return;
            }
            // Line-level transform over the full lines covering the selection.
            int ls = a == 0 ? 0 : v.LastIndexOf('\n', a - 1) + 1;
            int lineEnd = v.IndexOf('\n', b);
            if (lineEnd < 0) lineEnd = v.Length;
            string transformed = MarkdownView.TransformLines(id, v.Substring(ls, lineEnd - ls));
            if (transformed != null)
                _code.ReplaceRangeInternal(ls, lineEnd, transformed, ls + transformed.Length, typing: false);
        }

        /// <summary>New block from a formatting button when no block editor is
        /// open: appended at the end of the document, blank-line separated.</summary>
        void OnMdInsertBlock(string src)
        {
            string v = _code.value;
            string sep = v.Length == 0 ? "" : v.EndsWith("\n\n") ? "" : v.EndsWith("\n") ? "\n" : "\n\n";
            string insert = sep + src + "\n";
            _code.ReplaceRangeInternal(v.Length, v.Length, insert, v.Length + insert.Length, typing: false);
        }

        void OnMdBlockEdited(int start, int end, string replacement)
        {
            // Through the code view so undo/redo and dirty tracking apply;
            // the value-change event re-renders via UpdateMdUi.
            _code.ReplaceRangeInternal(start, end, replacement, start + replacement.Length, typing: false);
        }

        // --- Semantics (optional compiler-backed module) ---


        // --- Console pane (bottom, horizontal tabs; Console is the only
        // tab for now). Closing the tab hides the whole pane; the Window
        // menu shows it again. Visible by default. ---

        void BuildConsolePane(VisualElement root)
        {
            _consolePane = new VisualElement { name = "console-pane" };

            var tabs = new VisualElement { name = "console-tabs" };
            var tab = new VisualElement();
            tab.AddToClassList("console-tab");
            tab.Add(new Label("Console"));
            var close = new Button(() => SetConsoleVisible(false)) { text = "×" };
            close.AddToClassList("tab__close");
            tab.Add(close);
            tabs.Add(tab);
            _consolePane.Add(tabs);

            _consoleScroll = new ScrollView(ScrollViewMode.Vertical) { name = "console-scroll" };
            _consoleOutput = new Label { name = "console-output" };
            _consoleOutput.AddToClassList("code-line");
            _consoleOutput.focusable = true;
            _consoleOutput.selection.isSelectable = true; // select + Ctrl+C
            _consoleScroll.Add(_consoleOutput);
            _consolePane.Add(_consoleScroll);

            root.Add(_consolePane);
            SetConsoleVisible(_consoleVisible);
        }

        void SetConsoleVisible(bool visible)
        {
            _consoleVisible = visible;
            if (_consolePane != null)
                _consolePane.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) _consoleVersionShown = -1; // force refresh
        }

        void PollConsole()
        {
            if (!_consoleVisible || _consoleOutput == null) return;
            if (_consoleVersionShown == AteConsole.Version) return;
            _consoleVersionShown = AteConsole.Version;
            _consoleOutput.text = AteConsole.GetText();
            // Stick to the bottom once the new content has been laid out.
            _consoleScroll.schedule.Execute(() =>
                _consoleScroll.verticalScroller.value = _consoleScroll.verticalScroller.highValue)
                .ExecuteLater(30);
        }

        /// <summary>Status-bar messages also land in the console, and are held
        /// in the bar for a few seconds so the Ln/Col poll doesn't stomp them.</summary>
        void PostStatus(string message)
        {
            AteConsole.Log(message);
            if (_statusLeft != null) _statusLeft.text = message;
            _statusHoldUntil = EditorApplication.timeSinceStartup + 5.0;
        }

        // --- Settings tab ---

        void BuildSettingsPane(VisualElement root)
        {
            // ScrollView so short windows scroll the settings instead of the
            // pane's children spilling over the status bar.
            var scroller = new ScrollView(ScrollViewMode.Vertical) { name = "settings-scroll" };
            scroller.style.flexGrow = 1;
            scroller.style.display = DisplayStyle.None;

            _settingsPane = new VisualElement { name = "settings-pane" };

            var title = new Label(L10n.Tr("Editor Settings"));
            title.AddToClassList("settings-title");
            _settingsPane.Add(title);

            var themeNames = new List<string>();
            foreach (var t in HighlightTheme.All) themeNames.Add(t.Name);
            _settingsTheme = new PopupField<string>(L10n.Tr("Color Theme"), themeNames, CurrentTheme.Name);
            _settingsTheme.RegisterValueChangedCallback(e =>
            {
                CurrentTheme = HighlightTheme.ByName(e.newValue);
                ApplyTheme();
            });
            _settingsPane.Add(_settingsTheme);

            _settingsMode = new EnumField(L10n.Tr("Light/Dark Mode"), CurrentThemeMode);
            _settingsMode.RegisterValueChangedCallback(e =>
            {
                CurrentThemeMode = (ThemeMode)e.newValue;
                ApplyTheme();
            });
            _settingsPane.Add(_settingsMode);

            _settingsLines = new Toggle(L10n.Tr("Line Numbers")) { value = _showLineNumbers };
            _settingsLines.RegisterValueChangedCallback(e =>
            {
                _showLineNumbers = e.newValue;
                if (_code != null) _code.showLineNumbers = e.newValue;
            });
            _settingsPane.Add(_settingsLines);

            _settingsWrap = new Toggle(L10n.Tr("Word Wrap")) { value = _wordWrap };
            _settingsWrap.RegisterValueChangedCallback(e =>
            {
                _wordWrap = e.newValue;
                if (_code != null) _code.wordWrap = e.newValue;
            });
            _settingsPane.Add(_settingsWrap);

            _settingsTabSize = new IntegerField(L10n.Tr("Tab Size")) { value = EditorConfig.TabSize };
            _settingsTabSize.RegisterValueChangedCallback(e =>
            {
                EditorConfig.TabSize = e.newValue;
                _settingsTabSize.SetValueWithoutNotify(EditorConfig.TabSize); // clamp echo
                if (_code != null) _code.TabSize = EditorConfig.TabSize;
            });
            _settingsTabSize.tooltip = L10n.Tr("Spaces a tab renders as. Applies to files opened after the change.");
            _settingsPane.Add(_settingsTabSize);

            _settingsKeymap = new EnumField(L10n.Tr("Keyboard Layout"), EditorConfig.Keymap);
            _settingsKeymap.RegisterValueChangedCallback(e =>
                EditorConfig.Keymap = (KeymapLayout)e.newValue);
            _settingsKeymap.tooltip = L10n.Tr("Which IDE's default shortcuts to use for the commands this editor supports.");
            _settingsPane.Add(_settingsKeymap);

            var fontNames = new List<string> { "(Default Monospace)" };
            fontNames.AddRange(Font.GetOSInstalledFontNames().OrderBy(n => n));
            string currentFont = string.IsNullOrEmpty(EditorConfig.FontName) ? fontNames[0]
                : (fontNames.Contains(EditorConfig.FontName) ? EditorConfig.FontName : fontNames[0]);
            _settingsFont = new PopupField<string>(L10n.Tr("Font"), fontNames, currentFont);
            _settingsFont.RegisterValueChangedCallback(e =>
            {
                EditorConfig.FontName = e.newValue == fontNames[0] ? string.Empty : e.newValue;
                _code?.ApplyFontConfig();
            });
            _settingsPane.Add(_settingsFont);

            _settingsFontSize = new IntegerField(L10n.Tr("Font Size")) { value = EditorConfig.FontSize };
            _settingsFontSize.RegisterValueChangedCallback(e =>
            {
                EditorConfig.FontSize = e.newValue;
                _settingsFontSize.SetValueWithoutNotify(EditorConfig.FontSize); // clamp echo (8..40)
                _code?.ApplyFontConfig();
            });
            _settingsFontSize.tooltip = L10n.Tr("Also: Ctrl+MouseWheel, Ctrl+'+'/'-', Ctrl+0 resets.");
            _settingsPane.Add(_settingsFontSize);

            // External-editor fallback (used when ATE is Unity's selected
            // external script editor and a request isn't a text file).
            var editors = Unity.CodeEditor.CodeEditor.Editor.GetFoundScriptEditorPaths()
                .Where(kv => kv.Key != EditorApplication.applicationPath).ToList();
            var fallbackLabels = new List<string> { "(OS default application)" };
            fallbackLabels.AddRange(editors.Select(kv => kv.Value));
            string currentLabel = fallbackLabels[0];
            foreach (var kv in editors)
                if (kv.Key == EditorConfig.FallbackEditorPath) currentLabel = kv.Value;
            _settingsFallback = new PopupField<string>(L10n.Tr("External Fallback"), fallbackLabels, currentLabel);
            _settingsFallback.RegisterValueChangedCallback(e =>
            {
                int idx = fallbackLabels.IndexOf(e.newValue);
                EditorConfig.FallbackEditorPath = idx <= 0 ? string.Empty : editors[idx - 1].Key;
            });
            _settingsFallback.tooltip = L10n.Tr("When ATE is the External Script Editor, non-text requests (solutions, binaries, project sync) are forwarded here.");
            _settingsPane.Add(_settingsFallback);

            _settingsSemantics = new Toggle(L10n.Tr("Semantic Features")) { value = EditorConfig.SemanticsEnabled };
            _settingsSemantics.RegisterValueChangedCallback(e =>
            {
                EditorConfig.SemanticsEnabled = e.newValue;
                if (e.newValue) SemanticSetup.EnsureInstalled();
                else ScheduleSemanticPassCancel();
                RefreshFormatter();
            });
            _settingsSemantics.tooltip = L10n.Tr("Compiler-accurate colors and Go to Definition. If the project has no Roslyn, enabling installs the bundled MIT-licensed Roslyn assemblies (see THIRD-PARTY-NOTICES).");
            _settingsPane.Add(_settingsSemantics);

            _settingsSmooth = new Toggle(L10n.Tr("Smooth Scrolling")) { value = EditorConfig.SmoothScrolling };
            _settingsSmooth.RegisterValueChangedCallback(e => EditorConfig.SmoothScrolling = e.newValue);
            _settingsSmooth.tooltip = L10n.Tr("Animate wheel scrolling instead of stepping line by line.");
            _settingsPane.Add(_settingsSmooth);

            _settingsMdRendered = new Toggle(L10n.Tr("Open Markdown Rendered")) { value = EditorConfig.MdOpenRendered };
            _settingsMdRendered.RegisterValueChangedCallback(e => EditorConfig.MdOpenRendered = e.newValue);
            _settingsMdRendered.tooltip = L10n.Tr("Default view when opening .md files: rendered (WYSIWYG) when on, source when off. The MD/source toggle still switches per tab.");
            _settingsPane.Add(_settingsMdRendered);

            _settingsTrimSave = new Toggle(L10n.Tr("Trim Trailing Whitespace on Save")) { value = EditorConfig.TrimTrailingOnSave };
            _settingsTrimSave.RegisterValueChangedCallback(e => EditorConfig.TrimTrailingOnSave = e.newValue);
            _settingsTrimSave.tooltip = L10n.Tr("Remove spaces and tabs at line ends when saving (per project).");
            _settingsPane.Add(_settingsTrimSave);

            _settingsFinalNewline = new Toggle(L10n.Tr("Ensure Final Newline on Save")) { value = EditorConfig.FinalNewlineOnSave };
            _settingsFinalNewline.RegisterValueChangedCallback(e => EditorConfig.FinalNewlineOnSave = e.newValue);
            _settingsFinalNewline.tooltip = L10n.Tr("Guarantee the file ends with exactly one newline when saving (per project).");
            _settingsPane.Add(_settingsFinalNewline);

            _settingsRecentMax = new IntegerField(L10n.Tr("Recent Files Count")) { value = EditorConfig.RecentFilesMax };
            _settingsRecentMax.RegisterValueChangedCallback(e =>
            {
                EditorConfig.RecentFilesMax = e.newValue;
                _settingsRecentMax.SetValueWithoutNotify(EditorConfig.RecentFilesMax); // clamp echo (1-30)
            });
            _settingsRecentMax.tooltip = L10n.Tr("How many entries File → Recent Files keeps (1-30).");
            _settingsPane.Add(_settingsRecentMax);

            _settingsAutoUpdate = new Toggle(L10n.Tr("Automatic Updates")) { value = EditorConfig.AutoUpdate };
            _settingsAutoUpdate.RegisterValueChangedCallback(e => EditorConfig.AutoUpdate = e.newValue);
            _settingsAutoUpdate.tooltip = L10n.Tr("Check GitHub for new releases and offer to install them.");
            _settingsPane.Add(_settingsAutoUpdate);

            _settingsUpdateFreq = new IntegerField(L10n.Tr("Check Every (days)")) { value = EditorConfig.UpdateFrequencyDays };
            _settingsUpdateFreq.RegisterValueChangedCallback(e =>
            {
                EditorConfig.UpdateFrequencyDays = e.newValue;
                _settingsUpdateFreq.SetValueWithoutNotify(EditorConfig.UpdateFrequencyDays); // clamp echo (min 1)
            });
            _settingsUpdateFreq.tooltip = L10n.Tr("Days between automatic update checks. 1 = daily; never more often than once per day.");
            _settingsPane.Add(_settingsUpdateFreq);

            var checkNowRow = new VisualElement();
            checkNowRow.style.flexDirection = FlexDirection.Row;
            var updateStatus = new Label();
            updateStatus.style.opacity = 0.75f;
            updateStatus.style.marginLeft = 8;
            updateStatus.style.alignSelf = Align.Center;
            var checkNow = new Button(() =>
            {
                updateStatus.text = L10n.Tr("Checking…");
                UpdateChecker.CheckNow(manual: true, r => updateStatus.text = r);
            }) { text = L10n.Tr("Check for Updates Now") };
            checkNowRow.Add(checkNow);
            checkNowRow.Add(updateStatus);
            _settingsPane.Add(checkNowRow);

            var versionLabel = new Label(L10n.Tr("Installed version: ") + UpdateChecker.CurrentVersion());
            versionLabel.style.opacity = 0.6f;
            versionLabel.style.marginTop = 4;
            _settingsPane.Add(versionLabel);

            scroller.Add(_settingsPane);
            _settingsScroll = scroller;
            root.Add(scroller);
        }


        /// <summary>Opens (or switches to and refreshes) a virtual document —
        /// named content with no backing file — and focuses it.</summary>
        void OpenVirtualDoc(string title, string content, bool csharp,
            bool markdown = false, bool rendered = false)
        {
            int existing = _docs.FindIndex(d => d.VirtualName == title);
            if (existing >= 0)
            {
                _docs[existing].Content = content;
                _docs[existing].IsDirty = false;
                _docs[existing].VirtualMarkdown = markdown;
                _docs[existing].MdRendered = markdown && rendered;
                SwitchTo(existing);
            }
            else
            {
                var vdoc = new TextDocument
                {
                    Content = content, VirtualName = title, VirtualCSharp = csharp,
                    VirtualMarkdown = markdown, MdRendered = markdown && rendered
                };
                _docs.Add(vdoc);
                Scripting.AteApi.NotifyOpened(this, vdoc);
                SwitchTo(_docs.Count - 1);
            }
        }

        /// <summary>Shown after an update: the packaged RELEASE-NOTES.md as a
        /// focused virtual tab, always in rendered (WYSIWYG) Markdown mode.</summary>
        public static void ShowReleaseNotes(string version)
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == "com.adkom.text-editor");
            if (pkg == null) return;
            string path = Path.Combine(pkg.resolvedPath, "RELEASE-NOTES.md");
            if (!File.Exists(path)) return;
            string text = File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");

            var existing = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            var window = existing.Length > 0 ? existing[0] : CreateWindow<TextEditorWindow>();
            window.Show();
            window.Focus();
            // CreateGUI may not have run yet on a fresh window; defer a frame.
            window.rootVisualElement.schedule.Execute(() =>
                window.OpenVirtualDoc("Release Notes " + version, text, csharp: false,
                    markdown: true, rendered: true)).ExecuteLater(0);
        }

        /// <summary>Gear behavior: open the settings tab, bring it to the front
        /// if it exists in the background, or close it when already frontmost.</summary>
        void OpenSettings()
        {
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
            _settingsAutoUpdate?.SetValueWithoutNotify(EditorConfig.AutoUpdate);
            _settingsUpdateFreq?.SetValueWithoutNotify(EditorConfig.UpdateFrequencyDays);
            _settingsFontSize?.SetValueWithoutNotify(EditorConfig.FontSize);
            _settingsSmooth?.SetValueWithoutNotify(EditorConfig.SmoothScrolling);
            _settingsSemantics?.SetValueWithoutNotify(EditorConfig.SemanticsEnabled);
            _settingsMdRendered?.SetValueWithoutNotify(EditorConfig.MdOpenRendered);
            _settingsRecentMax?.SetValueWithoutNotify(EditorConfig.RecentFilesMax);
            _settingsTrimSave?.SetValueWithoutNotify(EditorConfig.TrimTrailingOnSave);
            _settingsFinalNewline?.SetValueWithoutNotify(EditorConfig.FinalNewlineOnSave);
        }

        // --- Tabs ---


        // --- Drag-to-reorder tabs. Left-drag past a small threshold enters
        // drag mode; crossing another tab's midpoint moves the dragged
        // document there live (browser-style). A plain click still switches
        // (handled on MouseDown before the threshold is reached). ---

        TextDocument _dragDoc;
        bool _dragActive;
        Vector2 _dragStart;
        const float DragThreshold = 5f;


        // --- Commands ---

        void NewFile()
        {
            var doc = new TextDocument();
            _docs.Add(doc);
            Scripting.AteApi.NotifyOpened(this, doc);
            SwitchTo(_docs.Count - 1);
        }

        void OpenFile()
        {
            string path = FileService.PromptOpen();
            if (path != null) OpenPath(path);
        }

        /// <summary>Recent Files menu entry: files gone from disk are dropped
        /// from the list with a console note instead of throwing.</summary>
        void OpenRecent(string path)
        {
            if (!File.Exists(path))
            {
                EditorConfig.RemoveRecentFile(path);
                AteConsole.Warn("[ADKOM Text Editor] Recent file no longer exists, removed from the list: " + path);
                PostStatus(L10n.Tr("Recent file no longer exists: ") + Path.GetFileName(path));
                return;
            }
            OpenPath(path);
        }

        /// <summary>Opens the file at <paramref name="path"/> in a tab: switches to
        /// its tab if already open, otherwise adds a new one.</summary>
        public void OpenPath(string path)
        {
            EnsureDocs();
            string full = Path.GetFullPath(path);
            int existing = _docs.FindIndex(d => d.HasFile && FileService.PathsEqual(d.FilePath, full));
            if (existing >= 0)
            {
                SwitchTo(existing);
                return;
            }

            var doc = new TextDocument();
            doc.LoadFrom(full);
            if (full.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase))
                doc.MdRendered = EditorConfig.MdOpenRendered;
            _docs.Add(doc);
            EditorConfig.AddRecentFile(full);
            Scripting.AteApi.NotifyOpened(this, doc);
            SwitchTo(_docs.Count - 1);
        }

        void SaveFile(bool saveAs)
        {
            if (!CanEditDoc) return;
            bool saved = saveAs ? FileService.SaveAs(Active) : FileService.Save(Active);
            if (saved)
            {
                // Save transforms (trim/final newline) may have changed the model.
                _code?.SetValueWithoutNotify(Active.Content);
                _code?.BreakUndoGroup(); // undo-past-save is a deliberate step
                if (saveAs && Active.HasFile) EditorConfig.AddRecentFile(Path.GetFullPath(Active.FilePath));
                RefreshFormatter(); // Save As can change the extension/language
                RebuildTabs();
                UpdateTitle();
                UpdateStatus();
            }
        }


        // --- Status-bar mini-buffer (emacs-style): a static prompt plus an
        // inline edit field in the status bar. Generic so future commands can
        // reuse it; Goto Line is the first client. ---

        VisualElement _miniBuffer;
        Label _miniPrompt;
        TextField _miniInput;
        System.Action<string> _miniCommit;
        bool _miniDigitsOnly;


        void OnLostFocus()
        {
            _code?.BreakUndoGroup();
        }

        void OnFocus()
        {
            // Inactive tabs are checked when they are activated (SwitchTo).
            if (_docs == null || _docs.Count == 0 || _active >= _docs.Count) return;
            CheckExternalChange(Active); // non-modal banner
        }

        void SetUpdatingOverlay(bool updating)
        {
            if (_updatingOverlay == null) return;
            _updatingOverlay.style.display = updating ? DisplayStyle.Flex : DisplayStyle.None;
            if (updating) _updatingOverlay.Focus(); // pull keyboard focus off the code view
        }

        void OnDisable()
        {
            UpdateChecker.onInstallStateChanged -= SetUpdatingOverlay;
        }


        const long SessionAutosaveMs = 30000;


        // --- Keyboard commands ---
        // Three layouts (Settings → Keyboard Layout): Visual Studio, VS Code,
        // Rider — covering the defaults that apply to this editor's features.

        /// <summary>Window-level commands; works from any tab including Settings.</summary>
        void OnGlobalKeyDown(KeyDownEvent e)
        {
            if (UpdateChecker.InstallInProgress) { e.StopImmediatePropagation(); return; }
            bool ctrl = e.ctrlKey || e.commandKey;

            // All global commands live in the command table (see
            // TextEditorWindow.Commands.cs — bindings, handlers, and menu
            // hints defined in one place).
            bool handled = DispatchCommands(e, CmdScope.Global);

            // Clipboard/selection anywhere in the window (menu bar, tab bar,
            // gutter...) — but never steal from real text inputs (settings
            // fields, the Markdown block editor) or selectable labels (the
            // console), whose own/native handling must win.
            if (!handled && ctrl && !e.altKey && !e.shiftKey && CanEditDoc && !TargetIsTextInput(e))
            {
                switch (e.keyCode)
                {
                    case KeyCode.X: _code.Cut(); handled = true; break;
                    case KeyCode.C: _code.Copy(); handled = true; break;
                    case KeyCode.V: _code.Paste(); handled = true; break;
                    case KeyCode.A: _code.SelectAll(); handled = true; break;
                }
            }

            if (handled)
            {
                e.StopImmediatePropagation();
            }
        }

        static bool TargetIsTextInput(KeyDownEvent e)
        {
            var v = e.target as VisualElement;
            if (v == null) return false;
            if (v is TextElement) return true; // selectable labels handle copy natively
            return v is TextField || v.GetFirstAncestorOfType<TextField>() != null;
        }

        /// <summary>Text-editing commands on the code view (trickle-down, so
        /// they win over CodeView's own typing/navigation handling).</summary>
        void OnKeyDown(KeyDownEvent e)
        {
            if (UpdateChecker.InstallInProgress) { e.StopImmediatePropagation(); return; }
            // Swallow the character-only Tab event; the keyCode event acts.
            if (e.keyCode == KeyCode.None && e.character == '\t')
            {
                e.StopImmediatePropagation();
                return;
            }

            if (!CanEditDoc) return;

            bool ctrl = e.ctrlKey || e.commandKey;
            bool handled;

            // Tab/Shift+Tab must run before CodeView's own handling, so they
            // stay a special case; every other editing command comes from the
            // command table (TextEditorWindow.Commands.cs).
            if (e.keyCode == KeyCode.Tab && !ctrl && !e.altKey)
            {
                if (e.shiftKey) UnindentSelection(); else InsertTab();
                handled = true;
            }
            else handled = DispatchCommands(e, CmdScope.Editor);

            if (handled)
            {
                e.StopImmediatePropagation();
            }
        }


        void SaveAll()
        {
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

        // --- AteApi plumbing (the stable facade lives in Scripting/AteApi.cs;
        // these internals are its only touchpoints). ---


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
            if (!HasDocs)
            {
                titleContent = new GUIContent("ATE", "ADKOM Text Editor");
                return;
            }
            EnsureDocs();
            titleContent = new GUIContent("ATE - " + (Active.IsDirty ? "*" : "") + Active.DisplayName,
                Active.HasFile ? Active.FilePath : "New unsaved document");
        }

        void UpdateStatus()
        {
            if (_statusLeft == null || _code == null) return;
            PollConsole();
            if (EditorApplication.timeSinceStartup < _statusHoldUntil) return; // message pinned

            if (!HasDocs)
            {
                // A blank bar reads as "the status bar disappeared".
                _statusLeft.text = L10n.Tr("No file open");
                _statusRight.text = string.Empty;
                return;
            }

            if (Active.IsSettings)
            {
                _statusLeft.text = L10n.Tr("Settings");
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
            _statusRight.text = $"{_code.ClassifierName ?? "Plain Text"}  |  UTF-8{(Active.HasBom ? " BOM" : "")}  |  {Active.EolLabel}";
        }
    }
}
#endif
