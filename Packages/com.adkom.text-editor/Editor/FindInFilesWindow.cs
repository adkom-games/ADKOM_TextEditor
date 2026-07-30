#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Find/Replace in Files: searches the project's text files (Assets +
    /// Packages by default, or any chosen root) with plain/regex queries —
    /// open buffers searched as the user sees them — and lists matches
    /// grouped per file with a checkbox each. The preview pane shows the
    /// selected match's line before → after; Replace Selected applies every
    /// checked match as ONE journaled operation, so Undo/Redo Replace
    /// restores all touched files (open or closed) in one click.
    /// </summary>
    public class FindInFilesWindow : EditorWindow
    {
        static FindInFilesWindow _instance;

        TextEditorWindow _owner;
        System.Threading.SynchronizationContext _ctx;

        TextField _query, _replace, _glob, _root;
        Toggle _case, _word, _regex;
        Label _status, _previewBefore, _previewAfter;
        Button _searchBtn, _replaceBtn, _undoBtn, _redoBtn;
        ScrollView _results;

        List<FindInFiles.Match> _matches = new List<FindInFiles.Match>();
        List<bool> _checked = new List<bool>();
        readonly List<VisualElement> _rowPool = new List<VisualElement>();
        int _previewIdx = -1;
        bool _searching;
        const int MaxRows = 1000;

        public static void Open(TextEditorWindow owner)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<FindInFilesWindow>();
                _instance.titleContent = new GUIContent(L10n.Tr("Find in Files"));
                _instance.minSize = new Vector2(640, 380);
                _instance.ShowUtility();
            }
            _instance._owner = owner;
            _instance._ctx = System.Threading.SynchronizationContext.Current;
            _instance.BuildUI();
            _instance.Focus();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = 6;

            var r1 = Row();
            _query = new TextField { style = { flexGrow = 1 } };
            _query.RegisterCallback<KeyDownEvent>(e =>
            { if (e.keyCode == KeyCode.Return) { StartSearch(); e.StopPropagation(); } });
            r1.Add(new Label(L10n.Tr("Find:")) { style = { width = 64, unityTextAlign = TextAnchor.MiddleLeft } });
            r1.Add(_query);
            _searchBtn = new Button(StartSearch) { text = L10n.Tr("Search") };
            r1.Add(_searchBtn);
            root.Add(r1);

            var r2 = Row();
            _replace = new TextField { style = { flexGrow = 1 } };
            r2.Add(new Label(L10n.Tr("Replace:")) { style = { width = 64, unityTextAlign = TextAnchor.MiddleLeft } });
            r2.Add(_replace);
            root.Add(r2);

            var r3 = Row();
            _case = new Toggle(L10n.Tr("Match Case"));
            _word = new Toggle(L10n.Tr("Whole Word"));
            _regex = new Toggle(L10n.Tr("Regex"));
            _glob = new TextField(L10n.Tr("Files")) { value = "*", style = { width = 150 } };
            r3.Add(_case); r3.Add(_word); r3.Add(_regex); r3.Add(_glob);
            root.Add(r3);

            var r4 = Row();
            _root = new TextField(L10n.Tr("Root")) { style = { flexGrow = 1 } };
            _root.tooltip = L10n.Tr("Folder to search. Empty = Assets + Packages.");
            var browse = new Button(() =>
            {
                string picked = EditorUtility.OpenFolderPanel(L10n.Tr("Search root folder"), _root.value, "");
                if (!string.IsNullOrEmpty(picked)) _root.value = picked;
            }) { text = "..." };
            r4.Add(_root); r4.Add(browse);
            root.Add(r4);

            _status = new Label(L10n.Tr("Enter a query and press Search.")) { style = { marginTop = 2, opacity = 0.8f } };
            root.Add(_status);

            _results = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, marginTop = 4 } };
            root.Add(_results);

            _previewBefore = new Label { enableRichText = true, style = { whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden, marginTop = 4 } };
            _previewAfter = new Label { enableRichText = true, style = { whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden } };
            _previewBefore.AddToClassList("code-line");
            _previewAfter.AddToClassList("code-line");
            root.Add(_previewBefore);
            root.Add(_previewAfter);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 6, marginBottom = 6 } };
            _undoBtn = new Button(() => { string l = _owner?.UndoReplaceInFiles(); if (l != null) { _status.text = string.Format(L10n.Tr("Undid: {0}"), l); StartSearch(); } }) { text = L10n.Tr("Undo Replace") };
            _redoBtn = new Button(() => { string l = _owner?.RedoReplaceInFiles(); if (l != null) { _status.text = string.Format(L10n.Tr("Redid: {0}"), l); StartSearch(); } }) { text = L10n.Tr("Redo Replace") };
            _replaceBtn = new Button(ReplaceSelected) { text = L10n.Tr("Replace Selected") };
            buttons.Add(_undoBtn);
            buttons.Add(_redoBtn);
            buttons.Add(_replaceBtn);
            root.Add(buttons);

            root.schedule.Execute(() =>
            {
                _undoBtn.SetEnabled(ReplaceJournal.CanUndo);
                _redoBtn.SetEnabled(ReplaceJournal.CanRedo);
                _undoBtn.tooltip = ReplaceJournal.UndoLabel ?? "";
                _redoBtn.tooltip = ReplaceJournal.RedoLabel ?? "";
            }).Every(300);
        }

        static VisualElement Row() => new VisualElement
        { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2, flexShrink = 0 } };

        void StartSearch()
        {
            if (_owner == null || _searching || string.IsNullOrEmpty(_query.value)) return;
            var o = new FindInFiles.Options
            {
                Query = _query.value,
                Replace = _replace.value ?? "",
                Regex = _regex.value,
                MatchCase = _case.value,
                WholeWord = _word.value,
                Glob = _glob.value,
            };
            if (o.Regex)
            {
                try { _ = new System.Text.RegularExpressions.Regex(o.Query); }
                catch (System.Exception ex)
                { _status.text = string.Format(L10n.Tr("Invalid regex: {0}"), ex.Message); return; }
            }
            string[] roots = string.IsNullOrWhiteSpace(_root.value)
                ? FindInFiles.DefaultRoots()
                : new[] { _root.value.Trim() };
            var open = _owner.OpenDocSnapshots();
            _searching = true;
            _status.text = L10n.Tr("Searching...");
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
                    if (this == null || _results == null) return;
                    if (error != null) { _status.text = error; return; }
                    _matches = found;
                    _checked = Enumerable.Repeat(true, found.Count).ToList();
                    _previewIdx = -1;
                    int files = found.Select(m => m.Path).Distinct().Count();
                    _status.text = string.Format(L10n.Tr("{0} match(es) in {1} file(s), {2} file(s) scanned."),
                        found.Count, files, scanned) + (truncated ? "  " + L10n.Tr("(truncated)") : "");
                    RebuildRows();
                    UpdatePreview();
                }, null);
            });
        }

        void RebuildRows()
        {
            _results.Clear();
            _rowPool.Clear();
            string lastFile = null;
            int rows = 0;
            for (int i = 0; i < _matches.Count && rows < MaxRows; i++)
            {
                var m = _matches[i];
                if (m.Path != lastFile)
                {
                    lastFile = m.Path;
                    var header = Row();
                    string file = m.Path;
                    var fileToggle = new Toggle { value = FileAllChecked(file) };
                    fileToggle.RegisterValueChangedCallback(e =>
                    {
                        for (int k = 0; k < _matches.Count; k++)
                            if (_matches[k].Path == file) _checked[k] = e.newValue;
                        RefreshRowChecks();
                    });
                    header.Add(fileToggle);
                    var name = new Label(ShortPath(file))
                    { style = { unityFontStyleAndWeight = FontStyle.Bold }, tooltip = file };
                    header.Add(name);
                    _results.Add(header);
                    _rowPool.Add(header);
                    rows++;
                }
                int idx = i;
                var row = Row();
                row.style.marginLeft = 18;
                var t = new Toggle { value = _checked[i] };
                t.RegisterValueChangedCallback(e => { _checked[idx] = e.newValue; RefreshRowChecks(); });
                row.Add(t);
                var lbl = new Label("L" + (m.Line + 1) + ":  " + m.LineText)
                { style = { whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden } };
                lbl.AddToClassList("code-line");
                lbl.RegisterCallback<PointerDownEvent>(e =>
                {
                    _previewIdx = idx;
                    UpdatePreview();
                    if (e.clickCount >= 2)
                        TextEditorWindow.OpenExternal(_matches[idx].Path, _matches[idx].Line + 1, _matches[idx].Col + 1);
                    e.StopPropagation();
                });
                row.Add(lbl);
                _results.Add(row);
                _rowPool.Add(row);
                rows++;
            }
            if (_matches.Count == 0)
                _results.Add(new Label(L10n.Tr("No matches.")) { style = { opacity = 0.7f, paddingLeft = 4 } });
        }

        bool FileAllChecked(string file)
        {
            for (int i = 0; i < _matches.Count; i++)
                if (_matches[i].Path == file && !_checked[i]) return false;
            return true;
        }

        void RefreshRowChecks()
        {
            // Row order mirrors build order; walk both in lockstep.
            string lastFile = null;
            int rowIdx = 0;
            for (int i = 0; i < _matches.Count && rowIdx < _rowPool.Count; i++)
            {
                if (_matches[i].Path != lastFile)
                {
                    lastFile = _matches[i].Path;
                    (_rowPool[rowIdx].Q<Toggle>()).SetValueWithoutNotify(FileAllChecked(lastFile));
                    rowIdx++;
                }
                if (rowIdx < _rowPool.Count)
                    (_rowPool[rowIdx].Q<Toggle>()).SetValueWithoutNotify(_checked[i]);
                rowIdx++;
            }
        }

        static string Np(string s) => s.IndexOf('<') >= 0 ? "<noparse>" + s + "</noparse>" : s;

        void UpdatePreview()
        {
            if (_previewIdx < 0 || _previewIdx >= _matches.Count)
            {
                _previewBefore.text = L10n.Tr("Select a match to preview; double-click to open it.");
                _previewAfter.text = "";
                return;
            }
            var m = _matches[_previewIdx];
            string before = m.LineText;
            // The after-line: splice the replacement into the line preview.
            int colInPreview = Mathf.Clamp(m.Col, 0, before.Length);
            string after = before;
            int end = Mathf.Min(colInPreview + m.Length, before.Length);
            if (before.Substring(colInPreview, end - colInPreview) == m.MatchText || m.Length > before.Length)
                after = before.Substring(0, colInPreview) + (m.Replacement ?? "") + before.Substring(end);
            _previewBefore.text = "<color=#E06C6C>− " + Np(before) + "</color>";
            _previewAfter.text = "<color=#7CBF7C>+ " + Np(after) + "</color>";
        }

        void ReplaceSelected()
        {
            if (_owner == null) return;
            var selected = new List<FindInFiles.Match>();
            for (int i = 0; i < _matches.Count; i++)
                if (_checked[i]) selected.Add(_matches[i]);
            if (selected.Count == 0) { _status.text = L10n.Tr("Nothing selected."); return; }
            var (files, replaced, skipped) = _owner.ApplyReplaceInFiles(selected);
            _status.text = string.Format(L10n.Tr("Replaced {0} match(es) in {1} file(s)."), replaced, files)
                + (skipped > 0 ? "  " + string.Format(L10n.Tr("Skipped {0} stale match(es)."), skipped) : "");
            StartSearch(); // results are stale now; refresh
        }

        static string ShortPath(string full)
        {
            string proj = FindInFiles.Norm(Path.GetDirectoryName(Application.dataPath));
            string p = FindInFiles.Norm(full);
            return p.StartsWith(proj, System.StringComparison.OrdinalIgnoreCase)
                ? p.Substring(proj.Length + 1) : p;
        }
    }
}
#endif
