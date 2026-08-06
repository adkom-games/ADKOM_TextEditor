#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements; // PopupField on older UIToolkit versions
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// The Diff / Merge window: two-way side-by-side diffs of files, folders
    /// or open tabs, and a three-way merge (left / base / right) with
    /// per-conflict resolution and a saveable result. Multiple windows may
    /// be open at once; every window restores its comparison — including
    /// in-progress merge choices and the edited result — after a domain
    /// reload.
    /// </summary>
    public class AteDiffWindow : EditorWindow
    {
        internal enum Mode { Files = 0, Folders = 1, Tabs = 2 }

        /// <summary>One comparison input. Path-backed sides re-read their
        /// file on rebuild; text-backed sides (git revisions, tab snapshots)
        /// carry the content itself so reloads cannot lose it.</summary>
        [Serializable]
        internal class DiffSide
        {
            public string Path = "";
            public string TabName = "";
            public string Text = "";
            public bool UseText;
            public string Label = "";
            public bool Dirty;   // merge buttons edited this side; Save clears

            public string DisplayLabel => !string.IsNullOrEmpty(Label) ? Label
                : !string.IsNullOrEmpty(TabName) ? TabName
                : !string.IsNullOrEmpty(Path) ? System.IO.Path.GetFileName(Path) : "?";

            /// <summary>Content of this side, or null with an error string.</summary>
            public string Resolve(out string error)
            {
                error = null;
                if (!string.IsNullOrEmpty(TabName))
                {
                    var w = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
                    string live = w.Length > 0 ? w[0].DiffableDocText(TabName) : null;
                    if (live != null) return live;
                    if (UseText) return Text ?? "";   // tab gone — snapshot from when the diff was set up
                    error = string.Format(L10n.Tr("Tab \"{0}\" is no longer open."), TabName);
                    return null;
                }
                if (UseText) return Text ?? "";
                if (string.IsNullOrEmpty(Path)) { error = L10n.Tr("No path chosen."); return null; }
                try { return System.IO.File.ReadAllText(Path); }
                catch (Exception ex) { error = ex.Message; return null; }
            }
        }

        [SerializeField] int _mode;
        [SerializeField] DiffSide _a = new DiffSide();
        [SerializeField] DiffSide _b = new DiffSide();
        [SerializeField] DiffSide _base = new DiffSide();
        [SerializeField] bool _threeWay;
        [SerializeField] string _outPath = "";
        [SerializeField] bool _inSetup = true;
        [SerializeField] int[] _choices = new int[0];   // per conflict: 0 open, 1 left, 2 right, 3 base, 4 both
        [SerializeField] string _resultText = "";
        [SerializeField] bool _fromUnityMerge;           // Save must write _outPath for Unity's VCS flow
        [SerializeField] float _splitFrac = 0.5f;        // two-way column split (draggable)

        // UI (rebuilt every reload; guards must reset with the domain)
        [NonSerialized] VisualElement _header;
        [NonSerialized] VisualElement _content;
        [NonSerialized] Label _statusLbl;
        [NonSerialized] ListView _list;
        [NonSerialized] List<Row> _rows;
        [NonSerialized] List<int> _changeRows;
        [NonSerialized] int _navIndex = -1;
        [NonSerialized] List<DiffEngine.MergeChunk> _chunks;
        [NonSerialized] TextField _resultField;
        [NonSerialized] Label _conflictLbl;
        [NonSerialized] List<VisualElement> _conflictPanels;
        [NonSerialized] bool _suppressResultDirty;
        [NonSerialized] string[] _linesA, _linesB;
        [NonSerialized] Label _headA, _headB;
        [NonSerialized] Button _saveLeftBtn, _saveRightBtn;
        [NonSerialized] VisualElement _frameBox;

        const float RowH = 18f;
        const float GutterW = 42f;

        static readonly Color AddedBg = new Color(0.25f, 0.62f, 0.25f, 0.16f);
        static readonly Color RemovedBg = new Color(0.78f, 0.25f, 0.25f, 0.16f);
        static readonly Color ChangedBg = new Color(0.78f, 0.65f, 0.20f, 0.13f);
        static readonly Color VoidBg = new Color(0.5f, 0.5f, 0.5f, 0.06f);
        const string MarkA = "#B0404066";  // intra-line, removed side
        const string MarkB = "#3FA53F66";  // intra-line, added side

        // ---------- Entry points ----------

        static AteDiffWindow Create(string title)
        {
            var w = CreateInstance<AteDiffWindow>();
            w.titleContent = new GUIContent(title);
            w.minSize = new Vector2(720, 360);
            w.Show();
            return w;
        }

        /// <summary>Tools menu: opens the setup pane.</summary>
        public static void OpenSetup()
        {
            var w = Create(L10n.Tr("Diff / Merge"));
            w._inSetup = true;
            w.BuildUI();
        }

        public static void OpenFiles(string pathA, string pathB)
        {
            var w = Create(L10n.Tr("Diff"));
            w._mode = (int)Mode.Files;
            w._a = new DiffSide { Path = pathA };
            w._b = new DiffSide { Path = pathB };
            w._inSetup = false;
            w.BuildUI();
        }

        public static void OpenFolders(string dirA, string dirB)
        {
            var w = Create(L10n.Tr("Folder Diff"));
            w._mode = (int)Mode.Folders;
            w._a = new DiffSide { Path = dirA };
            w._b = new DiffSide { Path = dirB };
            w._inSetup = false;
            w.BuildUI();
        }

        /// <summary>Literal contents (git revisions and similar).</summary>
        public static void OpenTexts(string labelA, string textA, string labelB, string textB)
        {
            var w = Create(L10n.Tr("Diff"));
            w._mode = (int)Mode.Files;
            w._a = new DiffSide { UseText = true, Text = textA ?? "", Label = labelA };
            w._b = new DiffSide { UseText = true, Text = textB ?? "", Label = labelB };
            w._inSetup = false;
            w.BuildUI();
        }

        /// <summary>Text vs file on disk (git working-tree diffs).</summary>
        public static void OpenTextVsFile(string labelA, string textA, string pathB, string labelB)
        {
            var w = Create(L10n.Tr("Diff"));
            w._mode = (int)Mode.Files;
            w._a = new DiffSide { UseText = true, Text = textA ?? "", Label = labelA };
            w._b = new DiffSide { Path = pathB, Label = labelB };
            w._inSetup = false;
            w.BuildUI();
        }

        /// <summary>Three-way merge; output lands in outPath on Save. Used
        /// by Unity's version-control merge flow and the setup pane.</summary>
        public static void OpenMerge(string leftPath, string rightPath, string basePath, string outPath, bool fromUnity)
        {
            var w = Create(L10n.Tr("Merge"));
            w._mode = (int)Mode.Files;
            w._threeWay = true;
            w._a = new DiffSide { Path = leftPath };
            w._b = new DiffSide { Path = rightPath };
            w._base = new DiffSide { Path = basePath };
            w._outPath = outPath ?? "";
            w._fromUnityMerge = fromUnity;
            w._inSetup = false;
            w.BuildUI();
        }

        /// <summary>Reload survivor: rebuild the whole view — comparison,
        /// merge choices and result included — from serialized state.</summary>
        void CreateGUI()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                if (_header != null) return; // fresh open builds directly
                BuildUI();
            });
        }

        // ---------- Frame ----------

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 6;
            root.style.paddingTop = 4; root.style.paddingBottom = 6;

            _header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0, marginBottom = 3 } };
            root.Add(_header);
            _content = new VisualElement { style = { flexGrow = 1 } };
            root.Add(_content);
            _statusLbl = new Label { style = { opacity = 0.8f, flexShrink = 0, marginTop = 2, whiteSpace = WhiteSpace.Normal } };
            root.Add(_statusLbl);

            if (_inSetup) BuildSetup();
            else RunCompare();
            AteTooltip.Attach(rootVisualElement);
        }

        Button HeaderBtn(string text, string tip, Action act)
        {
            var b = new Button(act) { text = text, tooltip = tip };
            b.style.marginRight = 4;
            _header.Add(b);
            return b;
        }

        void Status(string msg) { if (_statusLbl != null) _statusLbl.text = msg; }

        // ---------- Setup pane ----------

        void BuildSetup()
        {
            _header.Clear(); _content.Clear();
            titleContent = new GUIContent(L10n.Tr("Diff / Merge"));
            var modeLbl = new Label(L10n.Tr("Compare:")) { style = { marginRight = 6 }, tooltip = L10n.Tr("What kind of items to compare.") };
            _header.Add(modeLbl);
            var modes = new[] { L10n.Tr("Files"), L10n.Tr("Folders"), L10n.Tr("Tabs") };
            var modeTips = new[]
            {
                L10n.Tr("Compare two files on disk (optionally three-way with a base and merge)."),
                L10n.Tr("Compare two folders recursively; open any differing file pair as a diff."),
                L10n.Tr("Compare two open editor tabs.")
            };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = new Button(() => { _mode = idx; BuildSetup(); }) { text = modes[i], tooltip = modeTips[i] };
                if (_mode == idx) b.style.backgroundColor = new Color(0.3f, 0.5f, 0.75f, 0.5f);
                b.style.marginRight = 2;
                _header.Add(b);
            }

            var box = new VisualElement { style = { marginTop = 8, maxWidth = 700 } };
            _content.Add(box);

            if (_mode == (int)Mode.Tabs)
            {
                var owner = Resources.FindObjectsOfTypeAll<TextEditorWindow>().FirstOrDefault();
                var names = owner != null ? owner.DiffableDocNames() : new List<string>();
                if (names.Count < 2)
                {
                    box.Add(new Label(L10n.Tr("Open at least two documents in the text editor to compare tabs.")) { style = { opacity = 0.7f } });
                }
                else
                {
                    var pa = TabRow(box, L10n.Tr("Left tab"), names, _a.TabName, v => _a.TabName = v);
                    var pb = TabRow(box, L10n.Tr("Right tab"), names, _b.TabName, v => _b.TabName = v);
                    if (string.IsNullOrEmpty(_a.TabName)) _a.TabName = names[0];
                    if (string.IsNullOrEmpty(_b.TabName)) _b.TabName = names[Math.Min(1, names.Count - 1)];
                    pa.SetValueWithoutNotify(_a.TabName);
                    pb.SetValueWithoutNotify(_b.TabName);
                }
            }
            else
            {
                bool folders = _mode == (int)Mode.Folders;
                PathRow(box, folders ? L10n.Tr("Left folder") : L10n.Tr("Left file"), _a, folders, false);
                PathRow(box, folders ? L10n.Tr("Right folder") : L10n.Tr("Right file"), _b, folders, false);
                if (!folders)
                {
                    var three = new Toggle(L10n.Tr("Three-way (merge with a common base)")) { value = _threeWay, tooltip = L10n.Tr("Adds a base version: changes from both sides are merged; diverging changes become conflicts you resolve.") };
                    three.RegisterValueChangedCallback(e => { _threeWay = e.newValue; BuildSetup(); });
                    three.style.marginTop = 6;
                    box.Add(three);
                    if (_threeWay)
                    {
                        PathRow(box, L10n.Tr("Base file"), _base, false, false);
                        var outRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
                        outRow.Add(new Label(L10n.Tr("Output")) { style = { width = 90 }, tooltip = L10n.Tr("Where Save writes the merged result.") });
                        var tf = new TextField { value = _outPath, style = { flexGrow = 1 }, tooltip = L10n.Tr("Where Save writes the merged result.") };
                        tf.RegisterValueChangedCallback(e => _outPath = e.newValue);
                        outRow.Add(tf);
                        var br = new Button(() =>
                        {
                            string p = EditorUtility.SaveFilePanel(L10n.Tr("Merge output"), "", "merged.txt", "");
                            if (!string.IsNullOrEmpty(p)) { _outPath = p; tf.value = p; }
                        }) { text = "…", tooltip = L10n.Tr("Choose the output file.") };
                        outRow.Add(br);
                        box.Add(outRow);
                    }
                }
            }

            var go = new Button(() =>
            {
                _inSetup = false;
                if (_mode == (int)Mode.Tabs) { _a.UseText = false; _b.UseText = false; }
                RunCompare();
            })
            { text = _threeWay && _mode == (int)Mode.Files ? L10n.Tr("Merge") : L10n.Tr("Compare"), tooltip = L10n.Tr("Run the comparison.") };
            go.style.marginTop = 10; go.style.width = 120;
            box.Add(go);
        }

        PopupField<string> TabRow(VisualElement box, string label, List<string> names, string current, Action<string> set)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
            row.Add(new Label(label) { style = { width = 90 }, tooltip = L10n.Tr("An open document tab.") });
            int idx = Math.Max(0, names.IndexOf(current));
            var pop = new PopupField<string>(names, idx) { style = { flexGrow = 1 }, tooltip = L10n.Tr("An open document tab.") };
            pop.RegisterValueChangedCallback(e => set(e.newValue));
            set(pop.value);
            row.Add(pop);
            box.Add(row);
            return pop;
        }

        void PathRow(VisualElement box, string label, DiffSide side, bool folder, bool save)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
            string tip = folder ? L10n.Tr("A folder to compare recursively.") : L10n.Tr("A file to compare.");
            row.Add(new Label(label) { style = { width = 90 }, tooltip = tip });
            var tf = new TextField { value = side.Path, style = { flexGrow = 1 }, tooltip = tip };
            tf.RegisterValueChangedCallback(e => { side.Path = e.newValue; side.UseText = false; });
            row.Add(tf);
            var b = new Button(() =>
            {
                string p = folder
                    ? EditorUtility.OpenFolderPanel(label, side.Path, "")
                    : EditorUtility.OpenFilePanel(label, System.IO.Path.GetDirectoryName(string.IsNullOrEmpty(side.Path) ? Application.dataPath : side.Path), "");
                if (!string.IsNullOrEmpty(p)) { side.Path = p; side.UseText = false; tf.value = p; }
            }) { text = "…", tooltip = folder ? L10n.Tr("Choose a folder.") : L10n.Tr("Choose a file.") };
            row.Add(b);
            box.Add(row);
        }

        // ---------- Dispatch ----------

        void RunCompare()
        {
            _header.Clear(); _content.Clear(); Status("");
            HeaderBtn(L10n.Tr("New Comparison…"), L10n.Tr("Back to the setup pane to pick different items."), () => { _inSetup = true; _threeWay = _threeWay && _mode == (int)Mode.Files; BuildSetup(); });
            if (_mode == (int)Mode.Folders) { BuildFolderDiff(); return; }
            if (_threeWay) { BuildMerge(); return; }
            BuildFileDiff();
        }

        // ---------- Two-way file/tab diff ----------

        sealed class Row
        {
            public int ALine = -1, BLine = -1;      // 1-based; -1 = void half
            public string AText = "", BText = "";
            public DiffEngine.Op Op;
            public bool ChangeStart;
            public int IntraStart = -1, IntraLenA, IntraLenB;
            public DiffEngine.Block Block;          // the change region (null for Equal)
        }

        void BuildFileDiff()
        {
            string ea, eb;
            string ta = _a.Resolve(out ea);
            string tb = _b.Resolve(out eb);
            if (ta == null || tb == null) { Status((ea ?? "") + (ea != null && eb != null ? "  |  " : "") + (eb ?? "")); return; }
            titleContent = new GUIContent(L10n.Tr("Diff") + ": " + _a.DisplayLabel);

            var la = DiffEngine.SplitLines(ta);
            var lb = DiffEngine.SplitLines(tb);
            _linesA = la; _linesB = lb;
            var blocks = DiffEngine.DiffLines(la, lb);
            _rows = new List<Row>();
            _changeRows = new List<int>();
            int add = 0, del = 0, chg = 0;
            foreach (var blk in blocks)
            {
                bool first = true;
                switch (blk.Op)
                {
                    case DiffEngine.Op.Equal:
                        for (int i = 0; i < blk.ACount; i++)
                            _rows.Add(new Row { ALine = blk.AStart + i + 1, BLine = blk.BStart + i + 1, AText = la[blk.AStart + i], BText = lb[blk.BStart + i], Op = DiffEngine.Op.Equal });
                        break;
                    case DiffEngine.Op.Delete:
                        del += blk.ACount;
                        for (int i = 0; i < blk.ACount; i++)
                        { MarkChange(first); first = false;
                          _rows.Add(new Row { ALine = blk.AStart + i + 1, AText = la[blk.AStart + i], Op = DiffEngine.Op.Delete, ChangeStart = i == 0, Block = blk }); }
                        break;
                    case DiffEngine.Op.Insert:
                        add += blk.BCount;
                        for (int i = 0; i < blk.BCount; i++)
                        { MarkChange(first); first = false;
                          _rows.Add(new Row { BLine = blk.BStart + i + 1, BText = lb[blk.BStart + i], Op = DiffEngine.Op.Insert, ChangeStart = i == 0, Block = blk }); }
                        break;
                    case DiffEngine.Op.Replace:
                        chg += Math.Max(blk.ACount, blk.BCount);
                        int n = Math.Max(blk.ACount, blk.BCount);
                        for (int i = 0; i < n; i++)
                        {
                            MarkChange(first); first = false;
                            var r = new Row { Op = DiffEngine.Op.Replace, ChangeStart = i == 0, Block = blk };
                            if (i < blk.ACount) { r.ALine = blk.AStart + i + 1; r.AText = la[blk.AStart + i]; } else r.ALine = -1;
                            if (i < blk.BCount) { r.BLine = blk.BStart + i + 1; r.BText = lb[blk.BStart + i]; } else r.BLine = -1;
                            if (i < blk.ACount && i < blk.BCount)
                            {
                                DiffEngine.IntraLine(r.AText, r.BText, out int s, out int lenA, out int lenB);
                                r.IntraStart = s; r.IntraLenA = lenA; r.IntraLenB = lenB;
                            }
                            _rows.Add(r);
                        }
                        break;
                }
            }
            void MarkChange(bool first) { if (first) _changeRows.Add(_rows.Count); }

            // Header: navigation, whole-side merges, saves, stats.
            HeaderBtn("⇄", L10n.Tr("Swap the two sides."), () => { var t = _a; _a = _b; _b = t; RunCompare(); });
            HeaderBtn("▲", L10n.Tr("Go to the previous change."), () => NavChange(-1));
            HeaderBtn("▼", L10n.Tr("Go to the next change."), () => NavChange(1));
            HeaderBtn("◀◀", L10n.Tr("Merge every change into the left side (it becomes a copy of the right)."),
                () => { MarkSideEdited(_a, tb); RunCompare(); });
            HeaderBtn("▶▶", L10n.Tr("Merge every change into the right side (it becomes a copy of the left)."),
                () => { MarkSideEdited(_b, ta); RunCompare(); });
            _saveLeftBtn = HeaderBtn(L10n.Tr("Save Left"), L10n.Tr("Save the modified left side to its file."), () => SaveSide(_a));
            _saveRightBtn = HeaderBtn(L10n.Tr("Save Right"), L10n.Tr("Save the modified right side to its file."), () => SaveSide(_b));
            _saveLeftBtn.SetEnabled(_a.Dirty);
            _saveRightBtn.SetEnabled(_b.Dirty);
            var stats = new Label(string.Format(L10n.Tr("+{0}  −{1}  ~{2}"), add, del, chg))
            { tooltip = L10n.Tr("Added, removed and changed line counts."), style = { marginLeft = 6, opacity = 0.85f } };
            _header.Add(stats);

            // Framed column area: titles with a divider running through the
            // center gutter, then the aligned row list.
            _frameBox = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            FrameBorder(_frameBox);
            _content.Add(_frameBox);

            var cols = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            // Edge-to-edge line under the titles.
            cols.style.borderBottomWidth = 1;
            cols.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            _headA = SideHeader((_a.Dirty ? "*" : "") + _a.DisplayLabel);
            _headB = SideHeader((_b.Dirty ? "*" : "") + _b.DisplayLabel);
            _headA.style.flexGrow = _splitFrac;
            _headB.style.flexGrow = 1f - _splitFrac;
            var gutterHead = MakeGutter();
            gutterHead.tooltip = L10n.Tr("Drag to resize the columns.");
            MakeSplitterDraggable(gutterHead);
            cols.Add(_headA); cols.Add(gutterHead); cols.Add(_headB);
            _frameBox.Add(cols);

            _list = new ListView(_rows, RowH, MakeRow, BindRow) { style = { flexGrow = 1 } };
            _list.selectionType = SelectionType.None;
            _list.RegisterCallback<MouseUpEvent>(OnDiffContextMenu);
            _frameBox.Add(_list);
            SyncHeaderToScrollbar(cols, _list);
            if (_changeRows.Count == 0) Status(L10n.Tr("No differences."));
            else Status(string.Format(L10n.Tr("{0} change region(s)."), _changeRows.Count));
        }

        /// <summary>Context menu on the side-by-side diff rows: copy either
        /// side's (possibly merged) text, and each file-backed side gets its
        /// own Git submenu — WITHOUT Diff/Merge, which would recurse.</summary>
        void OnDiffContextMenu(MouseUpEvent e)
        {
            if (e.button != 1) return;
            e.StopPropagation();
            var m = new GenericMenu();
            if (_linesA != null)
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Copy Left Side ({0})"), _a.DisplayLabel)),
                    false, () => EditorGUIUtility.systemCopyBuffer = string.Join("\n", _linesA));
            if (_linesB != null)
                m.AddItem(new GUIContent(string.Format(L10n.Tr("Copy Right Side ({0})"), _b.DisplayLabel)),
                    false, () => EditorGUIUtility.systemCopyBuffer = string.Join("\n", _linesB));
            AddSideGitMenu(m, _a);
            AddSideGitMenu(m, _b);
            m.DropDown(new Rect(e.mousePosition, Vector2.zero));
        }

        void AddSideGitMenu(GenericMenu m, DiffSide side)
        {
            if (side == null || string.IsNullOrEmpty(side.Path) || !System.IO.File.Exists(side.Path)) return;
            var owners = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            if (owners.Length == 0) return;
            var owner = owners[0];
            string path = side.Path, name = side.DisplayLabel;
            string root = string.Format(L10n.Tr("Git ({0})"), name) + "/";
            m.AddItem(new GUIContent(root + L10n.Tr("Blame Current File")), false,
                () => owner.GitBlameFor(path, name));
            m.AddItem(new GUIContent(root + L10n.Tr("File History...")), false,
                () => owner.GitFileHistoryFor(path, name));
            m.AddItem(new GUIContent(root + L10n.Tr("Time Lapse Current File...")), false,
                () => owner.GitTimeLapseFor(path, name));
        }

        // ---- Column framing / splitter ----

        static void FrameBorder(VisualElement v)
        {
            var bc = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            v.style.borderTopWidth = 1; v.style.borderBottomWidth = 1;
            v.style.borderLeftWidth = 1; v.style.borderRightWidth = 1;
            SetBorder(v, bc);
            v.style.borderTopLeftRadius = 3; v.style.borderTopRightRadius = 3;
            v.style.borderBottomLeftRadius = 3; v.style.borderBottomRightRadius = 3;
            v.style.paddingLeft = 2; v.style.paddingRight = 2;
            v.style.paddingTop = 2; v.style.paddingBottom = 2;
        }

        /// <summary>Center gutter element: fixed width, vertical divider
        /// lines on both edges so the two columns read as framed panes.</summary>
        static VisualElement MakeGutter()
        {
            var g = new VisualElement { style = { width = GutterW, flexShrink = 0, flexDirection = FlexDirection.Row, justifyContent = Justify.Center, alignItems = Align.Center } };
            var bc = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            g.style.borderLeftWidth = 1; g.style.borderRightWidth = 1;
            g.style.borderLeftColor = bc; g.style.borderRightColor = bc;
            return g;
        }

        /// <summary>Keeps a header row aligned with the list rows below it:
        /// the list's vertical scrollbar makes rows narrower than the
        /// header, shifting the center gutters apart — pad the header's
        /// right edge by the actual scrollbar width whenever layout moves.</summary>
        static void SyncHeaderToScrollbar(VisualElement headerRow, ListView list)
        {
            void Sync()
            {
                var sv = list.Q<ScrollView>();
                if (sv == null) return;
                float pad = Mathf.Max(0f, list.worldBound.width - sv.contentViewport.worldBound.width);
                if (Mathf.Abs(headerRow.resolvedStyle.paddingRight - pad) > 0.5f)
                    headerRow.style.paddingRight = pad;
            }
            list.RegisterCallback<GeometryChangedEvent>(_ => Sync());
            // The scrollbar appearing resizes the VIEWPORT, not the list —
            // watch it too (it exists only after the list attaches).
            list.schedule.Execute(() =>
            {
                var sv = list.Q<ScrollView>();
                if (sv != null)
                    sv.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Sync());
                Sync();
            });
        }

        void MakeSplitterDraggable(VisualElement handle)
        {
            bool dragging = false;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target != handle) return; // clicks on the merge buttons are not drags
                dragging = true; handle.CapturePointer(e.pointerId); e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || _frameBox == null) return;
                var box = _frameBox.worldBound;
                float frac = (e.position.x - box.x - GutterW * 0.5f) / Mathf.Max(1f, box.width - GutterW);
                _splitFrac = Mathf.Clamp(frac, 0.15f, 0.85f);
                ApplySplit();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            { dragging = false; if (handle.HasPointerCapture(e.pointerId)) handle.ReleasePointer(e.pointerId); });
        }

        void ApplySplit()
        {
            if (_headA != null) { _headA.style.flexGrow = _splitFrac; _headB.style.flexGrow = 1f - _splitFrac; }
            if (_list == null) return;
            _list.Query<VisualElement>("ha").ForEach(v => v.style.flexGrow = _splitFrac);
            _list.Query<VisualElement>("hb").ForEach(v => v.style.flexGrow = 1f - _splitFrac);
        }

        // ---- Region merge operations ----

        /// <summary>Marks a side as edited in memory: content moves into the
        /// serialized text buffer (tab sides detach from their tab, keeping
        /// the name as a label); the Path remains the Save target.</summary>
        void MarkSideEdited(DiffSide side, string text)
        {
            if (!string.IsNullOrEmpty(side.TabName)) { side.Label = side.DisplayLabel; side.TabName = ""; }
            side.Text = text;
            side.UseText = true;
            side.Dirty = true;
        }

        /// <summary>Copies one change region across: toLeft replaces the
        /// region's left lines with the right side's, and vice versa. The
        /// view re-diffs and stays scrolled near the region.</summary>
        void ApplyRegion(DiffEngine.Block blk, bool toLeft)
        {
            if (_linesA == null || _linesB == null || blk == null) return;
            int near = _rows != null ? _rows.FindIndex(r => r.Block == blk) : -1;
            var src = toLeft ? _linesB : _linesA;
            var dst = toLeft ? _linesA : _linesB;
            int dStart = toLeft ? blk.AStart : blk.BStart;
            int dCount = toLeft ? blk.ACount : blk.BCount;
            int sStart = toLeft ? blk.BStart : blk.AStart;
            int sCount = toLeft ? blk.BCount : blk.ACount;
            var merged = new List<string>(dst.Length - dCount + sCount);
            for (int i = 0; i < dStart; i++) merged.Add(dst[i]);
            for (int i = 0; i < sCount; i++) merged.Add(src[sStart + i]);
            for (int i = dStart + dCount; i < dst.Length; i++) merged.Add(dst[i]);
            MarkSideEdited(toLeft ? _a : _b, string.Join("\n", merged));
            RunCompare();
            if (near >= 0 && _list != null && _rows.Count > 0)
                _list.ScrollToItem(Math.Min(near, _rows.Count - 1));
        }

        void SaveSide(DiffSide side)
        {
            string err;
            string text = side.Resolve(out err);
            if (text == null) { Status(err); return; }
            if (string.IsNullOrEmpty(side.Path))
            {
                string p = EditorUtility.SaveFilePanel(L10n.Tr("Save As…"), "", side.DisplayLabel, "");
                if (string.IsNullOrEmpty(p)) return;
                side.Path = p;
            }
            try
            {
                System.IO.File.WriteAllText(side.Path, text);
                side.Dirty = false;
                Status(string.Format(L10n.Tr("Saved {0}."), side.Path));
                RunCompare();
            }
            catch (Exception ex) { Status(ex.Message); }
        }

        Label SideHeader(string text)
        {
            var l = new Label(text) { tooltip = text };
            l.style.flexGrow = 1; l.style.flexBasis = 0;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.overflow = Overflow.Hidden; l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.textOverflow = TextOverflow.Ellipsis;
            return l;
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, height = RowH } };
            for (int side = 0; side < 2; side++)
            {
                var half = new VisualElement { name = side == 0 ? "ha" : "hb", style = { flexDirection = FlexDirection.Row, flexBasis = 0, overflow = Overflow.Hidden } };
                half.style.flexGrow = side == 0 ? _splitFrac : 1f - _splitFrac;
                var no = new Label { name = "no", style = { width = 40, unityTextAlign = TextAnchor.MiddleRight, marginRight = 6, opacity = 0.55f } };
                var tx = new Label { name = "tx", enableRichText = true, style = { flexGrow = 1, overflow = Overflow.Hidden, whiteSpace = WhiteSpace.NoWrap, unityTextAlign = TextAnchor.MiddleLeft } };
                Mono(no); Mono(tx);
                half.Add(no); half.Add(tx);
                row.Add(half);
                if (side == 0)
                {
                    // Center gutter between the columns: divider lines plus
                    // the per-region merge buttons (shown on region starts).
                    var gut = MakeGutter();
                    var bl = SmallBtn("◀", L10n.Tr("Copy this change to the left side."));
                    var br = SmallBtn("▶", L10n.Tr("Copy this change to the right side."));
                    bl.clicked += () => { if (row.userData is Row rr && rr.Block != null) ApplyRegion(rr.Block, toLeft: true); };
                    br.clicked += () => { if (row.userData is Row rr && rr.Block != null) ApplyRegion(rr.Block, toLeft: false); };
                    gut.Add(bl); gut.Add(br);
                    MakeSplitterDraggable(gut);
                    row.Add(gut);
                }
            }
            return row;
        }

        static Button SmallBtn(string text, string tip)
        {
            var b = new Button { text = text, tooltip = tip, name = text == "◀" ? "bl" : "br" };
            b.style.width = 17; b.style.height = 15;
            b.style.paddingLeft = 0; b.style.paddingRight = 0;
            b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.marginLeft = 1; b.style.marginRight = 1;
            b.style.marginTop = 0; b.style.marginBottom = 0;
            b.style.fontSize = 9;
            return b;
        }

        void BindRow(VisualElement el, int i)
        {
            var r = _rows[i];
            el.userData = r;
            var ha = el[0]; var hb = el[2];
            var bl = el[1].Q<Button>("bl"); var br = el[1].Q<Button>("br");
            bl.style.visibility = r.ChangeStart ? Visibility.Visible : Visibility.Hidden;
            br.style.visibility = r.ChangeStart ? Visibility.Visible : Visibility.Hidden;
            var na = ha.Q<Label>("no"); var ta = ha.Q<Label>("tx");
            var nb = hb.Q<Label>("no"); var tb = hb.Q<Label>("tx");
            na.text = r.ALine > 0 ? r.ALine.ToString() : "";
            nb.text = r.BLine > 0 ? r.BLine.ToString() : "";
            switch (r.Op)
            {
                case DiffEngine.Op.Equal:
                    ha.style.backgroundColor = StyleKeyword.Null; hb.style.backgroundColor = StyleKeyword.Null;
                    ta.text = Esc(r.AText); tb.text = Esc(r.BText);
                    break;
                case DiffEngine.Op.Delete:
                    ha.style.backgroundColor = RemovedBg; hb.style.backgroundColor = VoidBg;
                    ta.text = Esc(r.AText); tb.text = "";
                    break;
                case DiffEngine.Op.Insert:
                    ha.style.backgroundColor = VoidBg; hb.style.backgroundColor = AddedBg;
                    ta.text = ""; tb.text = Esc(r.BText);
                    break;
                case DiffEngine.Op.Replace:
                    ha.style.backgroundColor = r.ALine > 0 ? ChangedBg : VoidBg;
                    hb.style.backgroundColor = r.BLine > 0 ? ChangedBg : VoidBg;
                    ta.text = r.ALine > 0 ? RichIntra(r.AText, r.IntraStart, r.IntraLenA, MarkA) : "";
                    tb.text = r.BLine > 0 ? RichIntra(r.BText, r.IntraStart, r.IntraLenB, MarkB) : "";
                    break;
            }
        }

        void NavChange(int dir)
        {
            if (_changeRows == null || _changeRows.Count == 0 || _list == null) return;
            _navIndex = ((_navIndex + dir) % _changeRows.Count + _changeRows.Count) % _changeRows.Count;
            _list.ScrollToItem(Math.Min(_rows.Count - 1, _changeRows[_navIndex] + 3));
            Status(string.Format(L10n.Tr("Change {0} of {1}"), _navIndex + 1, _changeRows.Count));
        }

        // ---------- Folder diff ----------

        sealed class FolderRow { public string Rel; public int Kind; } // 0 same, 1 A only, 2 B only, 3 different
        [NonSerialized] List<FolderRow> _folderRows;
        [NonSerialized] bool _showSame;
        const int FolderCap = 20000;

        void BuildFolderDiff()
        {
            string da = _a.Path, db = _b.Path;
            if (!System.IO.Directory.Exists(da) || !System.IO.Directory.Exists(db))
            { Status(L10n.Tr("Choose two existing folders.")); return; }
            titleContent = new GUIContent(L10n.Tr("Folder Diff"));

            var fa = ListFiles(da, out bool cappedA);
            var fb = ListFiles(db, out bool cappedB);
            var all = new SortedSet<string>(fa.Keys, StringComparer.OrdinalIgnoreCase);
            all.UnionWith(fb.Keys);
            _folderRows = new List<FolderRow>();
            int same = 0, onlyA = 0, onlyB = 0, diff = 0;
            foreach (var rel in all)
            {
                bool inA = fa.ContainsKey(rel), inB = fb.ContainsKey(rel);
                int kind;
                if (inA && !inB) { kind = 1; onlyA++; }
                else if (!inA && inB) { kind = 2; onlyB++; }
                else if (SameFile(fa[rel], fb[rel])) { kind = 0; same++; }
                else { kind = 3; diff++; }
                _folderRows.Add(new FolderRow { Rel = rel, Kind = kind });
            }

            var show = new Toggle(L10n.Tr("Show identical files")) { value = _showSame, tooltip = L10n.Tr("Also list files that are the same in both folders.") };
            show.RegisterValueChangedCallback(e => { _showSame = e.newValue; BuildFolderList(); });
            _header.Add(show);
            var stats = new Label(string.Format(L10n.Tr("{0} different, {1} only left, {2} only right, {3} identical"), diff, onlyA, onlyB, same))
            { style = { marginLeft = 8, opacity = 0.85f }, tooltip = L10n.Tr("Comparison summary. Double-click a row to open the file diff.") };
            _header.Add(stats);

            var cols = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            cols.style.borderBottomWidth = 1;
            cols.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            cols.Add(SideHeader(da));
            cols.Add(SideHeader(db));
            _content.Add(cols);
            BuildFolderList();
            if (cappedA || cappedB)
                Status(string.Format(L10n.Tr("Folder scan capped at {0} files per side — results are incomplete."), FolderCap));
        }

        [NonSerialized] ListView _folderList;
        [NonSerialized] List<FolderRow> _folderVisible;

        void BuildFolderList()
        {
            _folderVisible = _folderRows.Where(r => _showSame || r.Kind != 0).ToList();
            if (_folderList != null) { _folderList.RemoveFromHierarchy(); }
            _folderList = new ListView(_folderVisible, RowH, () =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, height = RowH, alignItems = Align.Center } };
                var st = new Label { name = "st", style = { width = 64, opacity = 0.8f } };
                var pth = new Label { name = "p", style = { flexGrow = 1, overflow = Overflow.Hidden, whiteSpace = WhiteSpace.NoWrap } };
                Mono(pth);
                row.Add(st); row.Add(pth);
                row.RegisterCallback<PointerDownEvent>(ev =>
                {
                    if (ev.clickCount < 2) return;
                    var fr = row.userData as FolderRow;
                    if (fr != null) OpenFolderPair(fr);
                });
                return row;
            }, (el, i) =>
            {
                var fr = _folderVisible[i];
                el.userData = fr;
                var st = el.Q<Label>("st"); var pth = el.Q<Label>("p");
                pth.text = fr.Rel;
                pth.tooltip = L10n.Tr("Double-click to open this pair as a file diff.");
                switch (fr.Kind)
                {
                    case 0: st.text = L10n.Tr("same"); el.style.backgroundColor = StyleKeyword.Null; break;
                    case 1: st.text = L10n.Tr("left"); el.style.backgroundColor = RemovedBg; break;
                    case 2: st.text = L10n.Tr("right"); el.style.backgroundColor = AddedBg; break;
                    default: st.text = L10n.Tr("differs"); el.style.backgroundColor = ChangedBg; break;
                }
            }) { style = { flexGrow = 1 } };
            _folderList.selectionType = SelectionType.Single;
            _content.Add(_folderList);
        }

        void OpenFolderPair(FolderRow fr)
        {
            string pa = System.IO.Path.Combine(_a.Path, fr.Rel);
            string pb = System.IO.Path.Combine(_b.Path, fr.Rel);
            if (fr.Kind == 1) OpenTextVsFileMissing(pa, true);
            else if (fr.Kind == 2) OpenTextVsFileMissing(pb, false);
            else OpenFiles(pa, pb);
        }

        void OpenTextVsFileMissing(string existing, bool onLeft)
        {
            string content;
            try { content = System.IO.File.ReadAllText(existing); }
            catch (Exception ex) { Status(ex.Message); return; }
            string missing = L10n.Tr("(missing)");
            if (onLeft) OpenTexts(System.IO.Path.GetFileName(existing), content, missing, "");
            else OpenTexts(missing, "", System.IO.Path.GetFileName(existing), content);
        }

        static Dictionary<string, string> ListFiles(string root, out bool capped)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            capped = false;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files, dirs;
                try { files = System.IO.Directory.GetFiles(dir); dirs = System.IO.Directory.GetDirectories(dir); }
                catch (Exception) { continue; }
                foreach (var f in files)
                {
                    if (map.Count >= FolderCap) { capped = true; return map; }
                    map[f.Substring(root.Length).TrimStart('/', '\\')] = f;
                }
                foreach (var d in dirs) stack.Push(d);
            }
            return map;
        }

        static bool SameFile(string pa, string pb)
        {
            try
            {
                var ia = new System.IO.FileInfo(pa); var ib = new System.IO.FileInfo(pb);
                if (ia.Length != ib.Length) return false;
                // Equal size: compare bytes (streamed, early-out).
                using (var sa = ia.OpenRead()) using (var sb2 = ib.OpenRead())
                {
                    var ba = new byte[65536]; var bb = new byte[65536];
                    while (true)
                    {
                        int na = ReadFull(sa, ba); int nb = ReadFull(sb2, bb);
                        if (na != nb) return false;
                        if (na == 0) return true;
                        for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                    }
                }
            }
            catch (Exception) { return false; }
        }

        static int ReadFull(System.IO.Stream s, byte[] buf)
        {
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) break;
                off += n;
            }
            return off;
        }

        // ---------- Three-way merge ----------

        void BuildMerge()
        {
            string el, er, eb;
            string tl = _a.Resolve(out el);
            string tr = _b.Resolve(out er);
            string tb = _base.Resolve(out eb);
            if (tl == null || tr == null || tb == null)
            { Status(string.Join("  |  ", new[] { el, er, eb }.Where(s => s != null))); return; }
            titleContent = new GUIContent(L10n.Tr("Merge") + ": " + (!string.IsNullOrEmpty(_outPath) ? System.IO.Path.GetFileName(_outPath) : _a.DisplayLabel));

            _chunks = DiffEngine.Merge3(DiffEngine.SplitLines(tb), DiffEngine.SplitLines(tl), DiffEngine.SplitLines(tr));
            int conflicts = _chunks.Count(c => c.Kind == DiffEngine.ChunkKind.Conflict);
            if (_choices == null || _choices.Length != conflicts) _choices = new int[conflicts];

            HeaderBtn(L10n.Tr("All Left"), L10n.Tr("Resolve every conflict by taking the left side."), () => { for (int i = 0; i < _choices.Length; i++) _choices[i] = 1; RefreshMerge(); });
            HeaderBtn(L10n.Tr("All Right"), L10n.Tr("Resolve every conflict by taking the right side."), () => { for (int i = 0; i < _choices.Length; i++) _choices[i] = 2; RefreshMerge(); });
            HeaderBtn(L10n.Tr("Save"), L10n.Tr("Write the merged result to the output file."), SaveMerge);
            HeaderBtn(L10n.Tr("Save As…"), L10n.Tr("Write the merged result to a new file."), () =>
            {
                string p = EditorUtility.SaveFilePanel(L10n.Tr("Save merged result"), "", System.IO.Path.GetFileName(_outPath ?? "merged.txt"), "");
                if (!string.IsNullOrEmpty(p)) { _outPath = p; SaveMerge(); }
            });
            _conflictLbl = new Label { style = { marginLeft = 8, opacity = 0.85f }, tooltip = L10n.Tr("Unresolved conflict count.") };
            _header.Add(_conflictLbl);

            var split = new TwoPaneSplitView(0, Mathf.Max(220f, position.height * 0.55f), TwoPaneSplitViewOrientation.Vertical) { style = { flexGrow = 1 } };
            _content.Add(split);

            var top = new VisualElement();
            var colHead = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            colHead.Add(SideHeader(string.Format(L10n.Tr("Left: {0}"), _a.DisplayLabel)));
            colHead.Add(SideHeader(string.Format(L10n.Tr("Base: {0}"), _base.DisplayLabel)));
            colHead.Add(SideHeader(string.Format(L10n.Tr("Right: {0}"), _b.DisplayLabel)));
            top.Add(colHead);
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            top.Add(scroll);
            split.Add(top);

            _conflictPanels = new List<VisualElement>();
            int ci = 0;
            foreach (var ch in _chunks)
            {
                if (ch.Kind == DiffEngine.ChunkKind.Clean)
                {
                    if (ch.Lines.Length == 0) continue;
                    var lbl = new Label(Esc(string.Join("\n", ch.Lines))) { enableRichText = false };
                    Mono(lbl);
                    lbl.style.whiteSpace = WhiteSpace.Pre;
                    lbl.style.opacity = 0.75f;
                    scroll.Add(lbl);
                    continue;
                }
                int idx = ci++;
                var panel = new VisualElement { style = { marginTop = 4, marginBottom = 4, borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 3, borderRightWidth = 1 } };
                SetBorder(panel, new Color(0.9f, 0.45f, 0.2f, 0.9f));
                var strip = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, backgroundColor = new Color(0.9f, 0.45f, 0.2f, 0.12f) } };
                strip.Add(new Label(string.Format(L10n.Tr("Conflict {0}"), idx + 1)) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8 }, tooltip = L10n.Tr("Both sides changed this region differently.") });
                ChoiceBtn(strip, idx, 1, L10n.Tr("Take Left"), L10n.Tr("Use the left side's lines for this conflict."));
                ChoiceBtn(strip, idx, 3, L10n.Tr("Take Base"), L10n.Tr("Keep the base (original) lines for this conflict."));
                ChoiceBtn(strip, idx, 2, L10n.Tr("Take Right"), L10n.Tr("Use the right side's lines for this conflict."));
                ChoiceBtn(strip, idx, 4, L10n.Tr("Take Both"), L10n.Tr("Use the left lines followed by the right lines."));
                panel.Add(strip);
                var cols3 = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                cols3.Add(MergeCol(ch.Left, RemovedBg));
                cols3.Add(MergeCol(ch.Base, VoidBg));
                cols3.Add(MergeCol(ch.Right, AddedBg));
                panel.Add(cols3);
                scroll.Add(panel);
                _conflictPanels.Add(panel);
            }

            var bottom = new VisualElement();
            var resHead = new Label(L10n.Tr("Result (editable)")) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexShrink = 0 }, tooltip = L10n.Tr("The merged output. Edit freely; choosing a conflict resolution rebuilds it from the choices (manual edits are replaced).") };
            bottom.Add(resHead);
            _resultField = new TextField { multiline = true, style = { flexGrow = 1 } };
            _resultField.verticalScrollerVisibility = ScrollerVisibility.Auto;
            Mono(_resultField);
            _resultField.tooltip = L10n.Tr("The merged output. Edit freely; choosing a conflict resolution rebuilds it from the choices (manual edits are replaced).");
            _resultField.RegisterValueChangedCallback(e => { if (!_suppressResultDirty) _resultText = e.newValue; });
            bottom.Add(_resultField);
            split.Add(bottom);

            // Restore a surviving result (reload) or build the initial one.
            if (string.IsNullOrEmpty(_resultText)) RebuildResult();
            else SetResult(_resultText);
            RefreshMerge();
        }

        void ChoiceBtn(VisualElement strip, int idx, int val, string text, string tip)
        {
            var b = new Button(() => { _choices[idx] = _choices[idx] == val ? 0 : val; RebuildResult(); RefreshMerge(); }) { text = text, tooltip = tip };
            b.userData = "choice:" + idx + ":" + val;
            strip.Add(b);
        }

        VisualElement MergeCol(string[] lines, Color bg)
        {
            var col = new VisualElement { style = { flexGrow = 1, flexBasis = 0, backgroundColor = bg, paddingLeft = 4, overflow = Overflow.Hidden } };
            var lbl = new Label(lines.Length == 0 ? L10n.Tr("(empty)") : Esc(string.Join("\n", lines))) { enableRichText = false };
            if (lines.Length == 0) lbl.style.opacity = 0.5f;
            Mono(lbl);
            lbl.style.whiteSpace = WhiteSpace.Pre;
            col.Add(lbl);
            return col;
        }

        void RefreshMerge()
        {
            int open = _choices.Count(c => c == 0);
            if (_conflictLbl != null)
                _conflictLbl.text = open == 0
                    ? string.Format(L10n.Tr("{0} conflict(s), all resolved."), _choices.Length)
                    : string.Format(L10n.Tr("{0} of {1} conflict(s) unresolved."), open, _choices.Length);
            for (int i = 0; i < _conflictPanels.Count; i++)
            {
                var strip = _conflictPanels[i][0];
                foreach (var child in strip.Children())
                {
                    if (!(child is Button b) || !(b.userData is string s) || !s.StartsWith("choice:")) continue;
                    var parts = s.Split(':');
                    int val = int.Parse(parts[2]);
                    b.style.backgroundColor = _choices[i] == val
                        ? new Color(0.3f, 0.55f, 0.8f, 0.6f) : StyleKeyword.Null;
                }
                SetBorder(_conflictPanels[i], _choices[i] == 0
                    ? new Color(0.9f, 0.45f, 0.2f, 0.9f)
                    : new Color(0.35f, 0.65f, 0.35f, 0.9f));
            }
        }

        void RebuildResult()
        {
            var sb = new System.Text.StringBuilder();
            int ci = 0;
            foreach (var ch in _chunks)
            {
                if (ch.Kind == DiffEngine.ChunkKind.Clean)
                { foreach (var l in ch.Lines) sb.Append(l).Append('\n'); continue; }
                int choice = _choices[ci++];
                switch (choice)
                {
                    case 1: foreach (var l in ch.Left) sb.Append(l).Append('\n'); break;
                    case 2: foreach (var l in ch.Right) sb.Append(l).Append('\n'); break;
                    case 3: foreach (var l in ch.Base) sb.Append(l).Append('\n'); break;
                    case 4:
                        foreach (var l in ch.Left) sb.Append(l).Append('\n');
                        foreach (var l in ch.Right) sb.Append(l).Append('\n');
                        break;
                    default:
                        sb.Append("<<<<<<< ").Append(L10n.Tr("LEFT")).Append('\n');
                        foreach (var l in ch.Left) sb.Append(l).Append('\n');
                        sb.Append("||||||| ").Append(L10n.Tr("BASE")).Append('\n');
                        foreach (var l in ch.Base) sb.Append(l).Append('\n');
                        sb.Append("=======\n");
                        foreach (var l in ch.Right) sb.Append(l).Append('\n');
                        sb.Append(">>>>>>> ").Append(L10n.Tr("RIGHT")).Append('\n');
                        break;
                }
            }
            if (sb.Length > 0) sb.Length--; // no trailing extra newline
            SetResult(sb.ToString());
        }

        void SetResult(string text)
        {
            _resultText = text;
            if (_resultField == null) return;
            _suppressResultDirty = true;
            _resultField.SetValueWithoutNotify(text);
            _suppressResultDirty = false;
        }

        void SaveMerge()
        {
            if (string.IsNullOrEmpty(_outPath))
            {
                string p = EditorUtility.SaveFilePanel(L10n.Tr("Save merged result"), "", "merged.txt", "");
                if (string.IsNullOrEmpty(p)) return;
                _outPath = p;
            }
            int open = _choices.Count(c => c == 0);
            try
            {
                System.IO.File.WriteAllText(_outPath, _resultField != null ? _resultField.value : _resultText);
                Status(open == 0
                    ? string.Format(L10n.Tr("Saved {0}."), _outPath)
                    : string.Format(L10n.Tr("Saved {0} — {1} conflict(s) still contain <<<<<<< markers."), _outPath, open));
                if (_fromUnityMerge) AssetDatabase.Refresh();
            }
            catch (Exception ex) { Status(ex.Message); }
        }

        // ---------- Shared bits ----------

        static void Mono(VisualElement v)
        {
            v.style.unityFontDefinition = StyleKeyword.Null;
            v.style.unityFont = CodeView.MonoFont();
            v.style.fontSize = 12;
        }

        static void SetBorder(VisualElement v, Color c)
        {
            v.style.borderTopColor = c; v.style.borderBottomColor = c;
            v.style.borderLeftColor = c; v.style.borderRightColor = c;
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = ExpandTabs(s);
            return s.Replace("&", "&amp;").Replace("<", "&lt;");
        }

        static string ExpandTabs(string s)
            => s.IndexOf('\t') < 0 ? s : TextDocument.ExpandTabs(s, EditorConfig.TabSize);

        /// <summary>Escaped line with the intra-line changed span wrapped in
        /// a colored &lt;mark&gt;. Span indices refer to the RAW string, so
        /// mark first, then escape each part separately.</summary>
        static string RichIntra(string raw, int start, int len, string colorHex)
        {
            if (start < 0 || len <= 0 || start > raw.Length)
                return Esc(raw);
            len = Math.Min(len, raw.Length - start);
            string pre = raw.Substring(0, start);
            string mid = raw.Substring(start, len);
            string post = raw.Substring(start + len);
            // Tab expansion is column-aware, so expand around the split
            // conservatively: expand the concatenation piecewise is not
            // column-exact, but close enough for a highlight.
            return Esc(pre) + "<mark=" + colorHex + ">" + Esc(mid) + "</mark>" + Esc(post);
        }
    }
}
#endif
