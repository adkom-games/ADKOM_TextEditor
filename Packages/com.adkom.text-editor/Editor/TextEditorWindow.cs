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
        [SerializeField] bool _showHiddenChars;
        [SerializeField] bool _consoleVisible = true;
        // Per-tab visibility of the console AREA's views — each View menu
        // toggle flips exactly one of these, independently.
        [SerializeField] bool _consoleTabVisible = true;
        [SerializeField] bool _searchTabVisible = true;
        [SerializeField] bool _bmTabVisible;
        [SerializeField] bool _minimapVisible = true;
        [SerializeField] bool _indentGuides = true;

        CodeView _code;
        VisualElement _tabBar;
        Label _statusLeft;
        Label _statusRight;

        VisualElement _editorArea;
        MarkdownView _mdView;
        UnityEditor.UIElements.ToolbarButton _mdToggle;
        UnityEditor.UIElements.ToolbarButton _mdLockBtn;
        UnityEditor.UIElements.ToolbarButton _updateBtn;
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
        Toggle _settingsMdLocked;
        IntegerField _settingsRecentMax;
        UnityEditor.UIElements.ColorField _settingsTabColor;
        Toggle _settingsAutoClose;
        Toggle _settingsAutoReload;
        Toggle _settingsCopilot;
        VisualElement _settingsCopilotRow;
        Label _settingsCopilotStatus;
        Button _settingsCopilotSignIn;
        VisualElement _settingsUnityAiRow;
        Label _settingsUnityAiStatus;
        Label _settingsPragmaStatus;
        Button _settingsPragmaFix;
        Label _settingsDiffToolStatus;
        Button _settingsDiffToolBtn;
        Toggle _settingsTrimSave;
        Toggle _settingsFinalNewline;
        Toggle _settingsSemantics;

        IVisualElementScheduledItem _semanticPending;
        System.Threading.SynchronizationContext _mainCtx;
        VisualElement _updatingOverlay;
        VisualElement _notifyBar;
        Label _notifyLabel;
        VisualElement _notifyButtons;
        TextField _notifyInput;
        VisualElement _consolePane;
        VisualElement _consoleHost; // filter row + list; toggled as one by the tabs
        ListView _consoleList;
        TextField _consoleFilter;
        Label _consoleHeader;
        readonly System.Collections.Generic.List<string> _consoleLines = new System.Collections.Generic.List<string>();
        // What the ListView actually shows: _consoleLines through the Filter box.
        readonly System.Collections.Generic.List<string> _consoleShown = new System.Collections.Generic.List<string>();
        int _consoleVersionShown = -1;
        double _statusHoldUntil;

        // Defensive: clamp a stale index; null when no docs (callers guard
        // with HasDocs, this keeps a future unguarded call from throwing).
        TextDocument Active =>
            _docs.Count == 0 ? null : _docs[Mathf.Clamp(_active, 0, _docs.Count - 1)];

        // Discoverability besides Tools/ADKOM. (This alone does NOT reach the
        // dock's "Add Tab" menu — that list is fixed; see AteAddTabIntegration.)
        [MenuItem("Window/ADKOM Text Editor %&8")] // Ctrl+Alt+8 shown here
        static void OpenFromWindowMenu() => Open();

        /// <summary>Puts ATE in every dock's tab context / "⋮" menu under
        /// "Add Tab". The Add Tab list itself is a fixed set of built-in pane
        /// types (HostView.GetPaneTypes), so third-party windows can only get
        /// there via the internal static HostView.populateDefaultMenuItems
        /// event — Action&lt;GenericMenu, EditorWindow&gt;, raised while the
        /// menu is built. Same-named submenus merge, so our item lands inside
        /// the existing "Add Tab". Reflection-based and failure-tolerant: if
        /// a future Unity removes the event, we silently lose the entry.</summary>
        [InitializeOnLoad]
        static class AteAddTabIntegration
        {
            static AteAddTabIntegration()
            {
                try
                {
                    var hostT = typeof(EditorWindow).Assembly.GetType("UnityEditor.HostView");
                    var ev = hostT?.GetEvent("populateDefaultMenuItems",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (ev == null) return;
                    System.Action<GenericMenu, EditorWindow> handler = (menu, view) =>
                    {
                        if (view is TextEditorWindow) return; // already one here
                        menu.AddItem(new GUIContent("Add Tab/ADKOM Text Editor"), false, () =>
                        {
                            var existing = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
                            if (existing.Length > 0) { existing[0].Show(); existing[0].Focus(); return; }
                            var win = CreateInstance<TextEditorWindow>();
                            try
                            {
                                // Dock as a sibling tab of the clicked pane.
                                var parentF = typeof(EditorWindow).GetField("m_Parent",
                                    System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.NonPublic);
                                object dock = view != null ? parentF?.GetValue(view) : null;
                                var addTab = dock?.GetType().GetMethod("AddTab",
                                    new[] { typeof(EditorWindow), typeof(bool) });
                                if (addTab != null) addTab.Invoke(dock, new object[] { win, true });
                                else win.Show();
                            }
                            catch (System.Exception) { win.Show(); }
                            win.UpdateTitle();
                            win.Focus();
                        });
                    };
                    // The add accessor is internal — AddEventHandler refuses
                    // non-public accessors, so invoke it directly.
                    ev.GetAddMethod(true).Invoke(null, new object[] { handler });
                }
                catch (System.Exception) { /* menu entry is best-effort */ }
            }
        }

        [MenuItem("Tools/ADKOM/Text Editor")] // shortcut lives on the Window menu entry
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
        /// <summary>ATE's own link scheme: "ate://open?path=&lt;escaped&gt;&amp;line=N"
        /// opens the file IN ATE at that line instead of handing it to the OS
        /// (Application.OpenURL would launch the .cs in another app). Used by
        /// the addon security report's file/line links. Returns true when the
        /// URL was ours and was handled.</summary>
        public static bool TryOpenAteLink(string url)
        {
            const string prefix = "ate://open?";
            if (string.IsNullOrEmpty(url) || !url.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return false;
            string path = null;
            int line = 1;
            foreach (var part in url.Substring(prefix.Length).Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                if (k == "path") path = System.Uri.UnescapeDataString(v);
                else if (k == "line") int.TryParse(v, out line);
            }
            if (string.IsNullOrEmpty(path)) return false;
            OpenExternal(path, Mathf.Max(1, line), 1);
            return true;
        }

        /// <summary>Builds an ate:// link for a file:line location.</summary>
        internal static string AteLink(string path, int line) =>
            "ate://open?path=" + System.Uri.EscapeDataString(path ?? "") + "&line=" + line;

        public static void OpenExternal(string path, int line, int column)
        {
            var windows = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            var window = windows.Length > 0 ? windows[0] : CreateWindow<TextEditorWindow>();
            window.Show();
            window.Focus();
            window.PushNavLocation();
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
            // Never let Unity's modal close-prompt arm itself (a serialized
            // leftover from the reverted hasUnsavedChanges approach would
            // block the main loop + MCP on close).
            hasUnsavedChanges = false;
            if (_docs.Count == 0) RestoreSession();
            EnsureDocs();
            StartSessionAutosave();

            var root = rootVisualElement;
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);

            root.RegisterCallback<KeyDownEvent>(OnGlobalKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(OnGlobalKeyUp, TrickleDown.TrickleDown);
            // Undo histories survive domain reloads: the active document's
            // live world is parked on it just before the domain serializes.
            AssemblyReloadEvents.beforeAssemblyReload += SyncUndoWorldForReload;

            // --- Menu bar. GenericMenu.DropDown is Unity's native menu on
            // every platform (Windows/macOS/Linux), and building the menu on
            // click keeps item state (checks, enables, tab list) live. ---
            var toolbar = new Toolbar();
            VisualElement Menu(string title, System.Action<GenericMenu> fill, string tip)
            { var b = MenuButton(title, fill); b.tooltip = tip; return b; }
            toolbar.Add(Menu(L10n.Tr("File"), FillFileMenu, L10n.Tr("Open, save, and manage files and recent documents.")));
            toolbar.Add(Menu(L10n.Tr("Edit"), FillEditMenu, L10n.Tr("Editing, search, selection, and navigation commands.")));
            toolbar.Add(Menu(L10n.Tr("View"), FillViewMenu, L10n.Tr("Toggle editor views, folding, and themes.")));
            toolbar.Add(Menu(L10n.Tr("Tools"), FillToolsMenu, L10n.Tr("Language tools, snippets, add-ons, and utilities.")));
            toolbar.Add(Menu(L10n.Tr("Window"), FillWindowMenu, L10n.Tr("Switch tabs and open ATE windows.")));
            // Built on click like every menu, so the symbol lists are always
            // fresh for the current tab.
            toolbar.Add(Menu(L10n.Tr("Section"), FillSectionMenu, L10n.Tr("Jump to a class, property, method, or bookmark in the current tab.")));
            toolbar.Add(Menu(L10n.Tr("Games"), FillGamesMenu, L10n.Tr("Play Zork and the installed addon games.")));
            toolbar.Add(Menu(L10n.Tr("Help"), FillHelpMenu, L10n.Tr("Documentation, release notes, and support.")));
            toolbar.Add(new ToolbarSpacer { flex = true });
            _mdFormatBar = BuildMdFormatBar();
            _mdFormatBar.style.display = DisplayStyle.None; // transient: rendered MD mode only
            toolbar.Add(_mdFormatBar);
            _mdLockBtn = new ToolbarButton(ToggleMdLock);
            _mdLockBtn.style.display = DisplayStyle.None; // transient: rendered MD mode only
            _mdLockBtn.style.flexShrink = 0;
            // The 🔒/🔓 emoji comes from a fallback font and renders WIDER
            // than the toolbar font measures it, clipping the glyph to half —
            // give the button an explicit width instead of a measured one.
            _mdLockBtn.style.minWidth = 26;
            _mdLockBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            toolbar.Add(_mdLockBtn);
            _mdToggle = new ToolbarButton(ToggleMdMode)
            { tooltip = L10n.Tr("Switch this Markdown tab between rendered and source view.") };
            _mdToggle.style.display = DisplayStyle.None; // transient: .md tabs only
            _mdToggle.style.flexShrink = 0;
            toolbar.Add(_mdToggle);
            // Update-available icon: pinned immediately left of the gear —
            // transient bars (MD toolbar etc.) are added BEFORE it above, so
            // they appear to its left and never displace it.
            _updateBtn = new ToolbarButton(OnUpdateIconClicked)
            { tooltip = L10n.Tr("A new ATE version is available — click to update.") };
            _updateBtn.style.flexShrink = 0; // the MD bar shrinks, never this icon
            var dlTex = EditorGUIUtility.IconContent("Download-Available").image;
            if (dlTex != null)
            {
                var dlIcon = new Image { image = dlTex, scaleMode = ScaleMode.ScaleToFit };
                dlIcon.style.width = 16;
                dlIcon.style.height = 16;
                dlIcon.tintColor = new Color(0.35f, 0.9f, 0.35f);
                _updateBtn.Add(dlIcon);
            }
            else
            {
                _updateBtn.text = "⭳";
                _updateBtn.style.color = new Color(0.35f, 0.9f, 0.35f);
            }
            RefreshUpdateIcon(UpdateChecker.AvailableVersion);
            toolbar.Add(_updateBtn);
            var gear = new ToolbarButton(OpenSettings) { tooltip = L10n.Tr("Settings") };
            gear.style.flexShrink = 0; // like the update icon: never squeezed out
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
            // RED, mostly opaque: these are must-see moments (sign-in codes,
            // file conflicts). COLUMN layout: a wrapping Label inside a flex
            // ROW measures one line tall and clips when it wraps (classic
            // UIToolkit trap — "squished" sign-in code, Cary 2026-07-27);
            // stacking message-over-buttons sidesteps it entirely.
            _notifyBar = new VisualElement { name = "notify-bar" };
            _notifyBar.style.display = DisplayStyle.None;
            _notifyBar.style.flexDirection = FlexDirection.Column;
            // NEVER let the window's column layout compress the banner: the
            // editor area's tall intrinsic content otherwise squeezes it flat
            // (this — not text wrapping — was the actual squish).
            _notifyBar.style.flexShrink = 0;
            _notifyBar.style.paddingLeft = 10;
            _notifyBar.style.paddingRight = 10;
            _notifyBar.style.paddingTop = 8;
            _notifyBar.style.paddingBottom = 8;
            _notifyBar.style.backgroundColor = new Color(0.72f, 0.13f, 0.13f, 0.92f);
            _notifyLabel = new Label();
            _notifyLabel.style.whiteSpace = WhiteSpace.Normal;
            _notifyLabel.style.color = Color.white;
            _notifyLabel.style.fontSize = 13;
            _notifyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _notifyBar.Add(_notifyLabel);
            // Inline confirmation entry (typed addon consent): its own row
            // IN the banner, above the buttons — the status-bar mini-buffer
            // proved invisible for decisions this important. A sibling of
            // _notifyButtons (which ShowBanner clears), so it persists.
            _notifyInput = new TextField
            { tooltip = L10n.Tr("Type a value and press Enter; Escape cancels.") };
            _notifyInput.style.minWidth = 220;
            _notifyInput.style.alignSelf = Align.FlexEnd;
            _notifyInput.style.marginTop = 6;
            _notifyInput.style.display = DisplayStyle.None;
            _notifyInput.RegisterCallback<KeyDownEvent>(OnConsentInputKey, TrickleDown.TrickleDown);
            _notifyBar.Add(_notifyInput);
            _notifyButtons = new VisualElement();
            _notifyButtons.style.flexDirection = FlexDirection.Row;
            _notifyButtons.style.justifyContent = Justify.FlexEnd;
            _notifyButtons.style.marginTop = 6;
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
            _code.showHiddenChars = _showHiddenChars;
            _code.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _code.onFontSizeChanged += SyncSettingsControls; // zoom gestures
            _code.onNavigateRequest += NavigateToDefinition;  // Ctrl+Click
            _code.onUndoStatus += PostStatusBarOnly; // "Undid 12 char(s)." — bar only, never the console
            _code.isLineBookmarked = line => CanEditDoc && Active.Bookmarks.Contains(line);
            _copilotPending = rootVisualElement.schedule.Execute(RequestCopilotGhost);
            _copilotPending.Pause();
            if (EditorConfig.CopilotEnabled) CopilotService.Start();
            CopilotService.onStatusChanged += OnCopilotStatus;
            _code.onLineDelta += OnCodeLineDelta;
            _code.completionTextSources = () =>
            {
                var texts = new List<string>();
                foreach (var d in _docs)
                    if (!d.IsSettings && d != Active && d.Content != null) texts.Add(d.Content);
                return texts;
            };
            _code.requestSemanticCompletions = RequestSemanticCompletions;
            _code.RegisterCallback<MouseUpEvent>(OnCodeContextMenu);
            // Zoom on a font-overridden document adjusts the override; the
            // document remembers it so tab switches round-trip the size.
            _code.onFontOverrideSizeChanged = s =>
            {
                if (HasDocs && Active.FontSize > 0) Active.FontSize = s;
            };
            _code.minimapVisible = _minimapVisible;
            _code.showIndentGuides = _indentGuides;
            _mainCtx = System.Threading.SynchronizationContext.Current;
            // Post-reload, CreateGUI runs before the functional Unity context
            // is current — a context captured now can swallow Posts (async git
            // results would never land). Re-capture the working one.
            AteMainCtx.WhenReady(ctx => { if (this != null) _mainCtx = ctx; });
            _editorArea.Add(_code);

            _mdView = new MarkdownView();
            _mdView.style.display = DisplayStyle.None;
            _mdView.onEditBlock += OnMdBlockEdited;
            _mdView.onInsertBlock += OnMdInsertBlock;
            _mdView.onUnlockRequest += () =>
            { if (ActiveIsMarkdown && Active.MdLocked) ToggleMdLock(); };
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
            UpdateChecker.onAvailableVersionChanged += RefreshUpdateIcon;

            // --- Status bar ---
            var status = new VisualElement { name = "status-bar" };
            status.style.flexDirection = FlexDirection.Row;
            status.style.justifyContent = Justify.SpaceBetween;
            _statusLeft = new Label { tooltip = L10n.Tr("Status messages. Everything shown here is also kept in the Console pane.") };
            _statusRight = new Label { tooltip = L10n.Tr("Caret position, and the document's indentation, line-ending, and encoding info.") };
            status.Add(_statusLeft);
            BuildMiniBuffer(status); // emacs-style prompt, between left and right
            status.Add(_statusRight);
            root.Add(status);
            _updatingOverlay.BringToFront(); // above everything, incl. status bar

            ApplyTheme();
            SwitchTo(_active); // also restores settings-tab visibility state

            // Caret moves don't fire value changes; poll cheaply for line:col.
            root.schedule.Execute(UpdateStatus).Every(200);
            // Read/write occurrence highlighting follows the caret the same way.
            root.schedule.Execute(PollOccurrences).Every(300);

            // Z-Machine resume: games snapshotted before a domain reload (or
            // editor quit) rebuild around their surviving transcript tabs once
            // the UI and session are up. Slightly deferred so layout exists
            // (the screen reads the viewport width for wrapping). Stateful
            // addons (API 1.2) get their stored state back the same way.
            root.schedule.Execute(() =>
            {
                AteZMachine.ZMachineGame.Rehydrate(this);
                Scripting.AteAddonManager.DeliverPendingStates();
            }).ExecuteLater(300);
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
            _code.ClearGhost();
            if (EditorConfig.CopilotEnabled && CopilotService.Status == CopilotService.State.Ready)
                _copilotPending?.ExecuteLater(350);
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
            if (HasDocs && Active.GameMode)
            {
                // Game mode: overlay colors only, no syntax highlighting.
                _code?.SetClassifier(null);
                return;
            }
            if (HasDocs)
            {
                if (Active.HasFile) classifierPath = Active.FilePath;
                else if (Active.VirtualCSharp) classifierPath = "virtual.cs";
                else if (Active.VirtualMarkdown) classifierPath = "virtual.md";
            }
            _code?.SetClassifier(SyntaxClassifiers.ForPath(classifierPath));
            // Never show the previous document's (or a disabled provider's)
            // underlines; the pass repopulates them for the new context.
            _code?.ClearDiagnostics();
            if (_code != null) _code.spellEnabled = EditorConfig.SpellCheckEnabled;
            _code?.ApplyGitMarks(null); // stale marks belong to the previous doc
            RefreshGitMarksAsync();
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

        /// <summary>Per-tab read-only toggle for rendered Markdown. Locked
        /// (default, Settings → Open Markdown Locked): clicks select text for
        /// copying instead of opening block editors.</summary>
        void ToggleMdLock()
        {
            if (!ActiveIsMarkdown) return;
            Active.MdLocked = !Active.MdLocked;
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
            bool locked = rendered && Active.MdLocked;
            if (_mdLockBtn != null)
            {
                // Rendered mode only: source view is inherently editable, a
                // lock there would be dead weight. Same status/action split as
                // the MD toggle: label = current state, tooltip = the action.
                _mdLockBtn.style.display = rendered ? DisplayStyle.Flex : DisplayStyle.None;
                if (rendered)
                {
                    _mdLockBtn.text = locked ? "🔒" : "🔓";
                    _mdLockBtn.tooltip = locked
                        ? L10n.Tr("Locked (read-only): clicks select text for copying — click to allow editing")
                        : L10n.Tr("Unlocked: clicks open block editors — click to make this tab read-only");
                }
            }
            if (_mdFormatBar != null) // formatting edits, so it hides while locked
                _mdFormatBar.style.display = isMd && !locked ? DisplayStyle.Flex : DisplayStyle.None;
            if (_code != null) _code.style.display = rendered || !HasDocs ? DisplayStyle.None : DisplayStyle.Flex;
            if (_mdView != null)
            {
                _mdView.style.display = rendered ? DisplayStyle.Flex : DisplayStyle.None;
                if (rendered)
                {
                    _mdView.Locked = locked;
                    _mdView.SetPalette(CurrentTheme.Current);
                    _mdView.BaseDir = Active.HasFile
                        ? System.IO.Path.GetDirectoryName(Active.FilePath) : null;
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
            // On narrow windows the bar gives up its own space (clipping its
            // rightmost buttons) instead of squeezing the pinned lock/MD/
            // update/gear buttons to its right out of the toolbar.
            bar.style.flexShrink = 1;
            bar.style.overflow = Overflow.Hidden;
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
                b.style.flexShrink = 0; // whole buttons clip; none get squished
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
            if (Active.MdRendered && Active.MdLocked) return; // read-only tab
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

        VisualElement _consoleSplitter;

        void BuildConsolePane(VisualElement root)
        {
            // Drag handle above the console: resizes the pane (persisted).
            _consoleSplitter = new VisualElement { name = "console-splitter",
                tooltip = L10n.Tr("Drag to resize the console pane.") };
            _consoleSplitter.style.height = 5;
            _consoleSplitter.style.flexShrink = 0;
            float dragStartY = 0, dragStartH = 0;
            bool dragging = false;
            _consoleSplitter.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true;
                dragStartY = e.position.y;
                dragStartH = _consolePane.resolvedStyle.height;
                _consoleSplitter.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            _consoleSplitter.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                float maxH = Mathf.Max(80f, rootVisualElement.contentRect.height - 160f);
                float h = Mathf.Clamp(dragStartH + (dragStartY - e.position.y), 60f, maxH);
                _consolePane.style.height = h;
            });
            _consoleSplitter.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                _consoleSplitter.ReleasePointer(e.pointerId);
                EditorConfig.ConsoleHeight = _consolePane.resolvedStyle.height;
            });
            root.Add(_consoleSplitter);

            _consolePane = new VisualElement { name = "console-pane" };
            _consolePane.style.height = Mathf.Max(60f, EditorConfig.ConsoleHeight);

            var tabs = new VisualElement { name = "console-tabs" };
            // Every view tab carries an × that hides ONLY that view; for the
            // toggleable views it flips the View menu setting. Right-click on
            // a tab may offer a context menu (Clear).
            Button TabClose(System.Action hide, string tip)
            {
                var b = new Button(() => hide()) { text = "×", tooltip = tip };
                b.AddToClassList("tab__close");
                return b;
            }
            string hideTip = L10n.Tr("Hide this view (the View menu shows it again).");
            void TabPointer(VisualElement tab, int index, System.Action<GenericMenu> fillContext)
            {
                tab.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1 && fillContext != null)
                    {
                        var cm = new GenericMenu();
                        fillContext(cm);
                        cm.ShowAsContext();
                    }
                    else SelectConsoleTab(index);
                    e.StopPropagation();
                });
            }

            _consoleTab = new VisualElement { tooltip = L10n.Tr("Every ATE message, timestamped. Click a row; Ctrl+C or right-click → Copy Line copies it.") };
            _consoleTab.AddToClassList("console-tab");
            _consoleTab.Add(new Label(L10n.Tr("Console")));
            TabPointer(_consoleTab, 0, cm => cm.AddItem(new GUIContent(L10n.Tr("Clear")), false, AteConsole.Clear));
            _consoleTab.Add(TabClose(() => ToggleConsoleAreaTab(0), hideTip));
            tabs.Add(_consoleTab);
            _searchTab = new VisualElement { tooltip = L10n.Tr("Hits from Find All, Find in Files, and Find All References. Click a row to jump to it.") };
            _searchTab.AddToClassList("console-tab");
            _searchTab.Add(new Label(L10n.Tr("Search Results")));
            TabPointer(_searchTab, 1, cm => cm.AddItem(new GUIContent(L10n.Tr("Clear")), false, ClearSearchResults));
            _searchTab.Add(TabClose(() => ToggleConsoleAreaTab(1), hideTip));
            tabs.Add(_searchTab);
            // Bookmarks: hidden by default; View Bookmarks (or the View menu
            // toggle) reveals it.
            _bmTab = new VisualElement { tooltip = L10n.Tr("The bookmarked lines of every open document. Click a row to jump to it.") };
            _bmTab.AddToClassList("console-tab");
            _bmTab.Add(new Label(L10n.Tr("Bookmarks")));
            TabPointer(_bmTab, 4, null);
            _bmTab.Add(TabClose(() => ToggleConsoleAreaTab(4), hideTip));
            tabs.Add(_bmTab);
            // Scanner Results: appears only when the addon security scanner
            // has findings to show; its label carries the script's name.
            _scannerTab = new VisualElement { tooltip = L10n.Tr("Findings from the addon security scanner.") };
            _scannerTab.AddToClassList("console-tab");
            _scannerTabLabel = new Label(L10n.Tr("Scanner Results"));
            _scannerTab.Add(_scannerTabLabel);
            TabPointer(_scannerTab, 2, null);
            _scannerTab.Add(TabClose(() =>
            { _scannerTab.style.display = DisplayStyle.None; ApplyConsoleTabVisibility(); },
                L10n.Tr("Hide this view.")));
            _scannerTab.style.display = DisplayStyle.None;
            tabs.Add(_scannerTab);
            // Game maps: ONE TAB PER RUNNING GAME (built-in Z-Machine with
            // the auto-mapper on), labeled with the game's tab title. The
            // tabs live in this strip section; contents share _mapHost.
            _mapTabsHost = new VisualElement { name = "game-map-tabs",
                style = { flexDirection = FlexDirection.Row } };
            tabs.Add(_mapTabsHost);
            _consolePane.Add(tabs);

            // Filter row above the rows, mirroring the Search Results pane:
            // a case-insensitive substring over the whole line.
            _consoleHost = new VisualElement { name = "console-host",
                style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            var consoleTop = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
            _consoleHeader = new Label(); // "N of M shown" while filtering
            _consoleHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            _consoleHeader.style.paddingLeft = 4;
            _consoleHeader.style.flexGrow = 1;
            _consoleHeader.style.whiteSpace = WhiteSpace.NoWrap;
            _consoleHeader.style.overflow = Overflow.Hidden;
            consoleTop.Add(_consoleHeader);
            consoleTop.Add(new Label(L10n.Tr("Filter")) { style = { marginRight = 2 } });
            _consoleFilter = new TextField { name = "console-filter", style = { width = 160, marginRight = 4 },
                tooltip = L10n.Tr("Show only console lines containing this text.") };
            _consoleFilter.RegisterValueChangedCallback(_ => RefreshConsoleView());
            consoleTop.Add(_consoleFilter);
            _consoleHost.Add(consoleTop);

            // Per-line rows in a framed monospace subview with alternating
            // background tones. Single selection by design: the active line
            // is copied whole — Ctrl+C or right-click → Copy Line — with no
            // multiline or sub-line selection.
            _consoleList = new ListView
            {
                name = "console-list",
                itemsSource = _consoleShown,
                fixedItemHeight = 16,
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    var l = new Label();
                    l.AddToClassList("code-line");
                    l.style.fontSize = 11;
                    l.style.paddingLeft = 6;
                    l.style.unityTextAlign = TextAnchor.MiddleLeft;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.style.overflow = Overflow.Hidden;
                    l.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 1 || !(l.userData is int idx)) return;
                        if (idx < 0 || idx >= _consoleShown.Count) return;
                        _consoleList.SetSelection(idx); // the clicked row IS the active line
                        string lineText = _consoleShown[idx];
                        var cm = new GenericMenu();
                        cm.AddItem(new GUIContent(L10n.Tr("Copy Line")), false,
                            () => CopyConsoleLine(lineText));
                        cm.AddItem(new GUIContent(L10n.Tr("Clear")), false, AteConsole.Clear);
                        cm.ShowAsContext();
                        e.StopPropagation();
                    });
                    return l;
                },
            };
            _consoleList.bindItem = (e, i) =>
            {
                var l = (Label)e;
                l.text = i < _consoleShown.Count ? _consoleShown[i] : "";
                l.userData = i; // row index for the right-click menu
                // Zebra on the label itself: the wrapper element carries the
                // selection highlight, which must stay visible underneath.
                AteViewStyle.Zebra(l, i);
            };
            _consoleList.style.flexGrow = 1;
            AteViewStyle.Frame(_consoleList);
            AteViewStyle.Mono(_consoleList);
            // Explicit Ctrl+C: the window-level key handling must never eat a
            // copy from the console. Copies the active line only.
            _consoleList.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.C)
                {
                    int i = _consoleList.selectedIndex;
                    if (i >= 0 && i < _consoleShown.Count) CopyConsoleLine(_consoleShown[i]);
                    e.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
            _consoleHost.Add(_consoleList);
            _consolePane.Add(_consoleHost);

            _searchPane = new VisualElement { name = "search-results-pane",
                style = { display = DisplayStyle.None, flexGrow = 1, flexDirection = FlexDirection.Column } };
            var searchTop = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
            _searchHeader = new Label(); // empty until the first result set lands
            _searchHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            _searchHeader.style.paddingLeft = 4;
            _searchHeader.style.flexGrow = 1;
            _searchHeader.style.whiteSpace = WhiteSpace.NoWrap;
            _searchHeader.style.overflow = Overflow.Hidden;
            searchTop.Add(_searchHeader);
            searchTop.Add(new Label(L10n.Tr("Filter")) { style = { marginRight = 2 } });
            _searchFilter = new TextField { name = "search-filter", style = { width = 160, marginRight = 4 },
                tooltip = L10n.Tr("Show only rows whose file name, path, or text contains this.") };
            _searchFilter.RegisterValueChangedCallback(_ => RenderSearchResults());
            searchTop.Add(_searchFilter);
            _searchPane.Add(searchTop);
            _searchScroll = new ScrollView(ScrollViewMode.Vertical) { name = "search-results-scroll", style = { flexGrow = 1 } };
            AteViewStyle.Frame(_searchScroll);
            AteViewStyle.Mono(_searchScroll);
            _searchPane.Add(_searchScroll);
            _consolePane.Add(_searchPane);

            BuildBookmarksPane(_consolePane); // mirror of the search pane

            _scannerScroll = new ScrollView(ScrollViewMode.Vertical) { name = "scanner-results-scroll" };
            AteViewStyle.Frame(_scannerScroll);
            AteViewStyle.Mono(_scannerScroll);
            _scannerScroll.style.display = DisplayStyle.None;
            _scannerHeader = new Label();
            _scannerHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            _scannerHeader.style.paddingLeft = 4;
            _scannerScroll.Add(_scannerHeader);
            _consolePane.Add(_scannerScroll);

            _mapHost = new VisualElement { name = "game-map-host", style = { flexGrow = 1, display = DisplayStyle.None } };
            _consolePane.Add(_mapHost);
            SelectConsoleTab(0);

            root.Add(_consolePane);
            ApplyConsoleTabVisibility();
            SetConsoleVisible(_consoleVisible);
        }

        /// <summary>True when the console-area tab at <paramref name="index"/>
        /// is currently offered in the tab strip. Scanner and Map are
        /// content-driven; the rest follow their View menu toggle.</summary>
        bool ConsoleTabOffered(int index)
        {
            switch (index)
            {
                case 0: return _consoleTabVisible;
                case 1: return _searchTabVisible;
                case 2: return _scannerTab != null && _scannerTab.style.display.value == DisplayStyle.Flex;
                case 3:
                    foreach (var t in _gameMapTabs)
                        if (t.Tab.style.display.value == DisplayStyle.Flex) return true;
                    return false;
                case 4: return _bmTabVisible;
                default: return false;
            }
        }

        int FirstOfferedConsoleTab()
        {
            foreach (int i in new[] { 0, 1, 4, 2, 3 })
                if (ConsoleTabOffered(i)) return i;
            return -1;
        }

        /// <summary>Applies the per-tab visibility flags to the tab strip.
        /// When the active tab just went hidden, the first offered tab takes
        /// over; with none left the whole pane hides.</summary>
        void ApplyConsoleTabVisibility()
        {
            if (_consoleTab == null) return;
            _consoleTab.style.display = _consoleTabVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _searchTab.style.display = _searchTabVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _bmTab.style.display = _bmTabVisible ? DisplayStyle.Flex : DisplayStyle.None;
            // The active game-map tab went away (hidden or removed): fall to
            // another visible map tab before giving up on index 3 entirely.
            if (_activeConsoleTab == 3 &&
                (_activeMapTab == null || _activeMapTab.Tab.style.display.value != DisplayStyle.Flex))
            {
                foreach (var t in _gameMapTabs)
                    if (t.Tab.style.display.value == DisplayStyle.Flex)
                    { _activeMapTab = t; SelectConsoleTab(3); break; }
            }
            if (!ConsoleTabOffered(_activeConsoleTab))
            {
                int first = FirstOfferedConsoleTab();
                if (first >= 0) SelectConsoleTab(first);
                else SetConsoleVisible(false);
            }
        }

        /// <summary>View menu: independently toggles one console-area tab
        /// (0 = Console, 1 = Search Results, 4 = Bookmarks).</summary>
        void ToggleConsoleAreaTab(int index)
        {
            bool on;
            switch (index)
            {
                case 0: on = _consoleTabVisible = !_consoleTabVisible; break;
                case 1: on = _searchTabVisible = !_searchTabVisible; break;
                case 4: on = _bmTabVisible = !_bmTabVisible; break;
                default: return;
            }
            ApplyConsoleTabVisibility();
            if (!on) return;
            if (index == 4) ShowBookmarksTab(); // fresh snapshot + selects
            else
            {
                SetConsoleVisible(true);
                SelectConsoleTab(index);
            }
        }

        VisualElement _consoleTab, _searchTab, _scannerTab, _mapHost, _bmTab;
        VisualElement _mapTabsHost;
        Label _scannerTabLabel;
        int _activeConsoleTab;

        /// <summary>One game-map console tab: a game's own strip tab plus its
        /// own map view (kept as a hidden child of _mapHost while inactive,
        /// so zoom/scroll state survives tab switches).</summary>
        sealed class GameMapTab
        {
            public object Key;
            public VisualElement Tab;
            public VisualElement Content;
        }
        readonly System.Collections.Generic.List<GameMapTab> _gameMapTabs =
            new System.Collections.Generic.List<GameMapTab>();
        GameMapTab _activeMapTab;

        /// <summary>0 = Console, 1 = Search Results, 2 = Scanner Results,
        /// 3 = Map, 4 = Bookmarks.</summary>
        void SelectConsoleTab(int index)
        {
            _activeConsoleTab = index;
            if (_consoleHost != null)
                _consoleHost.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_searchPane != null)
                _searchPane.style.display = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_scannerScroll != null)
                _scannerScroll.style.display = index == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_mapHost != null)
                _mapHost.style.display = index == 3 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_bmPane != null)
                _bmPane.style.display = index == 4 ? DisplayStyle.Flex : DisplayStyle.None;
            var active = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            if (_consoleTab != null)
                _consoleTab.style.backgroundColor = index == 0 ? active : Color.clear;
            if (_searchTab != null)
                _searchTab.style.backgroundColor = index == 1 ? active : Color.clear;
            if (_scannerTab != null)
                _scannerTab.style.backgroundColor = index == 2 ? active : Color.clear;
            foreach (var t in _gameMapTabs)
            {
                bool on = index == 3 && t == _activeMapTab;
                if (t.Content != null)
                    t.Content.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                t.Tab.style.backgroundColor = on ? active : Color.clear;
            }
            if (_bmTab != null)
                _bmTab.style.backgroundColor = index == 4 ? active : Color.clear;
        }

        /// <summary>Game maps: ONE console tab per game. Creates (or re-shows)
        /// the tab for <paramref name="key"/>, labeled <paramref name="title"/>
        /// and hosting the game's own map view, and brings it to the front —
        /// unless <paramref name="front"/> is false (domain-reload resume
        /// registers every game's tab without stealing the console area).</summary>
        internal void ShowGameMapTab(object key, string title, VisualElement content, bool front = true)
        {
            if (_mapTabsHost == null || _mapHost == null || content == null) return;
            var t = FindGameMapTab(key);
            if (t == null)
            {
                var made = new GameMapTab { Key = key };
                var tab = new VisualElement { tooltip = L10n.Tr("The Z-Machine game's automatic map.") };
                tab.AddToClassList("console-tab");
                tab.Add(new Label(title));
                tab.RegisterCallback<PointerDownEvent>(e =>
                {
                    _activeMapTab = made;
                    SelectConsoleTab(3);
                    e.StopPropagation();
                });
                var close = new Button(() =>
                {
                    tab.style.display = DisplayStyle.None;
                    ApplyConsoleTabVisibility();
                }) { text = "×", tooltip = L10n.Tr("Hide this view.") };
                close.AddToClassList("tab__close");
                tab.Add(close);
                made.Tab = tab;
                _mapTabsHost.Add(tab);
                _gameMapTabs.Add(made);
                t = made;
            }
            if (t.Content != content)
            {
                // The game re-attached its auto-map with a fresh view.
                if (t.Content != null && t.Content.parent == _mapHost) _mapHost.Remove(t.Content);
                t.Content = content;
                content.style.display = DisplayStyle.None;
                _mapHost.Add(content);
            }
            t.Tab.style.display = DisplayStyle.Flex;
            if (!front)
            {
                if (_activeMapTab == null) _activeMapTab = t;
                return;
            }
            _activeMapTab = t;
            SetConsoleVisible(true);
            SelectConsoleTab(3);
        }

        /// <summary>Deletes a game's map tab and view (the game ended or its
        /// auto-map was turned off).</summary>
        internal void RemoveGameMapTab(object key)
        {
            var t = FindGameMapTab(key);
            if (t == null) return;
            if (t.Content != null && t.Content.parent == _mapHost) _mapHost.Remove(t.Content);
            t.Tab.RemoveFromHierarchy();
            _gameMapTabs.Remove(t);
            if (_activeMapTab == t) _activeMapTab = null;
            ApplyConsoleTabVisibility();
        }

        GameMapTab FindGameMapTab(object key)
        {
            foreach (var t in _gameMapTabs) if (Equals(t.Key, key)) return t;
            return null;
        }

        void SetConsoleVisible(bool visible)
        {
            _consoleVisible = visible;
            if (_consolePane != null)
                _consolePane.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_consoleSplitter != null)
                _consoleSplitter.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) _consoleVersionShown = -1; // force refresh
        }

        void PollConsole()
        {
            if (!_consoleVisible || _consoleList == null) return;
            if (_consoleVersionShown == AteConsole.Version) return;
            _consoleVersionShown = AteConsole.Version;
            AteConsole.CopyLinesInto(_consoleLines);
            RefreshConsoleView();
        }

        /// <summary>(Re)applies the console Filter box to the raw lines and
        /// refreshes the list, sticking to the bottom. The header shows
        /// "N of M shown" while the filter hides anything.</summary>
        void RefreshConsoleView()
        {
            if (_consoleList == null) return;
            string filter = _consoleFilter != null ? (_consoleFilter.value ?? "").Trim().ToLowerInvariant() : "";
            _consoleShown.Clear();
            foreach (var line in _consoleLines)
                if (filter.Length == 0 || line.ToLowerInvariant().Contains(filter))
                    _consoleShown.Add(line);
            _consoleHeader.text = filter.Length == 0 || _consoleShown.Count == _consoleLines.Count
                ? string.Empty
                : string.Format(L10n.Tr("{0} of {1} shown"), _consoleShown.Count, _consoleLines.Count);
            _consoleList.RefreshItems();
            // Stick to the bottom once the new content has been laid out.
            _consoleList.schedule.Execute(() =>
            {
                if (_consoleShown.Count > 0)
                    _consoleList.ScrollToItem(_consoleShown.Count - 1);
            }).ExecuteLater(30);
        }

        void CopyConsoleLine(string line)
        {
            EditorGUIUtility.systemCopyBuffer = line;
            PostStatus(L10n.Tr("Copied the console line."));
        }

        /// <summary>Status-bar messages also land in the console, and are held
        /// in the bar for a few seconds so the Ln/Col poll doesn't stomp them.</summary>
        void PostStatus(string message)
        {
            AteConsole.Log(message);
            PostStatusBarOnly(message);
        }

        IVisualElementScheduledItem _statusFlashReset;

        /// <summary>Status bar WITHOUT the console copy — for high-frequency
        /// ephemeral feedback (undo/redo character counts) that would
        /// otherwise spam the console on every Ctrl+Z step. Every new message
        /// FLASHES yellow briefly to draw the eye to the bar, then settles
        /// back to the theme color.</summary>
        void PostStatusBarOnly(string message)
        {
            if (_statusLeft != null)
            {
                _statusLeft.text = message;
                if (!string.IsNullOrEmpty(message))
                {
                    _statusLeft.style.color = new Color(0.95f, 0.85f, 0.3f);
                    _statusFlashReset ??= _statusLeft.schedule.Execute(() =>
                        _statusLeft.style.color = StyleKeyword.Null);
                    _statusFlashReset.ExecuteLater(800);
                }
            }
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

            // Group the flat list under section headers.
            void Section(string t) => _settingsPane.Add(new Label(L10n.Tr(t))
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12, marginBottom = 2, opacity = 0.8f }
            });

            Section("Appearance");
            var themeNames = new List<string>();
            foreach (var t in HighlightTheme.All) themeNames.Add(t.Name);
            _settingsTheme = new PopupField<string>(L10n.Tr("Color Theme"), themeNames, CurrentTheme.Name);
            _settingsTheme.RegisterValueChangedCallback(e =>
            {
                CurrentTheme = HighlightTheme.ByName(e.newValue);
                ApplyTheme();
            });
            _settingsTheme.tooltip = L10n.Tr("Syntax color palette: Visual Studio, VS Code, or Rider styles.");
            _settingsPane.Add(_settingsTheme);

            _settingsMode = new EnumField(L10n.Tr("Light/Dark Mode"), CurrentThemeMode);
            _settingsMode.tooltip = L10n.Tr("Follow the Unity Editor skin, or force the light or dark variant.");
            _settingsMode.RegisterValueChangedCallback(e =>
            {
                CurrentThemeMode = (ThemeMode)e.newValue;
                ApplyTheme();
            });
            _settingsPane.Add(_settingsMode);

            _settingsLines = new Toggle(L10n.Tr("Line Numbers")) { value = _showLineNumbers,
                tooltip = L10n.Tr("Show line numbers in the gutter.") };
            _settingsLines.RegisterValueChangedCallback(e =>
            {
                _showLineNumbers = e.newValue;
                ApplyViewChrome();
            });
            _settingsPane.Add(_settingsLines);

            _settingsWrap = new Toggle(L10n.Tr("Word Wrap")) { value = _wordWrap,
                tooltip = L10n.Tr("Wrap long lines at the viewport edge instead of scrolling horizontally.") };
            _settingsWrap.RegisterValueChangedCallback(e =>
            {
                _wordWrap = e.newValue;
                if (_code != null) _code.wordWrap = e.newValue;
            });
            _settingsPane.Add(_settingsWrap);

            Section("Editor");
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
            Section("Fonts");
            _settingsFont = new PopupField<string>(L10n.Tr("Font"), fontNames, currentFont)
            { tooltip = L10n.Tr("Editor font, from the fonts installed on this machine. Default is the bundled monospace.") };
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
            Section("Language & Tools");
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

            // "#pragma bookmark" lines trigger CS1633 (unknown pragma) in C#
            // scripts. Show whether the project suppresses it, with a
            // one-click fix that writes -nowarn:1633 into Assets/csc.rsp.
            var pragmaRow = new VisualElement();
            pragmaRow.style.flexDirection = FlexDirection.Row;
            pragmaRow.style.alignItems = Align.Center;
            _settingsPragmaFix = new Button(SuppressUnknownPragmaWarning)
            {
                text = L10n.Tr("Suppress in This Project"),
                tooltip = L10n.Tr("Adds -nowarn:1633 to Assets/csc.rsp, Unity's project-wide compiler response file, so #pragma bookmark lines compile without the warning. Assemblies compiled from their own asmdef may need the flag in their own response file.")
            };
            _settingsPragmaStatus = new Label
            {
                tooltip = L10n.Tr("#pragma bookmark lines in C# scripts trigger compiler warning CS1633 (unknown pragma) unless the project suppresses it.")
            };
            _settingsPragmaStatus.style.unityTextAlign = TextAnchor.MiddleLeft;
            pragmaRow.Add(_settingsPragmaFix);
            pragmaRow.Add(_settingsPragmaStatus);
            _settingsPane.Add(pragmaRow);
            SyncPragmaRow();

            // ATE as Unity's Revision Control Diff/Merge tool (External
            // Tools): a generated shim hands invocations to the running
            // editor, which opens them in the Diff / Merge window.
            var diffRow = new VisualElement();
            diffRow.style.flexDirection = FlexDirection.Row;
            diffRow.style.alignItems = Align.Center;
            _settingsDiffToolBtn = new Button(() =>
            {
                PostStatus(AteDiffToolBridge.IsConfigured
                    ? AteDiffToolBridge.Restore()
                    : AteDiffToolBridge.Configure());
                SyncDiffToolRow();
            })
            { tooltip = L10n.Tr("Sets ATE as Unity's Revision Control Diff/Merge tool (Preferences → External Tools), or restores the previous tool. Diffs and merges from Unity's version control then open in ATE's Diff / Merge window of this project's editor.") };
            _settingsDiffToolStatus = new Label
            { tooltip = L10n.Tr("Which application Unity currently launches for version-control diffs and merges.") };
            _settingsDiffToolStatus.style.unityTextAlign = TextAnchor.MiddleLeft;
            diffRow.Add(_settingsDiffToolBtn);
            diffRow.Add(_settingsDiffToolStatus);
            _settingsPane.Add(diffRow);
            SyncDiffToolRow();

            Section("Display");
            _settingsSmooth = new Toggle(L10n.Tr("Smooth Scrolling")) { value = EditorConfig.SmoothScrolling };
            _settingsSmooth.RegisterValueChangedCallback(e => EditorConfig.SmoothScrolling = e.newValue);
            _settingsSmooth.tooltip = L10n.Tr("Animate wheel scrolling instead of stepping line by line.");
            _settingsPane.Add(_settingsSmooth);

            _settingsMdRendered = new Toggle(L10n.Tr("Open Markdown Rendered")) { value = EditorConfig.MdOpenRendered };
            _settingsMdRendered.RegisterValueChangedCallback(e => EditorConfig.MdOpenRendered = e.newValue);
            _settingsMdRendered.tooltip = L10n.Tr("Default view when opening .md files: rendered (WYSIWYG) when on, source when off. The MD/source toggle still switches per tab.");
            _settingsPane.Add(_settingsMdRendered);

            _settingsMdLocked = new Toggle(L10n.Tr("Open Markdown Locked")) { value = EditorConfig.MdLockByDefault };
            _settingsMdLocked.RegisterValueChangedCallback(e => EditorConfig.MdLockByDefault = e.newValue);
            _settingsMdLocked.tooltip = L10n.Tr("Rendered Markdown opens read-only: clicks select text for copying instead of opening block editors. The lock button in the toolbar still switches per tab.");
            _settingsPane.Add(_settingsMdLocked);

            _settingsTabColor = new UnityEditor.UIElements.ColorField(L10n.Tr("Tab Color")) { value = EditorConfig.TabColor, showAlpha = false, hdr = false };
            _settingsTabColor.RegisterValueChangedCallback(e =>
            {
                EditorConfig.TabColor = e.newValue;
                InvalidateTabs();
                RebuildTabs();
            });
            _settingsTabColor.tooltip = L10n.Tr("Base color of the tab strip; each tab gets its own stable shade of it.");
            _settingsPane.Add(_settingsTabColor);

            Section("Editing");
            _settingsAutoClose = new Toggle(L10n.Tr("Auto-Close Brackets")) { value = EditorConfig.AutoCloseBrackets };
            _settingsAutoClose.RegisterValueChangedCallback(e => EditorConfig.AutoCloseBrackets = e.newValue);
            _settingsAutoClose.tooltip = L10n.Tr("Typing ( [ { \" ' inserts the closing pair, closers type over, Backspace removes empty pairs, selections get wrapped.");
            _settingsPane.Add(_settingsAutoClose);

            var spell = new Toggle(L10n.Tr("Spell Checking")) { value = EditorConfig.SpellCheckEnabled };
            spell.RegisterValueChangedCallback(e =>
            {
                EditorConfig.SpellCheckEnabled = e.newValue;
                if (_code != null) _code.spellEnabled = e.newValue;
            });
            spell.tooltip = L10n.Tr("Underline unknown words — comments and strings in code, everything in markdown/plain text. Right-click a flagged word to add it to your dictionary. Drop extra dictionaries (.txt / Hunspell .dic) in the shared Dictionaries folder.");
            _settingsPane.Add(spell);

            _settingsAutoReload = new Toggle(L10n.Tr("Auto-Reload Changed Files")) { value = EditorConfig.AutoReloadFromDisk };
            _settingsAutoReload.RegisterValueChangedCallback(e => EditorConfig.AutoReloadFromDisk = e.newValue);
            _settingsAutoReload.tooltip = L10n.Tr("When a file changes on disk and the buffer has no unsaved edits, reload it automatically instead of asking. Buffers with unsaved edits still ask.");
            _settingsPane.Add(_settingsAutoReload);

            Section("AI");
            _settingsCopilot = new Toggle(L10n.Tr("GitHub Copilot (inline suggestions)")) { value = EditorConfig.CopilotEnabled };
            _settingsCopilot.tooltip = L10n.Tr("Ghost-text code suggestions from GitHub Copilot. Requires Node.js on this machine and your own Copilot subscription; the official Copilot Language Server is installed on first enable. Tab accepts a suggestion, Escape dismisses it.");
            _settingsCopilot.RegisterValueChangedCallback(e =>
            {
                EditorConfig.CopilotEnabled = e.newValue;
                if (e.newValue) CopilotService.Start(); else CopilotService.Stop();
                SyncSettingsControls();
            });
            _settingsPane.Add(_settingsCopilot);
            _settingsCopilotRow = new VisualElement();
            _settingsCopilotRow.style.flexDirection = FlexDirection.Row;
            _settingsCopilotRow.style.marginLeft = 16;
            _settingsCopilotStatus = new Label();
            _settingsCopilotStatus.style.unityTextAlign = TextAnchor.MiddleLeft;
            _settingsCopilotSignIn = new Button(() =>
            {
                if (CopilotService.Status == CopilotService.State.Ready) CopilotService.SignOut();
                else CopilotService.SignIn();
            }) { tooltip = L10n.Tr("Sign in to GitHub Copilot with the device-flow code, or sign out of the current account.") };
            _settingsCopilotRow.Add(_settingsCopilotSignIn);
            _settingsCopilotRow.Add(_settingsCopilotStatus);
            _settingsPane.Add(_settingsCopilotRow);

            // Unity AI rides the Editor's Unity account. Deliberately NO
            // sign-out here (removed 2026-07-27, Cary's call): logging out is
            // editor-wide — collab, licensing, everything — far beyond ATE's
            // remit. Unity's own account UI owns that. We only SHOW which
            // account Unity AI will use.
            if (UnityAiBridge.Available)
            {
                _settingsUnityAiRow = new VisualElement();
                _settingsUnityAiRow.style.flexDirection = FlexDirection.Row;
                _settingsUnityAiRow.style.alignItems = Align.Center;
                _settingsUnityAiStatus = new Label();
                _settingsUnityAiStatus.tooltip = L10n.Tr("Unity AI uses the Unity account the Editor is signed into; manage it from the account menu in the Editor's top-right corner.");
                _settingsUnityAiRow.Add(_settingsUnityAiStatus);
                _settingsPane.Add(_settingsUnityAiRow);
            }

            Section("Files & Saving");
            _settingsTrimSave = new Toggle(L10n.Tr("Trim Trailing Whitespace on Save")) { value = EditorConfig.TrimTrailingOnSave };
            _settingsTrimSave.RegisterValueChangedCallback(e => EditorConfig.TrimTrailingOnSave = e.newValue);
            _settingsTrimSave.tooltip = L10n.Tr("Remove spaces and tabs at line ends when saving (per project).");
            _settingsPane.Add(_settingsTrimSave);

            _settingsFinalNewline = new Toggle(L10n.Tr("Ensure Final Newline on Save")) { value = EditorConfig.FinalNewlineOnSave };
            _settingsFinalNewline.RegisterValueChangedCallback(e => EditorConfig.FinalNewlineOnSave = e.newValue);
            _settingsFinalNewline.tooltip = L10n.Tr("Guarantee the file ends with exactly one newline when saving (per project).");
            _settingsPane.Add(_settingsFinalNewline);

            var autoSaveFocus = new Toggle(L10n.Tr("Auto-Save on Focus Loss")) { value = EditorConfig.AutoSaveOnFocusLoss };
            autoSaveFocus.RegisterValueChangedCallback(e => EditorConfig.AutoSaveOnFocusLoss = e.newValue);
            autoSaveFocus.tooltip = L10n.Tr("Save every dirty file-backed document when the ATE window loses focus. Untitled buffers are skipped (session persistence still protects them). Per project.");
            _settingsPane.Add(autoSaveFocus);

            _settingsRecentMax = new IntegerField(L10n.Tr("Recent Files Count")) { value = EditorConfig.RecentFilesMax };
            _settingsRecentMax.RegisterValueChangedCallback(e =>
            {
                EditorConfig.RecentFilesMax = e.newValue;
                _settingsRecentMax.SetValueWithoutNotify(EditorConfig.RecentFilesMax); // clamp echo (1-30)
            });
            _settingsRecentMax.tooltip = L10n.Tr("How many entries File → Recent Files keeps (1-30).");
            _settingsPane.Add(_settingsRecentMax);

            Section("Updates");
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
            }) { text = L10n.Tr("Check for Updates Now"),
                tooltip = L10n.Tr("Query GitHub for a newer ATE release immediately.") };
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
                    VirtualMarkdown = markdown, MdRendered = markdown && rendered,
                    MdLocked = markdown && EditorConfig.MdLockByDefault
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

        // ---- CS1633 (unknown pragma) suppression for #pragma bookmark ----

        static string CscRspPath => System.IO.Path.Combine(Application.dataPath, "csc.rsp");

        /// <summary>Is CS1633 suppressed project-wide? Reads Assets/csc.rsp
        /// for a -nowarn (or /nowarn) list containing 1633.</summary>
        static bool UnknownPragmaSuppressed()
        {
            try
            {
                if (!System.IO.File.Exists(CscRspPath)) return false;
                foreach (var line in System.IO.File.ReadAllLines(CscRspPath))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        line, @"^[ \t]*[-/]nowarn:(.+)$");
                    if (!match.Success) continue;
                    foreach (var warn in match.Groups[1].Value.Split(','))
                        if (warn.Trim().TrimStart('C', 'S') == "1633") return true;
                }
            }
            catch (System.Exception) { }
            return false;
        }

        /// <summary>The Settings button: appends -nowarn:1633 to Assets/csc.rsp
        /// (creating the file if needed) and triggers a script recompile so
        /// the suppression takes effect immediately.</summary>
        void SuppressUnknownPragmaWarning()
        {
            try
            {
                if (!UnknownPragmaSuppressed())
                {
                    string text = System.IO.File.Exists(CscRspPath)
                        ? System.IO.File.ReadAllText(CscRspPath).TrimEnd('\r', '\n') + System.Environment.NewLine
                        : string.Empty;
                    System.IO.File.WriteAllText(CscRspPath, text + "-nowarn:1633" + System.Environment.NewLine);
                    AssetDatabase.ImportAsset("Assets/csc.rsp");
                    UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                    PostStatus(L10n.Tr("Added -nowarn:1633 to Assets/csc.rsp — scripts will recompile."));
                }
            }
            catch (System.Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Could not update Assets/csc.rsp: " + ex.Message);
            }
            SyncPragmaRow();
        }

        void SyncPragmaRow()
        {
            if (_settingsPragmaStatus == null) return;
            bool suppressed = UnknownPragmaSuppressed();
            _settingsPragmaStatus.text = "  " + (suppressed
                ? L10n.Tr("Unknown-pragma warning (CS1633): suppressed in this project.")
                : L10n.Tr("Unknown-pragma warning (CS1633): not suppressed — #pragma bookmark lines will warn in C# scripts."));
            _settingsPragmaFix.style.display = suppressed ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void SyncDiffToolRow()
        {
            if (_settingsDiffToolStatus == null) return;
            bool ours = AteDiffToolBridge.IsConfigured;
            _settingsDiffToolBtn.text = ours
                ? L10n.Tr("Restore Previous Diff Tool")
                : L10n.Tr("Use ATE for Unity Diff/Merge");
            string current = UnityEditor.EditorPrefs.GetString("kDiffsDefaultApp", "");
            _settingsDiffToolStatus.text = "  " + (ours
                ? L10n.Tr("Unity diff/merge tool: ATE (this project).")
                : string.Format(L10n.Tr("Unity diff/merge tool: {0}"),
                    string.IsNullOrEmpty(current) ? L10n.Tr("(none)") : current));
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
            _settingsMdLocked?.SetValueWithoutNotify(EditorConfig.MdLockByDefault);
            _settingsRecentMax?.SetValueWithoutNotify(EditorConfig.RecentFilesMax);
            _settingsTabColor?.SetValueWithoutNotify(EditorConfig.TabColor);
            _settingsAutoClose?.SetValueWithoutNotify(EditorConfig.AutoCloseBrackets);
            _settingsAutoReload?.SetValueWithoutNotify(EditorConfig.AutoReloadFromDisk);
            _settingsCopilot?.SetValueWithoutNotify(EditorConfig.CopilotEnabled);
            if (_settingsCopilotRow != null)
            {
                bool on = EditorConfig.CopilotEnabled;
                _settingsCopilotRow.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                _settingsCopilotStatus.text = "  " + CopilotStatusText();
                _settingsCopilotSignIn.text = CopilotService.Status == CopilotService.State.Ready
                    ? L10n.Tr("Sign Out") : L10n.Tr("Sign In");
                _settingsCopilotSignIn.SetEnabled(
                    CopilotService.Status == CopilotService.State.Ready ||
                    CopilotService.Status == CopilotService.State.NotSignedIn);
            }
            if (_settingsUnityAiRow != null)
            {
                string user = UnityAiBridge.UnityAccountName;
                _settingsUnityAiStatus.text = user != null
                    ? string.Format(L10n.Tr("Unity AI account: {0}"), user)
                    : L10n.Tr("Unity account: signed out");
            }
            SyncPragmaRow();
            SyncDiffToolRow();
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
            {
                doc.MdRendered = EditorConfig.MdOpenRendered;
                doc.MdLocked = EditorConfig.MdLockByDefault;
            }
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
                RefreshGitMarksAsync(); // the diff against HEAD just changed
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
        System.Action _miniCancel;
        bool _miniDigitsOnly;
        bool _miniCancelOnBlur = true;


        void OnGlobalKeyUp(KeyUpEvent e)
        {
            Scripting.AteApi.NoteKeyUp(e.keyCode);
            if (!MiniBufferOpen && Scripting.AteApi.RaiseKeyUp(e))
                e.StopImmediatePropagation();
        }

        bool MiniBufferOpen =>
            _miniBuffer != null && _miniBuffer.style.display == DisplayStyle.Flex;

        void OnLostFocus()
        {
            _code?.BreakUndoGroup();
            // Auto-save: dirty FILE-BACKED docs only — never Save As prompts
            // from a mere focus change.
            if (EditorConfig.AutoSaveOnFocusLoss && _docs != null)
            {
                bool saved = false;
                foreach (var doc in _docs)
                    if (!doc.IsSettings && doc.IsDirty && doc.HasFile)
                    { FileService.Save(doc); saved = true; }
                if (saved) { RebuildTabs(); UpdateTitle(); UpdateStatus(); }
            }
            Scripting.AteApi.NotifyWindowFocus(false);
        }

        void OnFocus()
        {
            Scripting.AteApi.NotifyWindowFocus(true);
            // Inactive tabs are checked when they are activated (SwitchTo).
            if (_docs == null || _docs.Count == 0 || _active >= _docs.Count) return;
            CheckExternalChange(Active); // non-modal banner
            RefreshGitMarksAsync();      // commits/pulls may have moved HEAD
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
            UpdateChecker.onAvailableVersionChanged -= RefreshUpdateIcon;
            CopilotService.onStatusChanged -= OnCopilotStatus;
            AssemblyReloadEvents.beforeAssemblyReload -= SyncUndoWorldForReload;
        }

        /// <summary>The ACTIVE document's undo world lives in the code view
        /// and is only parked on the document at tab switches — park it right
        /// before the domain serializes, so its undo history (now a
        /// serialized TextDocument field) survives the reload too. Re-attach
        /// the same instance so the live view keeps operating; doc and view
        /// then share the reference, which is exactly what serialization
        /// needs.</summary>
        void SyncUndoWorldForReload()
        {
            if (_undoWorldDoc == null || _code == null) return;
            var world = _code.DetachUndoWorld();
            _undoWorldDoc.UndoWorld = world;
            _code.AttachUndoWorld(world);
        }

        /// <summary>Shows/hides the green update icon by the settings gear.</summary>
        void RefreshUpdateIcon(string version)
        {
            if (_updateBtn == null) return;
            bool show = !string.IsNullOrEmpty(version);
            _updateBtn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
                _updateBtn.tooltip = string.Format(
                    L10n.Tr("Update available: {0} (installed: {1})"),
                    version, UpdateChecker.CurrentVersion());
        }

        void OnUpdateIconClicked()
        {
            string latest = UpdateChecker.AvailableVersion;
            if (string.IsNullOrEmpty(latest)) return;
            // Always the dialog — embedded copies too (Install Now is disabled
            // there); a console-only reaction read as the click doing nothing.
            UpdatePromptWindow.Open(UpdateChecker.CurrentVersion(), latest);
        }


        const long SessionAutosaveMs = 30000;


        // --- Keyboard commands ---
        // Three layouts (Settings → Keyboard Layout): Visual Studio, VS Code,
        // Rider — covering the defaults that apply to this editor's features.

        /// <summary>Installed by the built-in Z-Machine game while it runs, so
        /// keys reach the game before editor commands. Returns true to consume.
        /// Not a [SerializeField] — a domain reload ends any running game.</summary>
        internal System.Func<KeyDownEvent, bool> GameKeyHandler;

        /// <summary>Window-level commands; works from any tab including Settings.</summary>
        void OnGlobalKeyDown(KeyDownEvent e)
        {
            if (UpdateChecker.InstallInProgress) { e.StopImmediatePropagation(); return; }
            // Addon input hook (API 1.1): feed the polled key state, then let
            // addons consume the key BEFORE any editor handling. Suppressed
            // while the mini-buffer prompt is open so games can use Prompt.
            Scripting.AteApi.NoteKeyDown(e.keyCode);
            if (!MiniBufferOpen && Scripting.AteApi.RaiseKeyDown(e))
            {
                e.StopImmediatePropagation();
                return;
            }
            // Built-in game (Z-Machine): a core hook, independent of the addon
            // input events so it survives addon reloads mid-game.
            if (!MiniBufferOpen && GameKeyHandler != null && GameKeyHandler(e))
            {
                e.StopImmediatePropagation();
                return;
            }
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
                // Locked rendered Markdown is copy-only: Ctrl+C without a
                // label selection copies the whole document as plain rendered
                // text (a selected label's native copy wins via
                // TargetIsTextInput); cut/paste must not reach the hidden
                // source buffer.
                bool lockedMd = ActiveIsMarkdown && Active.MdRendered && Active.MdLocked;
                switch (e.keyCode)
                {
                    case KeyCode.X: if (!lockedMd) _code.Cut(); handled = true; break;
                    case KeyCode.C:
                        if (lockedMd && _mdView != null)
                        {
                            if (_mdView.HasDocSelection)
                            {
                                EditorGUIUtility.systemCopyBuffer = _mdView.SelectedPlainText();
                                PostStatus(L10n.Tr("Copied the selected blocks as plain text."));
                            }
                            else
                            {
                                EditorGUIUtility.systemCopyBuffer = _mdView.PlainText();
                                PostStatus(L10n.Tr("Copied the rendered document as plain text."));
                            }
                        }
                        else _code.Copy();
                        handled = true; break;
                    case KeyCode.V: if (!lockedMd) _code.Paste(); handled = true; break;
                    case KeyCode.A:
                        // Locked rendered Markdown: select the whole DOCUMENT
                        // (the view-level block selection), not one label.
                        if (lockedMd) _mdView?.SelectAllDoc();
                        else _code.SelectAll();
                        handled = true; break;
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
                if (_code != null && !e.shiftKey && _code.AcceptGhost())
                    handled = true; // Copilot ghost text accepts on Tab
                else if (_code != null && _code.CompletionVisible)
                    handled = false; // the completion popup accepts on Tab
                else if (_code != null && _code.SnippetTabJump(e.shiftKey))
                    handled = true; // active snippet session: cycle its stops
                else if (_code != null && !e.shiftKey && _code.TryExpandSnippetAtCaret())
                    handled = true; // trigger word + Tab expands the snippet
                else
                {
                    if (e.shiftKey) UnindentSelection(); else InsertTab();
                    handled = true;
                }
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

        /// <summary>Wraps the selection (or the caret's line) in /* */, or
        /// removes an existing wrapping pair. One undo step.</summary>
        void ToggleBlockComment()
        {
            if (!CanEditDoc) return;
            int s = Mathf.Min(_code.cursorIndex, _code.selectIndex);
            int e = Mathf.Max(_code.cursorIndex, _code.selectIndex);
            string v = _code.value;
            if (s == e)
            {
                _code.IndexToLineCol(s, out int line, out _);
                s = _code.LineColToIndex(line, 0);
                e = s;
                while (e < v.Length && v[e] != '\n') e++;
            }
            string seg = v.Substring(s, e - s);
            string trimmed = seg.Trim();
            string repl;
            if (trimmed.StartsWith("/*") && trimmed.EndsWith("*/") && trimmed.Length >= 4)
            {
                int i1 = seg.IndexOf("/*", System.StringComparison.Ordinal);
                int i2 = seg.LastIndexOf("*/", System.StringComparison.Ordinal);
                repl = seg.Remove(i2, 2).Remove(i1, 2);
            }
            else repl = "/*" + seg + "*/";
            _code.ReplaceRangeInternal(s, e, repl, s + repl.Length, CodeView.EditKind.LineOp);
            _code.selectIndex = s;
            _code.cursorIndex = s + repl.Length;
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
            string repl = sb.ToString();
            ReplaceRange(first, last, repl, first);
            // A selection stays selected (the full affected line range), so
            // repeated Ctrl+/ toggles cleanly; a bare caret stays collapsed.
            if (end > start)
            {
                _code.selectIndex = first;
                _code.cursorIndex = first + repl.Length;
            }
        }

        // --- Find/Replace surface (used by FindReplaceWindow) ---

        public int DocCount => _docs.Count;
        public int ActiveIndex => _active;
        public bool IsSettingsTab(int i) => _docs[i].IsSettings;

        /// <summary>The document's file path, or null for untitled tabs.</summary>
        public string GetDocPath(int i) => _docs[i].HasFile ? _docs[i].FilePath : null;

        /// <summary>Mark tab support: bookmarks the given lines of the ACTIVE
        /// document (0-based), optionally clearing existing bookmarks first.
        /// Returns how many lines are newly marked.</summary>
        internal int MarkBookmarkLines(System.Collections.Generic.IEnumerable<int> lines, bool purge)
        {
            if (!CanEditDoc) return 0;
            if (purge) Active.Bookmarks.Clear();
            int added = 0;
            foreach (int l in lines)
                if (Active.Bookmarks.Add(l)) added++;
            _code.RefreshVisiblePublic();
            return added;
        }

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
            SyncMdFindHighlight();
        }

        /// <summary>Rendered Markdown: the code view (which carries every
        /// find/jump selection) is hidden, so mirror its selection into the
        /// rendered view as a scrolled-to block highlight.</summary>
        void SyncMdFindHighlight()
        {
            if (!(ActiveIsMarkdown && Active.MdRendered) || _mdView == null) return;
            GetSelection(out int s, out int e);
            _mdView.HighlightSpan(s, e);
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

        UnityEngine.UIElements.IVisualElementScheduledItem _copilotPending;

        void RequestCopilotGhost()
        {
            _copilotPending.Pause();
            if (!EditorConfig.CopilotEnabled || !CanEditDoc || Active.IsSettings) return;
            if (CopilotService.Status != CopilotService.State.Ready) return;
            // Unsaved/virtual documents complete too: the server ignores
            // untitled: URIs but happily serves a file-style pseudo path
            // whose content only ever exists via didOpen (never written).
            string path = Active.HasFile ? Active.FilePath
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ATE_Untitled",
                    Active.DisplayName.Replace(' ', '_').Replace('/', '_'));
            CopilotService.SyncDocument(path, _code.value);
            _code.IndexToLineCol(_code.cursorIndex, out int line, out int col);
            int version = _code.DocVersion;
            CopilotService.RequestCompletion(path, line, col, sugs =>
            {
                if (this == null || _code == null) return;
                if (sugs == null) { _code.ClearGhost(); return; }
                var items = new System.Collections.Generic.List<CodeView.GhostItem>(sugs.Count);
                foreach (var g in sugs)
                    items.Add(new CodeView.GhostItem
                    {
                        Text = g.Text,
                        StartIdx = _code.LineColToIndex(g.StartLine, g.StartChar),
                        EndIdx = _code.LineColToIndex(g.EndLine, g.EndChar)
                    });
                _code.ShowGhost(items, line, col, version);
            });
        }

        void OnCopilotStatus()
        {
            SyncSettingsControls();
            switch (CopilotService.Status)
            {
                case CopilotService.State.SigningIn:
                    ShowBanner(string.Format(
                        L10n.Tr("Copilot sign-in: your code {0} was copied to the clipboard — paste it on the GitHub page that just opened."),
                        CopilotService.PendingUserCode ?? ""),
                        (L10n.Tr("OK"), HideBanner));
                    break;
                case CopilotService.State.Ready:
                    HideBanner(); // the sign-in code prompt is done with
                    PostStatus(L10n.Tr("Copilot is ready."));
                    break;
                case CopilotService.State.Error:
                    PostStatus(L10n.Tr("Copilot: ") + CopilotService.StatusDetail);
                    break;
            }
        }

        static string CopilotStatusText()
        {
            switch (CopilotService.Status)
            {
                case CopilotService.State.Off: return L10n.Tr("off");
                case CopilotService.State.Installing: return L10n.Tr("installing server…");
                case CopilotService.State.Starting: return L10n.Tr("starting…");
                case CopilotService.State.NotSignedIn: return L10n.Tr("not signed in");
                case CopilotService.State.SigningIn: return L10n.Tr("waiting for GitHub confirmation…");
                case CopilotService.State.Ready: return L10n.Tr("ready");
                default: return L10n.Tr("error: ") + CopilotService.StatusDetail;
            }
        }

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

        // NOTE (2026-07-27): an earlier hasUnsavedChanges/SaveChanges approach
        // was reverted — it raises a MODAL dialog on close/quit, which blocks
        // the editor main loop (and the MCP server). The non-modal replacement
        // is AteUnsavedNotice (popup after close) + the reopen banner; unsaved
        // content is never at risk because the session persists it.

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
