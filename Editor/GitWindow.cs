#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// The Git panel: working-tree status with per-file stage/unstage
    /// checkboxes, a commit box, Push — and the branch-history tree, drawn as
    /// a commit graph (lanes + edges via Painter2D) that can be laid out
    /// vertically or horizontally (toggle button in the view). The tree is
    /// INTERACTIVE: click a commit to select it, and use its context menu to
    /// check out a branch, create a branch at the commit, or check out the
    /// commit detached — refused while the working tree is dirty.
    /// </summary>
    public class GitWindow : EditorWindow
    {
        static GitWindow _instance;

        [SerializeField] TextEditorWindow _owner; // an object ref survives reloads
        System.Threading.SynchronizationContext _ctx;
        [SerializeField] string _root;            // repo root — the rebuild key
        [SerializeField] string _draftMsg = "";   // working-tree commit draft, kept across reloads

        Label _header, _status;
        ScrollView _changes;
        TextField _commitMsg;
        Button _selectBtn;      // bulk checkbox (staging) menu
        TextField _filterField; // substring filter over the file list
        [System.NonSerialized] string _filterText = "";
        [System.NonSerialized] string _lastNameStatus; // inspected commit's file rows, for re-filtering
        TextField _giDate, _giHash, _giAuthor, _giTitle; // inspected-commit meta, read-only
        Button _commitBtn, _pushBtn, _orientBtn;
        Label _changesTitle;
        Button _backBtn;
        // Commit inspection: clicking a graph node shows ITS files and
        // message on the left; null = the normal working-tree view.
        // [NonSerialized] matters: Unity's domain-reload hot-serialization
        // captures even private fields, turning a null string into "" — which
        // silently defeats every == null check (an "inspection" of commit ""
        // suppressed RebuildChanges, leaving the file list empty forever).
        [System.NonSerialized] string _inspectHash;
        [System.NonSerialized] string _inspectOriginalMsg = "";
        [System.NonSerialized] bool _inspectIsHead;
        ScrollView _graphScroll;
        VisualElement _graphCanvas;
        bool _horizontal;

        List<GitService.StatusEntry> _entries = new List<GitService.StatusEntry>();
        List<GitService.GraphNode> _nodes = new List<GitService.GraphNode>();
        // null = HEAD is the active node; hot-serialization would turn it
        // into "" (no node active). _busy stuck true across a reload would
        // block every future Refresh. Both must reset with the domain.
        [System.NonSerialized] string _selectedHash;
        [System.NonSerialized] bool _busy;

        const float NodeGap = 26f, LaneGap = 18f, Radius = 4.5f;
        const float LabelSpace = 340f;

        public static void Open(TextEditorWindow owner, string repoRoot)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<GitWindow>();
                _instance.titleContent = new GUIContent("Git");
                _instance.minSize = new Vector2(660, 420);
                _instance.ShowUtility();
            }
            _instance._owner = owner;
            _instance._root = repoRoot;
            _instance._ctx = System.Threading.SynchronizationContext.Current;
            // Opening always starts at HEAD: the working tree is the active
            // node and the left pane shows the staging view.
            _instance._selectedHash = null;
            _instance._inspectHash = null;
            _instance._inspectOriginalMsg = "";
            _instance._inspectIsHead = false;
            _instance.BuildUI();
            _instance.Refresh();
            _instance.Focus();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void OnEnable() { if (_instance == null) _instance = this; }

        /// <summary>Reload survivor: the window outlives the domain but its
        /// UI and live references do not. Rebuild from the serialized repo
        /// root, re-acquire the main-thread context and the owner window,
        /// and re-read git state — the panel comes back at the HEAD /
        /// working-tree view, fully operational.</summary>
        void CreateGUI()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                if (_header != null || string.IsNullOrEmpty(_root)) return; // fresh Open() builds
                if (_owner == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
                    _owner = all.Length > 0 ? all[0] : null;
                }
                BuildUI();
                // The refresh is DEFERRED to delayCall: this early post-reload
                // tick's SynchronizationContext is not yet the functional
                // Unity context, so a ctx captured here posts into the void —
                // Refresh's completion never lands and the panel sticks at
                // "Working…" with an empty file list.
                // The refresh must wait for the REAL Unity synchronization
                // context — a context captured this early swallows Posts and
                // the panel would stick at "Working…" with an empty list.
                AteMainCtx.WhenReady(ctx =>
                {
                    if (this == null) return;
                    _ctx = ctx;
                    Refresh();
                });
            });
        }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = 6;
            root.style.paddingBottom = 8; // keep the panel frames off the window edge

            _header = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold } };
            root.Add(_header);
            _status = new Label { style = { opacity = 0.8f, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_status);

            // A TwoPaneSplitView (UIToolkit's splitter): the divider between
            // Changes and Branch History is draggable; the chosen width of
            // the fixed (left) pane persists across sessions.
            float savedWidth = EditorPrefs.GetFloat("ATE.Git.SplitWidth", 300f);
            var split = new TwoPaneSplitView(0, savedWidth, TwoPaneSplitViewOrientation.Horizontal)
            { style = { flexGrow = 1 } };

            // Framed panels: the same bordered-subview look the console area
            // uses, on both panes and the commit-message box.
            void Frame(VisualElement v)
            {
                var bc = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                v.style.borderTopWidth = 1; v.style.borderBottomWidth = 1;
                v.style.borderLeftWidth = 1; v.style.borderRightWidth = 1;
                v.style.borderTopColor = bc; v.style.borderBottomColor = bc;
                v.style.borderLeftColor = bc; v.style.borderRightColor = bc;
                v.style.borderTopLeftRadius = 3; v.style.borderTopRightRadius = 3;
                v.style.borderBottomLeftRadius = 3; v.style.borderBottomRightRadius = 3;
            }

            // --- Left: changes + commit ---
            var left = new VisualElement { style = { minWidth = 180, flexDirection = FlexDirection.Column,
                paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 4, marginRight = 3 } };
            Frame(left);
            left.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > 0)
                    EditorPrefs.SetFloat("ATE.Git.SplitWidth", e.newRect.width);
            });
            // The header keeps its height and truncates long titles with an
            // ellipsis — a "Commit <hash>" title must never overflow into
            // the file list below.
            var changesHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
            _changesTitle = new Label(L10n.Tr("Changes")) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexShrink = 1,
                whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } };
            _backBtn = new Button(LeaveInspect) { text = L10n.Tr("Working Tree"),
                tooltip = L10n.Tr("Back to the working-tree changes."),
                style = { display = DisplayStyle.None } };
            changesHeader.Add(_changesTitle);
            _selectBtn = new Button { text = L10n.Tr("Select") + " ▾",
                tooltip = L10n.Tr("Change which files are checked (staged) in one step."),
                style = { flexShrink = 0, marginLeft = 4 } };
            _selectBtn.clicked += ShowSelectMenu;
            changesHeader.Add(_selectBtn);
            // Filter sits right-aligned in the header; the spacer soaks up
            // the middle so a long "Commit <hash>" title still truncates.
            changesHeader.Add(new VisualElement { style = { flexGrow = 1 } });
            changesHeader.Add(new Label(L10n.Tr("Filter"))
            { style = { flexShrink = 0, marginRight = 2, alignSelf = Align.Center },
              tooltip = L10n.Tr("Show only changed files whose path contains this text.") });
            _filterField = new TextField
            { style = { width = 90, flexShrink = 0 },
              tooltip = L10n.Tr("Show only changed files whose path contains this text.") };
            _filterField.RegisterValueChangedCallback(e =>
            {
                _filterText = e.newValue ?? "";
                if (_inspectHash == null) RebuildChanges();
                else if (_lastNameStatus != null) RebuildCommitFiles(_lastNameStatus);
            });
            changesHeader.Add(_filterField);
            changesHeader.Add(_backBtn);
            // A rule under the header separates title/filter from the list.
            changesHeader.style.borderBottomWidth = 1;
            changesHeader.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            changesHeader.style.paddingBottom = 2;
            changesHeader.style.marginBottom = 3;
            left.Add(changesHeader);
            _changes = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            left.Add(_changes);

            // Inspected-commit meta (date / hash / author / title) in
            // read-only fields, mirroring the Time Lapse window; hidden
            // while the working tree is shown.
            var giMeta = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0, marginTop = 2 } };
            _giDate = new TextField
            {
                isReadOnly = true,
                tooltip = L10n.Tr("The shown revision's commit date. Read-only."),
                style = { fontSize = 11, width = 92, flexShrink = 0, display = DisplayStyle.None }
            };
            giMeta.Add(_giDate);
            _giHash = new TextField
            {
                isReadOnly = true,
                tooltip = L10n.Tr("The shown revision's short commit hash. Read-only."),
                style = { fontSize = 11, width = 78, flexShrink = 0, display = DisplayStyle.None }
            };
            giMeta.Add(_giHash);
            _giAuthor = new TextField
            {
                isReadOnly = true,
                tooltip = L10n.Tr("The shown revision's author. Read-only."),
                style = { fontSize = 11, flexGrow = 1, display = DisplayStyle.None }
            };
            giMeta.Add(_giAuthor);
            left.Add(giMeta);
            _giTitle = new TextField
            {
                isReadOnly = true,
                tooltip = L10n.Tr("The shown revision's commit title (subject line). Read-only; the text can be selected and copied."),
                style = { fontSize = 11, flexShrink = 0, display = DisplayStyle.None }
            };
            left.Add(_giTitle);

            left.Add(new Label(L10n.Tr("Commit message")) { style = { marginTop = 4 } });
            _commitMsg = new TextField { multiline = true, style = { height = 56, fontSize = 11 },
                tooltip = L10n.Tr("The message for the next commit.") };
            // Long messages (multi-paragraph bodies, inspected commits) need
            // to scroll inside the fixed-height box.
            _commitMsg.verticalScrollerVisibility = ScrollerVisibility.Auto;
            Frame(_commitMsg);
            // The working-tree DRAFT is serialized: it survives domain
            // reloads and inspect round-trips. Inspected commit messages are
            // deliberately not stored here.
            _commitMsg.value = _draftMsg ?? "";
            _commitMsg.RegisterValueChangedCallback(e =>
            { if (_inspectHash == null) _draftMsg = e.newValue; });
            left.Add(_commitMsg);
            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 4 } };
            _commitBtn = new Button(Commit) { text = L10n.Tr("Commit"),
                tooltip = L10n.Tr("Commit the checked files with the message above.") };
            _pushBtn = new Button(Push) { text = L10n.Tr("Push"),
                tooltip = L10n.Tr("Push the current branch to its remote.") };
            btnRow.Add(_commitBtn);
            btnRow.Add(_pushBtn);
            left.Add(btnRow);
            split.Add(left);

            // --- Right: branch graph ---
            var right = new VisualElement { style = { minWidth = 200, flexGrow = 1, flexDirection = FlexDirection.Column,
                paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 4, marginLeft = 3 } };
            Frame(right);
            var graphBar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            graphBar.Add(new Label(L10n.Tr("Branch History")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8 } });
            _orientBtn = new Button(() => { _horizontal = !_horizontal; RebuildGraph(); })
            { text = _horizontal ? "⇋" : "⇅", tooltip = L10n.Tr("Toggle vertical / horizontal layout") };
            graphBar.Add(_orientBtn);
            var refresh = new Button(Refresh) { text = L10n.Tr("Refresh"),
                tooltip = L10n.Tr("Re-read the repository status and branch history.") };
            graphBar.Add(refresh);
            // Matching rule under the Branch History bar.
            graphBar.style.borderBottomWidth = 1;
            graphBar.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            graphBar.style.paddingBottom = 2;
            graphBar.style.marginBottom = 3;
            right.Add(graphBar);
            _graphScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1 } };
            _graphCanvas = new VisualElement { style = { position = Position.Relative, flexShrink = 0 } };
            _graphCanvas.generateVisualContent += DrawGraphEdges;
            _graphScroll.Add(_graphCanvas);
            // The horizontal layout is short — keep it vertically centered
            // in the view instead of hugging the top (re-runs on resize).
            _graphScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => CenterGraphVertically());
            right.Add(_graphScroll);
            split.Add(right);

            root.Add(split);
        }

        void Refresh()
        {
            if (_busy || string.IsNullOrEmpty(_root)) return;
            _busy = true;
            _status.text = L10n.Tr("Working…");
            string rootDir = _root;
            var ctx = _ctx;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<GitService.StatusEntry> entries = null;
                List<GitService.GraphNode> nodes = null;
                string branch = null;
                try
                {
                    entries = GitService.Status(rootDir);
                    nodes = GitService.Graph(rootDir);
                    GitService.Run(rootDir, "branch --show-current", out branch, out _);
                }
                catch (System.Exception) { }
                ctx.Post(_ =>
                {
                    _busy = false;
                    if (this == null || _changes == null) return;
                    _entries = entries ?? new List<GitService.StatusEntry>();
                    _nodes = nodes ?? new List<GitService.GraphNode>();
                    _header.text = rootDir + "   —   " + (branch ?? "").Trim();
                    _status.text = _entries.Count == 0 ? L10n.Tr("Working tree clean.")
                        : string.Format(L10n.Tr("{0} changed file(s)."), _entries.Count);
                    if (_inspectHash == null) RebuildChanges(); // keep an open commit inspection
                    RebuildGraph();
                }, null);
            });
        }

        // ---- Changes / stage / commit / push ----

        bool PassesFilter(string path) =>
            string.IsNullOrEmpty(_filterText) ||
            path.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Status-letter tint: green added/untracked, amber
        /// modified, red deleted, blue renamed/copied — the git gutter
        /// palette, so the letters read the same as the code view marks.</summary>
        static Color StateColor(string state)
        {
            char c = state.Length > 0 ? state[0] : ' ';
            switch (c)
            {
                case 'A': case '?': return new Color(0.35f, 0.75f, 0.4f);
                case 'M': case 'U': return new Color(0.85f, 0.65f, 0.2f);
                case 'D': return new Color(0.9f, 0.35f, 0.3f);
                case 'R': case 'C': return new Color(0.45f, 0.65f, 0.9f);
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        void RebuildChanges()
        {
            _changes.Clear();
            foreach (var e in _entries)
            {
                var entry = e;
                if (!PassesFilter(entry.Path)) continue;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                bool staged = entry.Index != ' ' && entry.Index != '?';
                var t = new Toggle { value = staged, tooltip = L10n.Tr("Staged for commit") };
                t.RegisterValueChangedCallback(ev => RunGit(ev.newValue
                    ? "add -- \"" + entry.Path + "\""
                    : "restore --staged -- \"" + entry.Path + "\""));
                row.Add(t);
                string state = ("" + entry.Index + entry.Work).Trim();
                row.Add(new Label(state) { style = { width = 24, color = StateColor(state) } });
                var lbl = new Label(entry.Path) { style = { overflow = Overflow.Hidden, whiteSpace = WhiteSpace.NoWrap } };
                lbl.tooltip = entry.Path + "\n" + L10n.Tr("Double-click: diff against the previous version (HEAD).");
                lbl.RegisterCallback<PointerDownEvent>(ev =>
                {
                    if (ev.clickCount >= 2) DiffWorkingEntry(entry);
                });
                row.Add(lbl);
                row.RegisterCallback<MouseUpEvent>(ev =>
                {
                    if (ev.button != 1) return;
                    ev.StopPropagation();
                    ShowEntryMenu(entry.Path, staged, () => DiffWorkingEntry(entry));
                });
                _changes.Add(row);
            }
        }

        /// <summary>Context menu for a file row — working tree and inspected
        /// commits alike: diff, open, reveal, stage/unstage (working tree
        /// only), and the Git submenu incl. Time Lapse. Git Panel itself is
        /// pruned: this IS the Git Panel.</summary>
        void ShowEntryMenu(string relPath, bool? staged, System.Action diff)
        {
            var m = new GenericMenu();
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relPath));
            string name = System.IO.Path.GetFileName(relPath);
            bool onDisk = System.IO.File.Exists(full);
            m.AddItem(new GUIContent(_inspectHash == null
                ? L10n.Tr("Diff Against Previous Version") : L10n.Tr("Diff Against Parent")), false, () => diff());
            if (onDisk)
            {
                m.AddItem(new GUIContent(L10n.Tr("Open in Text Editor")), false,
                    () => TextEditorWindow.OpenExternal(full, 1, 1));
                m.AddItem(new GUIContent(L10n.Tr("Show in File Explorer")), false,
                    () => EditorUtility.RevealInFinder(full));
            }
            if (staged != null)
            {
                m.AddSeparator("");
                if (staged.Value)
                    m.AddItem(new GUIContent(L10n.Tr("Unstage")), false,
                        () => RunGit("restore --staged -- \"" + relPath + "\""));
                else
                    m.AddItem(new GUIContent(L10n.Tr("Stage")), false,
                        () => RunGit("add -- \"" + relPath + "\""));
            }
            if (onDisk && _owner != null)
            {
                m.AddSeparator("");
                string gitRoot = L10n.Tr("Git") + "/";
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("Blame Current File")), false,
                    () => _owner.GitBlameFor(full, name));
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("File History...")), false,
                    () => _owner.GitFileHistoryFor(full, name, _changes));
                m.AddItem(new GUIContent(gitRoot + L10n.Tr("Time Lapse Current File...")), false,
                    () => _owner.GitTimeLapseFor(full, name));
            }
            m.ShowAsContext();
        }

        /// <summary>The Select ▾ menu: sets the staging checkboxes of the
        /// currently FILTERED working-tree rows in one step.</summary>
        void ShowSelectMenu()
        {
            if (_inspectHash != null) return;
            var visible = _entries.Where(e => PassesFilter(e.Path)).ToList();
            var m = new GenericMenu();
            void Item(string label, System.Func<GitService.StatusEntry, bool> want)
            {
                m.AddItem(new GUIContent(label), false, () =>
                {
                    var stage = new List<string>();
                    var unstage = new List<string>();
                    foreach (var e in visible)
                    {
                        bool staged = e.Index != ' ' && e.Index != '?';
                        bool target = want(e);
                        if (target && !staged) stage.Add(e.Path);
                        else if (!target && staged) unstage.Add(e.Path);
                    }
                    RunGitBatch("add", stage, () => RunGitBatch("restore --staged", unstage));
                });
            }
            bool Untracked(GitService.StatusEntry e) => e.Index == '?' || e.Work == '?';
            bool Modified(GitService.StatusEntry e) => e.Index == 'M' || e.Work == 'M';
            bool Added(GitService.StatusEntry e) => e.Index == 'A' || e.Work == 'A';
            bool Deleted(GitService.StatusEntry e) => e.Index == 'D' || e.Work == 'D';
            bool Renamed(GitService.StatusEntry e) => e.Index == 'R' || e.Work == 'R';
            Item(L10n.Tr("Select All"), _ => true);
            Item(L10n.Tr("Select None"), _ => false);
            m.AddSeparator("");
            Item(L10n.Tr("Select Modified"), Modified);
            Item(L10n.Tr("Select Untracked"), Untracked);
            Item(L10n.Tr("Select Added"), Added);
            Item(L10n.Tr("Select Deleted"), Deleted);
            Item(L10n.Tr("Select Renamed"), Renamed);
            m.AddSeparator("");
            Item(L10n.Tr("Invert Selection"), e => !(e.Index != ' ' && e.Index != '?'));
            m.ShowAsContext();
        }

        /// <summary>Runs a path-taking git command over an arbitrarily long
        /// path list by CHUNKING it — one giant command line blew straight
        /// through the OS argument-length limit the first time Select All
        /// met an untracked build folder. Chunks chain through RunGit's
        /// completion callback; an empty list just advances the chain.</summary>
        void RunGitBatch(string command, List<string> paths, System.Action after = null)
        {
            const int Chunk = 32;
            if (paths == null || paths.Count == 0) { after?.Invoke(); return; }
            var batch = paths.Take(Chunk).Select(p => "\"" + p.Replace("\"", "\\\"") + "\"");
            var rest = paths.Skip(Chunk).ToList();
            RunGit(command + " -- " + string.Join(" ", batch), () => RunGitBatch(command, rest, after));
        }

        void RunGit(string args, System.Action after = null)
        {
            if (_busy || string.IsNullOrEmpty(_root)) return;
            _busy = true;
            _status.text = L10n.Tr("Working…");
            string rootDir = _root;
            var ctx = _ctx;
            System.Threading.Tasks.Task.Run(() =>
            {
                int code = GitService.Run(rootDir, args, out var so, out var se);
                ctx.Post(_ =>
                {
                    _busy = false;
                    if (this == null) return;
                    if (code != 0)
                    {
                        string msg = (se + "\n" + so).Trim();
                        _status.text = msg.Length > 300 ? msg.Substring(0, 297) + "..." : msg;
                        // Batched commands can carry hundreds of paths —
                        // echoing them whole turned the console to soup.
                        string shownArgs = args.Length > 160 ? args.Substring(0, 157) + "…" : args;
                        if (msg.Length > 400) msg = msg.Substring(0, 397) + "…";
                        AteConsole.Warn("[ADKOM Text Editor] git " + shownArgs + " failed: " + msg);
                    }
                    after?.Invoke();
                    Refresh();
                    _owner?.RefreshGitMarksAsync();
                }, null);
            });
        }

        void Commit()
        {
            string msg = (_commitMsg.value ?? "").Trim();
            if (msg.Length == 0) { _status.text = L10n.Tr("Enter a commit message."); return; }
            if (_inspectHash != null)
            {
                // Inspecting a commit: the button is Amend. Only HEAD can be
                // amended (anything older would rewrite descendant history).
                if (!_inspectIsHead) return;
                if (msg == _inspectOriginalMsg.Trim())
                { _status.text = L10n.Tr("Message unchanged — nothing to amend."); return; }
                // --only with no paths = message-only amend: staged changes
                // stay staged instead of being swept into the old commit.
                RunGit("commit --amend --only -m \"" + msg.Replace("\"", "\\\"") + "\"", LeaveInspect);
                return;
            }
            RunGit("commit -m \"" + msg.Replace("\"", "\\\"") + "\"", () => _commitMsg.value = "");
        }

        void Push() => RunGit("push");

        // ---- Commit inspection (click a graph node) ----

        /// <summary>Shows a commit's file list on the left and its message in
        /// the message box. On HEAD the Commit button becomes Amend: editing
        /// the message and pressing it rewrites the message in place.</summary>
        void InspectCommit(string hash)
        {
            if (string.IsNullOrEmpty(_root) || string.IsNullOrEmpty(hash)) return;
            string rootDir = _root;
            var ctx = _ctx;
            System.Threading.Tasks.Task.Run(() =>
            {
                string files = null, msg = null, head = null, meta = null;
                try
                {
                    GitService.Run(rootDir, "show --name-status --format= " + hash, out files, out _);
                    // A clean MERGE commit shows an empty combined diff —
                    // fall back to the first-parent diff, which is "what
                    // this merge landed on the branch".
                    if (string.IsNullOrWhiteSpace(files))
                        GitService.Run(rootDir, "diff-tree --no-commit-id --name-status -r " + hash + "^ " + hash,
                            out files, out _);
                    GitService.Run(rootDir, "log -1 --format=%B " + hash, out msg, out _);
                    GitService.Run(rootDir, "log -1 --date=short --format=%h%x01%an%x01%ad%x01%s " + hash, out meta, out _);
                    GitService.Run(rootDir, "rev-parse HEAD", out head, out _);
                }
                catch (System.Exception) { }
                ctx.Post(_ =>
                {
                    if (this == null || _changes == null) return;
                    head = (head ?? "").Trim();
                    _inspectHash = hash;
                    _inspectIsHead = head.Length > 0 &&
                        (head.StartsWith(hash) || hash.StartsWith(head));
                    _inspectOriginalMsg = (msg ?? "").TrimEnd('\n', '\r');
                    _commitMsg.value = _inspectOriginalMsg;
                    // Meta fields (date / hash / author / title), same as
                    // the Time Lapse window's read-only info block.
                    var mp = (meta ?? "").Trim().Split('\u0001');
                    _giHash.SetValueWithoutNotify(mp.Length > 0 ? mp[0] : hash);
                    _giAuthor.SetValueWithoutNotify(mp.Length > 1 ? mp[1] : "");
                    _giDate.SetValueWithoutNotify(mp.Length > 2 ? mp[2] : "");
                    _giTitle.SetValueWithoutNotify(mp.Length > 3 ? mp[3] : "");
                    SetInspectMetaVisible(true);
                    RebuildCommitFiles(files ?? "");
                    UpdateActionButtons();
                }, null);
            });
        }

        /// <summary>The inspected commit's files: "M\tpath" name-status rows;
        /// renames arrive as "R100\told\tnew" — show the new path.</summary>
        void RebuildCommitFiles(string nameStatus)
        {
            _lastNameStatus = nameStatus; // filter edits re-run this
            _changes.Clear();
            _changesTitle.text = string.Format(L10n.Tr("Commit {0}"), _inspectHash);
            _backBtn.style.display = DisplayStyle.Flex;
            _selectBtn.style.display = DisplayStyle.None; // staging is working-tree-only
            foreach (var raw in nameStatus.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split('\t');
                string state = parts[0], path = parts[parts.Length - 1];
                if (!PassesFilter(path)) continue;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.Add(new Label(state) { style = { width = 32, color = StateColor(state) } });
                var lbl = new Label(path) { style = { overflow = Overflow.Hidden, whiteSpace = WhiteSpace.NoWrap } };
                lbl.tooltip = path + "\n" + L10n.Tr("Double-click: diff this commit's version against its parent.");
                string p = path;
                string hash = _inspectHash;
                lbl.RegisterCallback<PointerDownEvent>(ev =>
                {
                    if (ev.clickCount >= 2) DiffCommitEntry(p, hash);
                });
                row.Add(lbl);
                row.RegisterCallback<MouseUpEvent>(ev =>
                {
                    if (ev.button != 1) return;
                    ev.StopPropagation();
                    ShowEntryMenu(p, null, () => DiffCommitEntry(p, hash));
                });
                _changes.Add(row);
            }
        }

        void SetInspectMetaVisible(bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            _giDate.style.display = d;
            _giHash.style.display = d;
            _giAuthor.style.display = d;
            _giTitle.style.display = d;
        }

        /// <summary>Double-click on a working-tree change: diff the previous
        /// version (HEAD) against the file as it is now.</summary>
        void DiffWorkingEntry(GitService.StatusEntry entry)
        {
            string abs = System.IO.Path.Combine(_root, entry.Path);
            bool untracked = entry.Index == '?' || entry.Work == '?';
            string prev = untracked ? "" : (GitService.ShowFileAt(abs, "HEAD") ?? "");
            string prevLabel = entry.Path + " @ HEAD" + (untracked ? " " + L10n.Tr("(new file)") : "");
            if (System.IO.File.Exists(abs))
                AteDiffWindow.OpenTextVsFile(prevLabel, prev, abs, entry.Path + " " + L10n.Tr("(working)"));
            else
                AteDiffWindow.OpenTexts(prevLabel, prev, entry.Path + " " + L10n.Tr("(deleted)"), "");
        }

        /// <summary>Double-click on an inspected commit's file: diff the
        /// parent's version against this commit's version.</summary>
        void DiffCommitEntry(string relPath, string hash)
        {
            string abs = System.IO.Path.Combine(_root, relPath);
            string before = GitService.ShowFileAt(abs, hash + "^") ?? "";
            string after = GitService.ShowFileAt(abs, hash) ?? "";
            AteDiffWindow.OpenTexts(relPath + " @ " + hash + "^", before, relPath + " @ " + hash, after);
        }

        void LeaveInspect()
        {
            _inspectHash = null;
            _inspectOriginalMsg = "";
            _inspectIsHead = false;
            _lastNameStatus = null;
            _changesTitle.text = L10n.Tr("Changes");
            _backBtn.style.display = DisplayStyle.None;
            _selectBtn.style.display = DisplayStyle.Flex;
            SetInspectMetaVisible(false);
            _commitMsg.value = _draftMsg ?? ""; // the draft survives an inspect round-trip
            _commitMsg.isReadOnly = false;
            RebuildChanges();
            UpdateActionButtons();
        }

        void UpdateActionButtons()
        {
            if (_inspectHash == null)
            {
                _commitBtn.text = L10n.Tr("Commit");
                _commitBtn.tooltip = L10n.Tr("Commit the checked files with the message above.");
                _commitBtn.SetEnabled(true);
                _commitMsg.isReadOnly = false;
                return;
            }
            _commitBtn.text = L10n.Tr("Amend");
            if (_inspectIsHead)
            {
                _commitBtn.tooltip = L10n.Tr("Rewrite this commit's message (message-only amend; staged files are not swept in).");
                _commitBtn.SetEnabled(true);
                _commitMsg.isReadOnly = false;
            }
            else
            {
                _commitBtn.tooltip = L10n.Tr("Only the latest commit (HEAD) can be amended.");
                _commitBtn.SetEnabled(false);
                _commitMsg.isReadOnly = true;
            }
        }

        // ---- Branch graph ----

        // The first slot in the "newest" direction is reserved for the HEAD
        // marker (the working-tree pseudo-node), so commits start one gap in.
        Vector2 NodePos(GitService.GraphNode n) => _horizontal
            ? new Vector2(20 + (_nodes.Count - 1 - n.Row) * NodeGap, 16 + n.Lane * LaneGap)
            : new Vector2(16 + n.Lane * LaneGap, 12 + NodeGap + n.Row * NodeGap);

        GitService.GraphNode HeadNode() => _nodes.FirstOrDefault(n => n.IsHead);

        /// <summary>Where the HEAD (working tree) marker sits: one gap past
        /// the newest commit, on HEAD's lane.</summary>
        Vector2 HeadMarkerPos(GitService.GraphNode head) => _horizontal
            ? new Vector2(20 + _nodes.Count * NodeGap, 16 + head.Lane * LaneGap)
            : new Vector2(16 + head.Lane * LaneGap, 12);

        /// <summary>Stable per-BRANCH color, keyed by the layout's segment id
        /// (golden-angle hue spacing keeps neighbors distinct), used for the
        /// commit dots and their connecting edges. Keyed by segment, not
        /// lane: lanes are reused for compactness, and lane-keyed colors
        /// made an unmerged stale branch read as one continuous line with
        /// the newer merged branches sharing its lane. Segment 0 (main's
        /// trunk) stays the first hue.</summary>
        static Color BranchColor(int segment)
        {
            float h = (segment * 0.61803399f) % 1f;
            return Color.HSVToRGB(h, 0.55f, 0.95f);
        }

        void RebuildGraph()
        {
            _orientBtn.text = _horizontal ? "⇋" : "⇅";
            _graphCanvas.Clear();
            if (_nodes.Count == 0) { _graphCanvas.MarkDirtyRepaint(); return; }
            int maxLane = _nodes.Max(n => n.Lane);
            float w = _horizontal ? 40 + (_nodes.Count + 1) * NodeGap : 24 + (maxLane + 1) * LaneGap + LabelSpace;
            float h = _horizontal ? 32 + (maxLane + 1) * LaneGap + 220 : 24 + (_nodes.Count + 1) * NodeGap;
            _graphCanvas.style.width = w;
            _graphCanvas.style.height = h;

            // ---- The HEAD marker: a pseudo-node for the WORKING TREE, one
            // slot ahead of the newest commit on HEAD's lane. Clicking it
            // returns the left pane to the working-tree changes (the
            // counterpart of clicking a commit to inspect it). ----
            var headNode = HeadNode();
            if (headNode != null)
            {
                var hp = HeadMarkerPos(headNode);
                // The HEAD marker is the ACTIVE node by default (no commit
                // selected = the working tree is what the left pane shows),
                // and clicking it re-activates it — so it wears the same
                // selection gold the commits use.
                bool headActive = _selectedHash == null;
                var gold = new Color(0.95f, 0.8f, 0.3f);
                var laneCol = headActive ? gold : BranchColor(headNode.Segment);
                bool dirty = _entries.Count > 0;
                var marker = new VisualElement
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = hp.x - Radius, top = hp.y - Radius,
                        width = Radius * 2, height = Radius * 2,
                        borderTopLeftRadius = Radius, borderTopRightRadius = Radius,
                        borderBottomLeftRadius = Radius, borderBottomRightRadius = Radius,
                        // Hollow ring: visibly "not a commit yet". A dirty
                        // tree fills it faintly; the active marker fills more.
                        backgroundColor = headActive
                            ? new Color(gold.r, gold.g, gold.b, dirty ? 0.6f : 0.35f)
                            : dirty ? new Color(laneCol.r, laneCol.g, laneCol.b, 0.35f) : Color.clear,
                        borderTopWidth = 2, borderBottomWidth = 2,
                        borderLeftWidth = 2, borderRightWidth = 2,
                        borderTopColor = laneCol, borderBottomColor = laneCol,
                        borderLeftColor = laneCol, borderRightColor = laneCol,
                    },
                    tooltip = L10n.Tr("HEAD — your working tree. Click to show the working-tree changes.")
                };
                marker.RegisterCallback<PointerDownEvent>(e =>
                {
                    _selectedHash = null;
                    LeaveInspect();
                    RebuildGraph();
                    e.StopPropagation();
                });
                _graphCanvas.Add(marker);
                var headLbl = new Label(dirty ? "HEAD *" : "HEAD")
                {
                    style =
                    {
                        position = Position.Absolute,
                        color = laneCol,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        whiteSpace = WhiteSpace.NoWrap,
                        left = _horizontal ? hp.x - 16 : 24 + (maxLane + 1) * LaneGap,
                        top = _horizontal ? hp.y - 26 : hp.y - 8,
                    },
                    tooltip = L10n.Tr("HEAD — your working tree. Click to show the working-tree changes.")
                };
                headLbl.RegisterCallback<PointerDownEvent>(e =>
                {
                    _selectedHash = null;
                    LeaveInspect();
                    RebuildGraph();
                    e.StopPropagation();
                });
                _graphCanvas.Add(headLbl);
            }

            foreach (var n in _nodes)
            {
                var node = n;
                var p = NodePos(n);
                var dot = new VisualElement
                {
                    style =
                    {
                        position = Position.Absolute,
                        left = p.x - Radius, top = p.y - Radius,
                        width = Radius * 2, height = Radius * 2,
                        borderTopLeftRadius = Radius, borderTopRightRadius = Radius,
                        borderBottomLeftRadius = Radius, borderBottomRightRadius = Radius,
                        // Dots carry their BRANCH's lane color; selection is
                        // gold, and HEAD keeps its lane color under a green
                        // ring so both facts stay visible at once.
                        backgroundColor = node.Hash == _selectedHash
                            ? new Color(0.95f, 0.8f, 0.3f)
                            : BranchColor(node.Segment),
                    },
                    tooltip = node.Hash + "  " + node.Date + "  " + node.Author + "\n" + node.Subject
                };
                if (node.IsHead)
                {
                    var ring = new Color(0.5f, 0.9f, 0.55f);
                    dot.style.borderTopWidth = 2; dot.style.borderBottomWidth = 2;
                    dot.style.borderLeftWidth = 2; dot.style.borderRightWidth = 2;
                    dot.style.borderTopColor = ring; dot.style.borderBottomColor = ring;
                    dot.style.borderLeftColor = ring; dot.style.borderRightColor = ring;
                }
                dot.RegisterCallback<PointerDownEvent>(e =>
                {
                    _selectedHash = node.Hash;
                    _status.text = node.Hash + "  " + node.Date + "  " + node.Author + "  —  " + node.Subject;
                    RebuildGraph();
                    InspectCommit(node.Hash);
                    if (e.button == 1 || e.clickCount >= 2) NodeMenu(node);
                    e.StopPropagation();
                });
                _graphCanvas.Add(dot);

                // Vertical layout gets inline commit labels; horizontal keeps
                // the row compact (tooltips + selection line carry the detail).
                if (!_horizontal)
                {
                    string refs = node.Refs.Count > 0 ? "[" + string.Join(", ", node.Refs) + "] " : "";
                    string subj = node.Subject.Length > 46 ? node.Subject.Substring(0, 43) + "..." : node.Subject;
                    var lbl = new Label(node.Hash + "  " + refs + subj)
                    {
                        style =
                        {
                            position = Position.Absolute,
                            left = 24 + (maxLane + 1) * LaneGap,
                            top = p.y - 8,
                            whiteSpace = WhiteSpace.NoWrap,
                            color = node.Refs.Count > 0 ? new Color(0.55f, 0.85f, 0.6f) : new Color(0.8f, 0.8f, 0.8f)
                        }
                    };
                    lbl.RegisterCallback<PointerDownEvent>(e =>
                    {
                        _selectedHash = node.Hash;
                        RebuildGraph();
                        InspectCommit(node.Hash);
                        if (e.button == 1 || e.clickCount >= 2) NodeMenu(node);
                        e.StopPropagation();
                    });
                    _graphCanvas.Add(lbl);
                }
                else if (node.Refs.Count > 0)
                {
                    var lbl = new Label(string.Join(",", node.Refs))
                    {
                        style = { position = Position.Absolute, left = p.x - 12, top = 16 + (maxLane + 1) * LaneGap + 4,
                                  rotate = new Rotate(45), color = new Color(0.55f, 0.85f, 0.6f), whiteSpace = WhiteSpace.NoWrap }
                    };
                    _graphCanvas.Add(lbl);
                }
            }
            _graphCanvas.MarkDirtyRepaint();
            CenterGraphVertically();
        }

        /// <summary>Horizontal layout: pads the canvas's top so the lane
        /// band (plus a modest ref-label allowance — NOT the canvas's full
        /// label slack, which would bias the band upward) sits vertically
        /// centered in the viewport. Vertical layout resets the pad.</summary>
        void CenterGraphVertically()
        {
            if (_graphCanvas == null || _graphScroll == null) return;
            float margin = 0;
            if (_horizontal && _nodes.Count > 0)
            {
                int maxLane = _nodes.Max(n => n.Lane);
                float visualH = 32 + (maxLane + 1) * LaneGap + 40;
                float viewH = _graphScroll.contentViewport.layout.height;
                if (!float.IsNaN(viewH) && viewH > visualH) margin = (viewH - visualH) * 0.5f;
            }
            _graphCanvas.style.marginTop = margin;
        }

        void DrawGraphEdges(MeshGenerationContext ctx)
        {
            if (_nodes.Count == 0) return;
            var byHash = _nodes.ToDictionary(n => n.Hash, n => n);
            var p = ctx.painter2D;
            p.lineWidth = 1.5f;
            // The HEAD (working tree) marker hangs off the HEAD commit.
            var head = HeadNode();
            if (head != null)
            {
                var col = BranchColor(head.Segment);
                p.strokeColor = new Color(col.r, col.g, col.b, 0.55f);
                p.BeginPath();
                p.MoveTo(HeadMarkerPos(head));
                p.LineTo(NodePos(head));
                p.Stroke();
            }
            foreach (var n in _nodes)
                foreach (var parent in n.Parents)
                {
                    if (!byHash.TryGetValue(parent, out var pn)) continue;
                    // The edge takes the color of the BRANCH it carries: for
                    // same-lane edges both ends agree; for forks and merges
                    // the branch is the node on the OUTER lane of the pair.
                    var col = BranchColor((n.Lane >= pn.Lane ? n : pn).Segment);
                    p.strokeColor = new Color(col.r, col.g, col.b, 0.85f);
                    p.BeginPath();
                    p.MoveTo(NodePos(n));
                    p.LineTo(NodePos(pn));
                    p.Stroke();
                }
        }

        void NodeMenu(GitService.GraphNode node)
        {
            var m = new GenericMenu();
            bool dirty = _entries.Count > 0;
            foreach (var r in node.Refs.Where(r => !r.StartsWith("tag:")))
            {
                string branch = r;
                if (branch.StartsWith("origin/")) continue; // remotes: not directly checkout-able here
                if (dirty)
                    m.AddDisabledItem(new GUIContent(string.Format(L10n.Tr("Checkout {0} (working tree not clean)"), branch)));
                else
                    m.AddItem(new GUIContent(string.Format(L10n.Tr("Checkout {0}"), branch)), false,
                        () => RunGit("checkout \"" + branch + "\""));
            }
            m.AddItem(new GUIContent(L10n.Tr("Create Branch Here...")), false, () =>
            {
                string name = "branch-" + node.Hash;
                RunGit("branch \"" + name + "\" " + node.Hash,
                    () => _status.text = string.Format(L10n.Tr("Created branch {0}."), name));
            });
            if (dirty)
                m.AddDisabledItem(new GUIContent(L10n.Tr("Checkout Commit (working tree not clean)")));
            else
                m.AddItem(new GUIContent(L10n.Tr("Checkout Commit (detached)")), false,
                    () => RunGit("checkout " + node.Hash));
            m.AddItem(new GUIContent(L10n.Tr("Copy Hash")), false,
                () => EditorGUIUtility.systemCopyBuffer = node.Hash);
            m.ShowAsContext();
        }
    }
}
#endif
