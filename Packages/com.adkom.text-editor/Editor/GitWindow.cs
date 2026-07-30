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

        TextEditorWindow _owner;
        System.Threading.SynchronizationContext _ctx;
        string _root;

        Label _header, _status;
        ScrollView _changes;
        TextField _commitMsg;
        Button _commitBtn, _pushBtn, _orientBtn;
        ScrollView _graphScroll;
        VisualElement _graphCanvas;
        bool _horizontal;

        List<GitService.StatusEntry> _entries = new List<GitService.StatusEntry>();
        List<GitService.GraphNode> _nodes = new List<GitService.GraphNode>();
        string _selectedHash;
        bool _busy;

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
            _instance.BuildUI();
            _instance.Refresh();
            _instance.Focus();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = 6;

            _header = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold } };
            root.Add(_header);
            _status = new Label { style = { opacity = 0.8f, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(_status);

            var split = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

            // --- Left: changes + commit ---
            var left = new VisualElement { style = { width = 300, flexShrink = 0, flexDirection = FlexDirection.Column,
                borderRightWidth = 1, borderRightColor = new Color(0.5f, 0.5f, 0.5f, 0.4f), paddingRight = 6 } };
            left.Add(new Label(L10n.Tr("Changes")) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _changes = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            left.Add(_changes);
            left.Add(new Label(L10n.Tr("Commit message")) { style = { marginTop = 4 } });
            _commitMsg = new TextField { multiline = true, style = { height = 56 } };
            left.Add(_commitMsg);
            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 4 } };
            _commitBtn = new Button(Commit) { text = L10n.Tr("Commit") };
            _pushBtn = new Button(Push) { text = L10n.Tr("Push") };
            btnRow.Add(_commitBtn);
            btnRow.Add(_pushBtn);
            left.Add(btnRow);
            split.Add(left);

            // --- Right: branch graph ---
            var right = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column, paddingLeft = 6 } };
            var graphBar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            graphBar.Add(new Label(L10n.Tr("Branch History")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8 } });
            _orientBtn = new Button(() => { _horizontal = !_horizontal; RebuildGraph(); })
            { text = _horizontal ? "⇋" : "⇅", tooltip = L10n.Tr("Toggle vertical / horizontal layout") };
            graphBar.Add(_orientBtn);
            var refresh = new Button(Refresh) { text = L10n.Tr("Refresh") };
            graphBar.Add(refresh);
            right.Add(graphBar);
            _graphScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1 } };
            _graphCanvas = new VisualElement { style = { position = Position.Relative, flexShrink = 0 } };
            _graphCanvas.generateVisualContent += DrawGraphEdges;
            _graphScroll.Add(_graphCanvas);
            right.Add(_graphScroll);
            split.Add(right);

            root.Add(split);
        }

        void Refresh()
        {
            if (_busy || _root == null) return;
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
                    RebuildChanges();
                    RebuildGraph();
                }, null);
            });
        }

        // ---- Changes / stage / commit / push ----

        void RebuildChanges()
        {
            _changes.Clear();
            foreach (var e in _entries)
            {
                var entry = e;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                bool staged = entry.Index != ' ' && entry.Index != '?';
                var t = new Toggle { value = staged, tooltip = L10n.Tr("Staged for commit") };
                t.RegisterValueChangedCallback(ev => RunGit(ev.newValue
                    ? "add -- \"" + entry.Path + "\""
                    : "restore --staged -- \"" + entry.Path + "\""));
                row.Add(t);
                string state = ("" + entry.Index + entry.Work).Trim();
                row.Add(new Label(state) { style = { width = 24, opacity = 0.7f } });
                var lbl = new Label(entry.Path) { style = { overflow = Overflow.Hidden, whiteSpace = WhiteSpace.NoWrap } };
                lbl.tooltip = entry.Path;
                lbl.RegisterCallback<PointerDownEvent>(ev =>
                {
                    if (ev.clickCount >= 2)
                        TextEditorWindow.OpenExternal(System.IO.Path.Combine(_root, entry.Path), 1, 1);
                });
                row.Add(lbl);
                _changes.Add(row);
            }
        }

        void RunGit(string args, System.Action after = null)
        {
            if (_busy || _root == null) return;
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
                        AteConsole.Warn("[ADKOM Text Editor] git " + args + " failed: " + msg);
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
            RunGit("commit -m \"" + msg.Replace("\"", "\\\"") + "\"", () => _commitMsg.value = "");
        }

        void Push() => RunGit("push");

        // ---- Branch graph ----

        Vector2 NodePos(GitService.GraphNode n) => _horizontal
            ? new Vector2(20 + (_nodes.Count - 1 - n.Row) * NodeGap, 16 + n.Lane * LaneGap)
            : new Vector2(16 + n.Lane * LaneGap, 12 + n.Row * NodeGap);

        void RebuildGraph()
        {
            _orientBtn.text = _horizontal ? "⇋" : "⇅";
            _graphCanvas.Clear();
            if (_nodes.Count == 0) { _graphCanvas.MarkDirtyRepaint(); return; }
            int maxLane = _nodes.Max(n => n.Lane);
            float w = _horizontal ? 40 + _nodes.Count * NodeGap : 24 + (maxLane + 1) * LaneGap + LabelSpace;
            float h = _horizontal ? 32 + (maxLane + 1) * LaneGap + 220 : 24 + _nodes.Count * NodeGap;
            _graphCanvas.style.width = w;
            _graphCanvas.style.height = h;

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
                        backgroundColor = node.Hash == _selectedHash
                            ? new Color(0.95f, 0.8f, 0.3f)
                            : node.IsHead ? new Color(0.5f, 0.9f, 0.55f)
                            : new Color(0.55f, 0.7f, 0.95f),
                    },
                    tooltip = node.Hash + "  " + node.Date + "  " + node.Author + "\n" + node.Subject
                };
                dot.RegisterCallback<PointerDownEvent>(e =>
                {
                    _selectedHash = node.Hash;
                    _status.text = node.Hash + "  " + node.Date + "  " + node.Author + "  —  " + node.Subject;
                    RebuildGraph();
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
        }

        void DrawGraphEdges(MeshGenerationContext ctx)
        {
            if (_nodes.Count == 0) return;
            var byHash = _nodes.ToDictionary(n => n.Hash, n => n);
            var p = ctx.painter2D;
            p.lineWidth = 1.5f;
            p.strokeColor = new Color(0.55f, 0.55f, 0.6f, 0.9f);
            foreach (var n in _nodes)
                foreach (var parent in n.Parents)
                {
                    if (!byHash.TryGetValue(parent, out var pn)) continue;
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
