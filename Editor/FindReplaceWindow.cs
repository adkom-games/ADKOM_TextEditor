#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// The Find/Replace dialog: a tabbed PARAMETER window (Find, Replace,
    /// Find in Files, Bookmark — Notepad++-style) that never shows results
    /// itself. Every "Find All" action lists its hits in the editor's
    /// Search Results console tab; replaces act directly (Replace in Files
    /// is journaled with global Undo/Redo); Bookmark marks matching lines
    /// of the active document. Search Mode offers Normal, Extended
    /// (\n \r \t \0 \xHH escapes), and Regular expression matching.
    /// Search state persists for the session; the View menu toggles the
    /// window; F3 / Shift+F3 repeat the last search from the editor.
    /// </summary>
    public class FindReplaceWindow : EditorWindow
    {
        internal enum FrTab { Find = 0, Replace = 1, InFiles = 2, Bookmark = 3 }
        const int ModeNormal = 0, ModeExtended = 1, ModeRegex = 2;

        static FindReplaceWindow _instance;

        // Session-persistent state.
        // Search state is SessionState-backed: it survives domain reloads
        // (statics do not) and dies with the editor session — exactly the
        // documented "search state persists for the session".
        static FrTab _sTab
        { get => (FrTab)SessionState.GetInt("ATE.FR.Tab", 0); set => SessionState.SetInt("ATE.FR.Tab", (int)value); }
        static string _sFind
        { get => SessionState.GetString("ATE.FR.Find", string.Empty); set => SessionState.SetString("ATE.FR.Find", value ?? string.Empty); }
        static string _sReplace
        { get => SessionState.GetString("ATE.FR.Replace", string.Empty); set => SessionState.SetString("ATE.FR.Replace", value ?? string.Empty); }
        static string _sFilters
        { get => SessionState.GetString("ATE.FR.Filters", "*"); set => SessionState.SetString("ATE.FR.Filters", value ?? "*"); }
        static string _sDir
        { get => SessionState.GetString("ATE.FR.Dir", string.Empty); set => SessionState.SetString("ATE.FR.Dir", value ?? string.Empty); }
        static int _sMode
        { get => SessionState.GetInt("ATE.FR.Mode", ModeNormal); set => SessionState.SetInt("ATE.FR.Mode", value); }
        static bool _sDotNL
        { get => SessionState.GetBool("ATE.FR.DotNL", false); set => SessionState.SetBool("ATE.FR.DotNL", value); }
        static bool _sCase
        { get => SessionState.GetBool("ATE.FR.Case", false); set => SessionState.SetBool("ATE.FR.Case", value); }
        static bool _sWord
        { get => SessionState.GetBool("ATE.FR.Word", false); set => SessionState.SetBool("ATE.FR.Word", value); }
        static bool _sBack
        { get => SessionState.GetBool("ATE.FR.Back", false); set => SessionState.SetBool("ATE.FR.Back", value); }
        static bool _sInSel
        { get => SessionState.GetBool("ATE.FR.InSel", false); set => SessionState.SetBool("ATE.FR.InSel", value); }
        static bool _sWrap
        { get => SessionState.GetBool("ATE.FR.Wrap", true); set => SessionState.SetBool("ATE.FR.Wrap", value); }
        static bool _sSubFolders
        { get => SessionState.GetBool("ATE.FR.SubFolders", true); set => SessionState.SetBool("ATE.FR.SubFolders", value); }
        static bool _sHidden
        { get => SessionState.GetBool("ATE.FR.Hidden", false); set => SessionState.SetBool("ATE.FR.Hidden", value); }
        static bool _sFollowDoc
        { get => SessionState.GetBool("ATE.FR.FollowDoc", false); set => SessionState.SetBool("ATE.FR.FollowDoc", value); }
        static bool _sPurge
        { get => SessionState.GetBool("ATE.FR.Purge", false); set => SessionState.SetBool("ATE.FR.Purge", value); }

        [SerializeField] TextEditorWindow _owner; // an object ref survives reloads
        System.Threading.SynchronizationContext _ctx;
        // Guard flags must reset with the domain: hot-serialization keeps
        // private fields, and a stuck true here would block forever.
        [System.NonSerialized] bool _searching;

        /// <summary>Reload survivor: the window asset outlives the domain —
        /// re-adopt the singleton and re-acquire what the reload severed
        /// (main-thread context; the owner if its reference broke). CreateGUI
        /// then rebuilds the UI from the SessionState-backed search state.</summary>
        void OnEnable()
        {
            _ctx = System.Threading.SynchronizationContext.Current;
            // SaveState normally runs on close and tab switch — a domain
            // reload does neither, so typed-but-unsaved field values would
            // vanish. Flush them to SessionState just before the reload.
            AssemblyReloadEvents.beforeAssemblyReload += SaveState;
            // Singleton re-adoption is DEFERRED: OnEnable runs inside
            // CreateInstance, before the F3 headless helper gets its
            // HideAndDontSave flag — checking immediately would let the
            // invisible helper steal the singleton slot.
            // Post-reload, the context captured in OnEnable predates the
            // functional Unity context — re-capture the working one.
            AteMainCtx.WhenReady(ctx => { if (this != null) _ctx = ctx; });
            EditorApplication.delayCall += () =>
            {
                if (this == null || hideFlags == HideFlags.HideAndDontSave) return;
                if (_instance == null) _instance = this;
                if (_owner == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
                    _owner = all.Length > 0 ? all[0] : null;
                }
            };
        }

        void OnDisable() { AssemblyReloadEvents.beforeAssemblyReload -= SaveState; }

        Button[] _tabButtons;
        VisualElement _body;
        TextField _find, _replace, _filters, _dir;
        Toggle _case, _word, _back, _wrap, _inSel, _subFolders, _hidden, _followDoc, _purge, _dotNL;
        RadioButtonGroup _mode;
        Label _status;
        Button _undoBtn, _redoBtn;

        // ---------- Entry points ----------

        internal static void Open(TextEditorWindow owner, FrTab tab)
        {
            _sTab = tab;
            bool creating = _instance == null;
            var w = _instance != null ? _instance : (_instance = CreateInstance<FindReplaceWindow>());
            w._owner = owner;
            w._ctx = System.Threading.SynchronizationContext.Current;
            w.titleContent = new GUIContent(L10n.Tr("Find / Replace"));
            if (creating)
            {
                // Fixed size: big enough for the tallest tab (Find in Files),
                // not resizable (min == max).
                w.minSize = w.maxSize = new Vector2(560, 420);
                w.ShowUtility(); // floating, modeless
            }
            w.Focus();
            w.BuildBody();
            var target = w._find;
            target?.schedule.Execute(() => { target.Focus(); target.SelectAll(); }).ExecuteLater(50);
        }

        /// <summary>Whether the dialog is currently open (View menu checkmark).</summary>
        public static bool IsOpen => _instance != null;

        /// <summary>View menu toggle: closes the dialog when open, otherwise
        /// opens it on its last-used tab.</summary>
        public static void ToggleVisible(TextEditorWindow owner)
        {
            if (_instance != null) _instance.Close();
            else Open(owner, _sTab);
        }

        /// <summary>Opens the Find tab with the search field pre-filled (e.g.
        /// "Find Occurrences" from the code view context menu).</summary>
        public static void OpenWithQuery(TextEditorWindow owner, string query)
        {
            if (!string.IsNullOrEmpty(query)) _sFind = query;
            Open(owner, FrTab.Find);
        }

        // Reusable hidden instance for F3-with-dialog-closed: the search core
        // is instance-shaped but needs no UI (never shown, CreateGUI never
        // runs, _status stays null). Replaces the old create-and-
        // DestroyImmediate-per-keypress probe (defect #4).
        static FindReplaceWindow _headless;

        /// <summary>F3 / Shift+F3 support: repeats the last search if any.</summary>
        public static bool FindAgain(TextEditorWindow owner, bool reverse)
        {
            if (string.IsNullOrEmpty(_sFind)) return false;
            var w = _instance;
            if (w == null)
            {
                if (_headless == null)
                {
                    _headless = CreateInstance<FindReplaceWindow>();
                    _headless.hideFlags = HideFlags.HideAndDontSave;
                }
                w = _headless;
            }
            w._owner = owner;
            w.FindNextCore(reverse);
            return true;
        }

        void OnDestroy() { SaveState(); if (_instance == this) _instance = null; }

        // ---------- UI ----------

        void SaveState()
        {
            if (_find != null) _sFind = _find.value;
            if (_replace != null) _sReplace = _replace.value;
            if (_filters != null) _sFilters = _filters.value;
            if (_dir != null) _sDir = _dir.value;
            if (_mode != null) _sMode = _mode.value;
            if (_dotNL != null) _sDotNL = _dotNL.value;
            if (_case != null) _sCase = _case.value;
            if (_word != null) _sWord = _word.value;
            if (_back != null) _sBack = _back.value;
            if (_wrap != null) _sWrap = _wrap.value;
            if (_inSel != null) _sInSel = _inSel.value;
            if (_subFolders != null) _sSubFolders = _subFolders.value;
            if (_hidden != null) _sHidden = _hidden.value;
            if (_followDoc != null) _sFollowDoc = _followDoc.value;
            if (_purge != null) _sPurge = _purge.value;
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 6;

            // Tab strip: Find | Replace | Find in Files | Bookmark.
            var tabs = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0, marginBottom = 6 } };
            string[] names = { L10n.Tr("Find"), L10n.Tr("Replace"), L10n.Tr("Find in Files"), L10n.Tr("Bookmark") };
            string[] tips =
            {
                L10n.Tr("Search the active document"),
                L10n.Tr("Search and replace in open documents"),
                L10n.Tr("Search and replace across the files on disk"),
                L10n.Tr("Bookmark every line that matches"),
            };
            _tabButtons = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = new Button(() => { SaveState(); _sTab = (FrTab)idx; BuildBody(); }) { text = names[i], tooltip = tips[i] };
                b.style.marginLeft = 0;
                b.style.marginRight = 0;
                b.style.borderTopLeftRadius = 4;
                b.style.borderTopRightRadius = 4;
                b.style.borderBottomLeftRadius = 0;
                b.style.borderBottomRightRadius = 0;
                _tabButtons[i] = b;
                tabs.Add(b);
            }
            root.Add(tabs);

            _body = new VisualElement { style = { flexGrow = 1 } };
            root.Add(_body);

            _status = new Label { style = { marginTop = 4, marginBottom = 4, opacity = 0.75f, flexShrink = 0 } };
            root.Add(_status);

            // Replace-in-Files journal buttons stay current while visible.
            root.schedule.Execute(() =>
            {
                if (_undoBtn == null || _redoBtn == null) return;
                _undoBtn.SetEnabled(ReplaceJournal.CanUndo);
                _redoBtn.SetEnabled(ReplaceJournal.CanRedo);
                _undoBtn.tooltip = ReplaceJournal.UndoLabel
                    ?? L10n.Tr("Restore all files touched by the last Replace in Files.");
                _redoBtn.tooltip = ReplaceJournal.RedoLabel
                    ?? L10n.Tr("Re-apply the last undone Replace in Files.");
            }).Every(300);

            BuildBody();
        }

        /// <summary>(Re)builds the body for the active tab. State lives in
        /// the statics, so switching tabs keeps every entered value.</summary>
        void BuildBody()
        {
            if (_body == null) return; // headless instance
            _body.Clear();
            _find = _replace = _filters = _dir = null;
            _case = _word = _back = _wrap = _inSel = _subFolders = _hidden = _followDoc = _purge = _dotNL = null;
            _mode = null;
            _undoBtn = _redoBtn = null;

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                bool on = i == (int)_sTab;
                _tabButtons[i].style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
                _tabButtons[i].style.backgroundColor = on
                    ? new Color(0.5f, 0.5f, 0.5f, 0.25f) : Color.clear;
            }

            bool fif = _sTab == FrTab.InFiles;

            var cols = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            var left = new VisualElement { style = { flexGrow = 1, marginRight = 10 } };
            var right = new VisualElement { style = { width = 210, flexShrink = 0 } };
            cols.Add(left);
            cols.Add(right);
            _body.Add(cols);

            // ---- Fields ----
            _find = LabeledField(left, L10n.Tr("Find what:"), _sFind);
            _find.tooltip = L10n.Tr("The text or pattern to search for. Enter runs this tab's main action.");
            _find.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return) return;
                SaveState();
                RunDefaultAction();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);

            if (_sTab == FrTab.Replace || fif)
            {
                var row = FieldRow(left);
                _replace = LabeledField(row, L10n.Tr("Replace with:"), _sReplace, grow: true);
                _replace.tooltip = L10n.Tr("The replacement text. Regular expression mode expands $1-style group references.");
                var swap = new Button(() =>
                {
                    (string f, string r) = (_find.value, _replace.value);
                    _find.SetValueWithoutNotify(r);
                    _replace.SetValueWithoutNotify(f);
                    SaveState();
                }) { text = "⇅", tooltip = L10n.Tr("Swap Find and Replace") };
                row.Add(swap);
            }

            if (fif)
            {
                _filters = LabeledField(left, L10n.Tr("Filters:"), _sFilters);
                _filters.tooltip = L10n.Tr("File-name wildcard, e.g. *.cs. * matches every file.");
                var dirRow = FieldRow(left);
                _dir = LabeledField(dirRow, L10n.Tr("Directory:"), _sDir, grow: true);
                _dir.tooltip = L10n.Tr("Folder to search. Empty = Assets + Packages.");
                var browse = new Button(() =>
                {
                    string picked = EditorUtility.OpenFolderPanel(L10n.Tr("Search root folder"), _dir.value, "");
                    if (!string.IsNullOrEmpty(picked)) { _dir.value = picked; SaveState(); }
                }) { text = "...", tooltip = L10n.Tr("Choose the folder to search") };
                dirRow.Add(browse);
            }

            // ---- Option checkboxes ----
            var opts = new VisualElement { style = { marginTop = 6 } };
            left.Add(opts);
            if (!fif)
            {
                _back = MakeOpt(opts, L10n.Tr("Backward direction"), _sBack,
                    L10n.Tr("Find Next searches toward the start of the document."));
                _word = MakeOpt(opts, L10n.Tr("Match whole word only"), _sWord,
                    L10n.Tr("Match only when not embedded in a longer word."));
                _case = MakeOpt(opts, L10n.Tr("Match case"), _sCase,
                    L10n.Tr("Uppercase and lowercase must match exactly."));
                _wrap = MakeOpt(opts, L10n.Tr("Wrap around"), _sWrap,
                    L10n.Tr("Continue from the other end when the search hits the end of the document."));
                _inSel = MakeOpt(opts, L10n.Tr("In selection"), _sInSel,
                    L10n.Tr("Limit Count, Find All, Replace All, and Bookmark All to the current selection."));
                if (_sTab == FrTab.Bookmark)
                    _purge = MakeOpt(opts, L10n.Tr("Purge for each search"), _sPurge,
                        L10n.Tr("Clear existing bookmarks before bookmarking the new matches."));
            }
            else
            {
                _word = MakeOpt(opts, L10n.Tr("Match whole word only"), _sWord,
                    L10n.Tr("Match only when not embedded in a longer word."));
                _case = MakeOpt(opts, L10n.Tr("Match case"), _sCase,
                    L10n.Tr("Uppercase and lowercase must match exactly."));
                _followDoc = MakeOpt(opts, L10n.Tr("Follow current doc."), _sFollowDoc,
                    L10n.Tr("Search the active document's folder instead of the Directory field."));
                _followDoc.RegisterValueChangedCallback(e => _dir?.SetEnabled(!e.newValue));
                _subFolders = MakeOpt(opts, L10n.Tr("In all sub-folders"), _sSubFolders,
                    L10n.Tr("Also search every folder below the chosen one."));
                _hidden = MakeOpt(opts, L10n.Tr("In hidden folders"), _sHidden,
                    L10n.Tr("Also search folders whose name starts with a dot."));
                _dir.SetEnabled(!_sFollowDoc);
            }

            // ---- Search Mode group ----
            var modeBox = new VisualElement { style = { marginTop = 8, paddingLeft = 6, paddingRight = 6, paddingBottom = 4 } };
            AteViewStyle.Frame(modeBox);
            modeBox.style.marginLeft = 0;
            modeBox.style.marginRight = 0;
            modeBox.Add(new Label(L10n.Tr("Search Mode")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 2, marginBottom = 2 } });
            _mode = new RadioButtonGroup(null, new List<string>
            {
                L10n.Tr("Normal"),
                L10n.Tr("Extended (\\n, \\r, \\t, \\0, \\x...)"),
                L10n.Tr("Regular expression"),
            }) { value = _sMode };
            _mode.tooltip = L10n.Tr("Normal: literal text. Extended: escape sequences like \\n and \\t are interpreted. Regular expression: .NET regex syntax.");
            _dotNL = new Toggle(L10n.Tr(". matches newline")) { value = _sDotNL };
            _dotNL.tooltip = L10n.Tr("In regular expressions, '.' also matches line breaks.");
            _dotNL.style.marginLeft = 18;
            _dotNL.SetEnabled(_sMode == ModeRegex);
            _mode.RegisterValueChangedCallback(e =>
            {
                _sMode = e.newValue;
                _dotNL.SetEnabled(e.newValue == ModeRegex);
            });
            modeBox.Add(_mode);
            modeBox.Add(_dotNL);
            left.Add(modeBox);

            // ---- Buttons (right column) ----
            switch (_sTab)
            {
                case FrTab.Find:
                    Btn(right, L10n.Tr("Find Next"), () => FindNextCore(false),
                        L10n.Tr("Select the next occurrence in the active document (F3)."));
                    Btn(right, L10n.Tr("Count"), CountCommand,
                        L10n.Tr("Count the occurrences in the active document."));
                    Btn(right, L10n.Tr("Find All in Current Document"), () => FindAllDocs(currentOnly: true),
                        L10n.Tr("List every occurrence in the Search Results tab of the console pane."));
                    Btn(right, L10n.Tr("Find All in All Opened Documents"), () => FindAllDocs(currentOnly: false),
                        L10n.Tr("List occurrences from every open tab in the Search Results tab."));
                    break;
                case FrTab.Replace:
                    Btn(right, L10n.Tr("Find Next"), () => FindNextCore(false),
                        L10n.Tr("Select the next occurrence in the active document (F3)."));
                    Btn(right, L10n.Tr("Replace"), ReplaceOnce,
                        L10n.Tr("Replace the selected match, then find the next one."));
                    Btn(right, L10n.Tr("Replace All"), ReplaceAllCurrent,
                        L10n.Tr("Replace every occurrence in the active document (one undo step)."));
                    Btn(right, L10n.Tr("Replace All in All Opened Documents"), ReplaceAllOpenDocs,
                        L10n.Tr("Replace every occurrence in every open tab."));
                    break;
                case FrTab.InFiles:
                    Btn(right, L10n.Tr("Find All"), () => StartFilesSearch(replaceMode: false),
                        L10n.Tr("Search the files and list every match in the Search Results tab."));
                    Btn(right, L10n.Tr("Replace in Files"), () => StartFilesSearch(replaceMode: true),
                        L10n.Tr("Replace every match on disk and in open buffers — one journaled operation, undoable below."));
                    _undoBtn = Btn(right, L10n.Tr("Undo Replace"), () =>
                    { string l = _owner?.UndoReplaceInFiles(); if (l != null) SetStatus(string.Format(L10n.Tr("Undid: {0}"), l)); });
                    _redoBtn = Btn(right, L10n.Tr("Redo Replace"), () =>
                    { string l = _owner?.RedoReplaceInFiles(); if (l != null) SetStatus(string.Format(L10n.Tr("Redid: {0}"), l)); });
                    break;
                case FrTab.Bookmark:
                    Btn(right, L10n.Tr("Bookmark All"), MarkAllCommand,
                        L10n.Tr("Bookmark every line of the active document that contains a match."));
                    Btn(right, L10n.Tr("Clear all bookmarks"), () =>
                    { if (OwnerOk()) { _owner.ClearBookmarks(); SetStatus(L10n.Tr("Bookmarks cleared.")); } },
                        L10n.Tr("Remove every bookmark from the active document."));
                    Btn(right, L10n.Tr("Copy Matched Text"), CopyMatchedCommand,
                        L10n.Tr("Copy the text of every match to the clipboard, one per line."));
                    break;
            }
            Btn(right, L10n.Tr("Close"), Close, L10n.Tr("Close this dialog. Reopen it from the Edit or View menu.")).style.marginTop = 10;
        }

        void RunDefaultAction()
        {
            switch (_sTab)
            {
                case FrTab.Find:
                case FrTab.Replace: FindNextCore(false); break;
                case FrTab.InFiles: StartFilesSearch(replaceMode: false); break;
                case FrTab.Bookmark: MarkAllCommand(); break;
            }
        }

        static VisualElement FieldRow(VisualElement parent)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
            parent.Add(row);
            return row;
        }

        static TextField LabeledField(VisualElement parent, string label, string value, bool grow = false)
        {
            var f = new TextField(label);
            f.SetValueWithoutNotify(value);
            f.labelElement.style.minWidth = 96;
            f.labelElement.style.unityTextAlign = TextAnchor.MiddleRight;
            if (grow) f.style.flexGrow = 1; else f.style.flexShrink = 0;
            parent.Add(f);
            return f;
        }

        static Toggle MakeOpt(VisualElement parent, string label, bool value, string tip = null)
        {
            var t = new Toggle(label) { value = value };
            if (tip != null) t.tooltip = tip;
            parent.Add(t);
            return t;
        }

        Button Btn(VisualElement parent, string text, System.Action action, string tip = null)
        {
            var b = new Button(() => { SaveState(); action(); }) { text = text };
            if (tip != null) b.tooltip = tip;
            b.style.marginBottom = 2;
            b.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(b);
            return b;
        }

        // ---------- Shared plumbing ----------

        /// <summary>Owner window resolved (no open-tab requirement).</summary>
        bool ResolveOwner()
        {
            if (_owner == null)
                _owner = Resources.FindObjectsOfTypeAll<TextEditorWindow>() is var w && w.Length > 0 ? w[0] : null;
            if (_owner == null) { SetStatus("No editor window."); return false; }
            return true;
        }

        /// <summary>Owner resolved AND has a document.</summary>
        bool OwnerOk()
        {
            if (!ResolveOwner()) return false;
            if (_owner.DocCount == 0) { SetStatus("No open tabs."); return false; }
            return true;
        }

        void SetStatus(string s) { if (_status != null) _status.text = s; AteConsole.Log("Find/Replace: " + s); }

        /// <summary>Interprets \n \r \t \0 \\ \xHH escapes (Extended mode).</summary>
        internal static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'x':
                        if (i + 2 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out int hex))
                        { sb.Append((char)hex); i += 2; }
                        else sb.Append('\\').Append(n);
                        break;
                    default: sb.Append('\\').Append(n); break;
                }
            }
            return sb.ToString();
        }

        Regex BuildRegex(bool rightToLeft, out string error)
        {
            error = null;
            string pat = _sMode == ModeRegex ? _sFind
                : Regex.Escape(_sMode == ModeExtended ? Unescape(_sFind) : _sFind);
            if (_sWord && _sMode != ModeRegex) pat = @"\b(?:" + pat + @")\b";
            var o = RegexOptions.None;
            if (!_sCase) o |= RegexOptions.IgnoreCase;
            if (rightToLeft) o |= RegexOptions.RightToLeft;
            if (_sMode == ModeRegex && _sDotNL) o |= RegexOptions.Singleline;
            try { return new Regex(pat, o); }
            catch (System.ArgumentException ex) { error = "Invalid regex: " + ex.Message; return null; }
        }

        string ReplacementFor(Match m) => _sMode == ModeRegex ? m.Result(_sReplace ?? "")
            : _sMode == ModeExtended ? Unescape(_sReplace) : (_sReplace ?? "");

        /// <summary>The [start, end) range a doc-scoped command works on:
        /// the selection when "In selection" is on and one exists, else the
        /// whole content.</summary>
        void DocRange(string content, out int start, out int end)
        {
            start = 0;
            end = content.Length;
            if (_sInSel)
            {
                _owner.GetSelectionSpan(out int s, out int e);
                if (e > s) { start = s; end = Mathf.Min(e, content.Length); }
            }
        }

        /// <summary>Walks matches of <paramref name="rx"/> within [s, e) of
        /// content, reporting each with its 0-based line/col and line text.</summary>
        static void ScanMatches(string content, Regex rx, int s, int e,
            System.Action<Match, int, int, string> report)
        {
            int line = 0, lineStart = 0, scanned = 0;
            for (var m = rx.Match(content, s); m.Success && m.Index + m.Length <= e; m = m.NextMatch())
            {
                if (m.Length == 0) break; // zero-width would loop
                for (; scanned < m.Index; scanned++)
                    if (content[scanned] == '\n') { line++; lineStart = scanned + 1; }
                int lineEnd = content.IndexOf('\n', m.Index);
                if (lineEnd < 0) lineEnd = content.Length;
                string text = content.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
                if (text.Length > 200) text = text.Substring(0, 200);
                report(m, line, m.Index - lineStart, text);
            }
        }

        // ---------- Find / Replace tabs (current document) ----------

        public void FindNextCore(bool reverseOnce)
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            bool back = _sBack ^ reverseOnce;
            var rx = BuildRegex(back, out string err);
            if (rx == null) { SetStatus(err); return; }

            int active = _owner.ActiveIndex;
            _owner.GetSelectionSpan(out int selS, out int selE);
            string content = _owner.GetDocContent(active);
            int from = back ? selS : selE;
            var m = back ? rx.Match(content, 0, Mathf.Clamp(from, 0, content.Length))
                         : rx.Match(content, Mathf.Clamp(from, 0, content.Length));
            if (m.Success) { Select(active, m); return; }
            if (_sWrap)
            {
                var mw = back ? rx.Match(content, 0, content.Length) : rx.Match(content, 0);
                if (mw.Success) { Select(active, mw); SetStatus("Wrapped."); return; }
            }
            SetStatus("Not found: " + _sFind);
        }

        void Select(int doc, Match m)
        {
            if (doc != _owner.ActiveIndex) _owner.SwitchTab(doc);
            _owner.SelectSpan(m.Index, m.Index + m.Length);
            SetStatus(_owner.GetDocName(doc) + "  " + (m.Index + 1));
        }

        void ReplaceOnce()
        {
            if (!OwnerOk()) return;
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }
            _owner.GetSelectionSpan(out int s, out int e);
            if (e > s)
            {
                string content = _owner.GetDocContent(_owner.ActiveIndex);
                var m = rx.Match(content, s);
                if (m.Success && m.Index == s && m.Length == e - s)
                    _owner.ReplaceSpanInActive(s, e, ReplacementFor(m));
            }
            FindNextCore(false);
        }

        void ReplaceAllCurrent()
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }

            int active = _owner.ActiveIndex;
            string content = _owner.GetDocContent(active);
            DocRange(content, out int s, out int e);
            var hits = new List<Match>();
            foreach (Match m in rx.Matches(content))
                if (m.Length > 0 && m.Index >= s && m.Index + m.Length <= e) hits.Add(m);
            if (hits.Count == 0) { SetStatus("Not found: " + _sFind); return; }
            var sb = new System.Text.StringBuilder(content);
            for (int i = hits.Count - 1; i >= 0; i--)
                sb.Remove(hits[i].Index, hits[i].Length).Insert(hits[i].Index, ReplacementFor(hits[i]));
            _owner.SetDocContent(active, sb.ToString());
            SetStatus(string.Format(L10n.Tr("Replaced {0} match(es) in {1} file(s)."), hits.Count, 1));
        }

        void ReplaceAllOpenDocs()
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }

            int total = 0, docsTouched = 0;
            for (int i = 0; i < _owner.DocCount; i++)
            {
                if (_owner.IsSettingsTab(i)) continue;
                string content = _owner.GetDocContent(i);
                int count = rx.Matches(content).Count;
                if (count == 0) continue;
                _owner.SetDocContent(i, rx.Replace(content, ReplacementFor));
                total += count;
                docsTouched++;
            }
            SetStatus(total == 0 ? "Not found: " + _sFind
                : string.Format(L10n.Tr("Replaced {0} match(es) in {1} file(s)."), total, docsTouched));
        }

        void CountCommand()
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }
            string content = _owner.GetDocContent(_owner.ActiveIndex);
            DocRange(content, out int s, out int e);
            int n = 0;
            foreach (Match m in rx.Matches(content))
                if (m.Length > 0 && m.Index >= s && m.Index + m.Length <= e) n++;
            SetStatus(string.Format(L10n.Tr("{0} match(es)."), n));
        }

        /// <summary>Find All: lists every hit in the editor's Search Results
        /// console tab (current document, or every opened document).</summary>
        void FindAllDocs(bool currentOnly)
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }

            var items = new List<TextEditorWindow.PickLocation>();
            int docsWithHits = 0;
            for (int i = 0; i < _owner.DocCount; i++)
            {
                if (currentOnly && i != _owner.ActiveIndex) continue;
                if (_owner.IsSettingsTab(i)) continue;
                string content = _owner.GetDocContent(i);
                int s = 0, e = content.Length;
                if (currentOnly) DocRange(content, out s, out e);
                string path = _owner.GetDocPath(i);
                int before = items.Count;
                int docId = path == null ? i + 1 : 0;
                string display = path ?? _owner.GetDocName(i);
                ScanMatches(content, rx, s, e, (m, line, col, text) =>
                    items.Add(new TextEditorWindow.PickLocation
                    { Path = display, DocId = docId, Line = line, Col = col, Preview = text }));
                if (items.Count > before) docsWithHits++;
            }
            string title = string.Format(L10n.Tr("'{0}' — {1} match(es) in {2} file(s)"),
                _sFind, items.Count, docsWithHits);
            _owner.ShowSearchResults(title, items);
            SetStatus(title);
        }

        // ---------- Find in Files tab ----------

        void StartFilesSearch(bool replaceMode)
        {
            if (_searching || string.IsNullOrEmpty(_sFind) || !ResolveOwner()) return;
            var o = new FindInFiles.Options
            {
                Query = _sMode == ModeExtended ? Unescape(_sFind) : _sFind,
                Replace = _sMode == ModeExtended ? Unescape(_sReplace) : (_sReplace ?? ""),
                Regex = _sMode == ModeRegex,
                MatchCase = _sCase,
                WholeWord = _sWord,
                Glob = _sFilters,
                DotNL = _sMode == ModeRegex && _sDotNL,
                NoRecurse = !_sSubFolders,
                Hidden = _sHidden,
            };
            if (o.Regex)
            {
                try { _ = new Regex(o.Query); }
                catch (System.Exception ex)
                { SetStatus(string.Format(L10n.Tr("Invalid regex: {0}"), ex.Message)); return; }
            }
            string followDir = null;
            if (_sFollowDoc && _owner.DocCount > 0)
            {
                string activePath = _owner.GetDocPath(_owner.ActiveIndex);
                if (activePath != null) followDir = Path.GetDirectoryName(activePath);
            }
            string[] roots = followDir != null ? new[] { followDir }
                : string.IsNullOrWhiteSpace(_sDir) ? FindInFiles.DefaultRoots()
                : new[] { _sDir.Trim() };
            var open = _owner.OpenDocSnapshots();
            _searching = true;
            SetStatus(L10n.Tr("Searching..."));
            var ctx = _ctx;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<FindInFiles.Match> found = null;
                int scanned = 0; bool truncated = false; string error = null;
                try { found = FindInFiles.Search(o, roots, open, out scanned, out truncated); }
                catch (System.Exception ex) { error = ex.Message; }
                ctx.Post(_ =>
                {
                    _searching = false;
                    if (this == null) return;
                    if (error != null) { SetStatus(error); return; }
                    if (replaceMode) FinishFilesReplace(found);
                    else FinishFilesFind(found, scanned, truncated);
                }, null);
            });
        }

        void FinishFilesFind(List<FindInFiles.Match> found, int scanned, bool truncated)
        {
            if (!ResolveOwner()) return;
            var items = found.Select(m => new TextEditorWindow.PickLocation
            { Path = m.Path, Line = m.Line, Col = m.Col, Preview = m.LineText }).ToList();
            int files = found.Select(m => m.Path).Distinct().Count();
            _owner.ShowSearchResults(
                string.Format(L10n.Tr("'{0}' — {1} match(es) in {2} file(s)"), _sFind, found.Count, files),
                items);
            SetStatus(string.Format(L10n.Tr("{0} match(es) in {1} file(s), {2} file(s) scanned."),
                found.Count, files, scanned) + (truncated ? "  " + L10n.Tr("(truncated)") : ""));
        }

        void FinishFilesReplace(List<FindInFiles.Match> found)
        {
            if (!ResolveOwner()) return;
            if (found.Count == 0) { SetStatus(L10n.Tr("No matches.")); return; }
            var (files, replaced, skipped) = _owner.ApplyReplaceInFiles(found);
            SetStatus(string.Format(L10n.Tr("Replaced {0} match(es) in {1} file(s)."), replaced, files)
                + (skipped > 0 ? "  " + string.Format(L10n.Tr("Skipped {0} stale match(es)."), skipped) : ""));
        }

        // ---------- Mark tab (bookmarks matching lines) ----------

        void MarkAllCommand()
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }
            string content = _owner.GetDocContent(_owner.ActiveIndex);
            DocRange(content, out int s, out int e);
            var lines = new HashSet<int>();
            ScanMatches(content, rx, s, e, (m, line, col, text) => lines.Add(line));
            _owner.MarkBookmarkLines(lines, _sPurge);
            SetStatus(string.Format(L10n.Tr("Bookmarked {0} line(s)."), lines.Count));
        }

        void CopyMatchedCommand()
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }
            string content = _owner.GetDocContent(_owner.ActiveIndex);
            DocRange(content, out int s, out int e);
            var sb = new System.Text.StringBuilder();
            ScanMatches(content, rx, s, e, (m, line, col, text) => sb.Append(m.Value).Append('\n'));
            if (sb.Length == 0) { SetStatus(L10n.Tr("No matches.")); return; }
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            SetStatus(L10n.Tr("Copied."));
        }
    }
}
#endif
