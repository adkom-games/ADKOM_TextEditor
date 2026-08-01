#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Search Results: a tab in the console pane (next to Console) listing
    // locations from multi-result commands (Find All References, ...). Rows
    // are clickable and jump to the location, opening the file if needed.
    public partial class TextEditorWindow
    {
        internal struct PickLocation
        {
            public string Path;    // absolute file path (or display name, see DocId)
            public int Line, Col;  // 0-based
            public string Preview; // trimmed line text
            public int DocId;      // 0 = Path is a file; else docIndex+1 of an
                                   // untitled tab (Path holds its display name)
        }

        VisualElement _searchPane;
        TextField _searchFilter;
        ScrollView _searchScroll;
        Label _searchHeader;
        readonly List<Label> _searchLabels = new List<Label>();
        List<PickLocation> _searchItems = new List<PickLocation>();
        readonly List<int> _searchRowItem = new List<int>(); // visible row -> item index
        string _searchTitle = "";

        /// <summary>Fills the Search Results tab and brings it to the front
        /// (showing the console pane if hidden).</summary>
        internal void ShowSearchResults(string title, List<PickLocation> items)
        {
            _searchItems = items ?? new List<PickLocation>();
            _searchTitle = title;
            RenderSearchResults();
            _searchTabVisible = true; // results reveal the tab even if hidden
            ApplyConsoleTabVisibility();
            SetConsoleVisible(true);
            SelectConsoleTab(1);
        }

        /// <summary>(Re)renders the result rows applying the Filter box — a
        /// case-insensitive substring over the file name, full path, and the
        /// matched line text. The header shows "N of M shown" when filtering.</summary>
        void RenderSearchResults()
        {
            string filter = _searchFilter != null ? (_searchFilter.value ?? "").Trim().ToLowerInvariant() : "";
            _searchRowItem.Clear();
            int row = 0;
            for (int i = 0; i < _searchItems.Count; i++)
            {
                var it = _searchItems[i];
                if (filter.Length > 0)
                {
                    string hay = (Path.GetFileName(it.Path) + " " + it.Path + " " + it.Preview).ToLowerInvariant();
                    if (!hay.Contains(filter)) continue;
                }
                if (row >= _searchLabels.Count)
                {
                    var l = new Label();
                    l.AddToClassList("code-line");
                    l.style.paddingTop = 1;
                    l.style.paddingBottom = 1;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    int rowIdx = row;
                    l.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0) return;
                        JumpToSearchRow(rowIdx);
                        e.StopPropagation();
                    });
                    l.RegisterCallback<MouseEnterEvent>(_ =>
                        l.style.backgroundColor = AteViewStyle.HoverRow);
                    l.RegisterCallback<MouseLeaveEvent>(_ =>
                        AteViewStyle.Zebra(l, rowIdx));
                    _searchScroll.Add(l);
                    _searchLabels.Add(l);
                }
                _searchLabels[row].text = "  " + Path.GetFileName(it.Path) + ":" + (it.Line + 1) + "   " + it.Preview;
                _searchLabels[row].tooltip = it.Path + ":" + (it.Line + 1);
                _searchLabels[row].style.display = DisplayStyle.Flex;
                AteViewStyle.Zebra(_searchLabels[row], row);
                _searchRowItem.Add(i);
                row++;
            }
            for (int i = row; i < _searchLabels.Count; i++)
                _searchLabels[i].style.display = DisplayStyle.None;
            _searchHeader.text = filter.Length == 0 || row == _searchItems.Count
                ? _searchTitle
                : string.Format(L10n.Tr("{0}  ({1} of {2} shown)"), _searchTitle, row, _searchItems.Count);
        }

        void JumpToSearchRow(int row)
        {
            if (row < 0 || row >= _searchRowItem.Count) return;
            JumpToLocation(_searchItems[_searchRowItem[row]]);
        }

        /// <summary>Shared row-click jump for the Search Results and
        /// Bookmarks tabs.</summary>
        void JumpToLocation(PickLocation it)
        {
            PushNavLocation();
            if (it.DocId > 0)
            {
                // Untitled tab: jump by tab index (snapshot; may be stale
                // after tabs close, in which case the jump is skipped).
                int di = it.DocId - 1;
                if (di < _docs.Count && !_docs[di].HasFile)
                {
                    SwitchTo(di);
                    _code.GoToLine(it.Line + 1, it.Col + 1);
                }
                return;
            }
            OpenExternal(it.Path, it.Line + 1, it.Col + 1); // opens the tab if needed
        }

        // ---- Scanner Results: same shape, fed by the addon security scan.
        // The tab is hidden until a scan with findings shows it, and its
        // label names the scanned script ("Snake Scanner Results").

        ScrollView _scannerScroll;
        Label _scannerHeader;
        readonly List<Label> _scannerLabels = new List<Label>();
        List<PickLocation> _scannerItems = new List<PickLocation>();

        /// <summary>Fills the "&lt;script&gt; Scanner Results" console tab and
        /// brings it to the front. Rows jump to file:line, opening the addon
        /// file in a tab when it is not open yet.</summary>
        internal void ShowScannerResults(string scriptName, List<PickLocation> items)
        {
            if (_scannerScroll == null) return;
            _scannerItems = items ?? new List<PickLocation>();
            _scannerTabLabel.text = string.Format(L10n.Tr("{0} Scanner Results"), scriptName);
            _scannerHeader.text = string.Format(
                _scannerItems.Count == 1
                    ? L10n.Tr("{0}: {1} potentially dangerous API")
                    : L10n.Tr("{0}: {1} potentially dangerous APIs"),
                scriptName, _scannerItems.Count);
            for (int i = 0; i < _scannerItems.Count; i++)
            {
                if (i >= _scannerLabels.Count)
                {
                    var l = new Label();
                    l.AddToClassList("code-line");
                    l.style.paddingTop = 1;
                    l.style.paddingBottom = 1;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    int idx = i;
                    l.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0) return;
                        JumpToScannerResult(idx);
                        e.StopPropagation();
                    });
                    l.RegisterCallback<MouseEnterEvent>(_ =>
                        l.style.backgroundColor = AteViewStyle.HoverRow);
                    l.RegisterCallback<MouseLeaveEvent>(_ =>
                        AteViewStyle.Zebra(l, idx));
                    _scannerScroll.Add(l);
                    _scannerLabels.Add(l);
                }
                var it = _scannerItems[i];
                _scannerLabels[i].text = "  " + Path.GetFileName(it.Path) + ":" + (it.Line + 1) + "   " + it.Preview;
                _scannerLabels[i].tooltip = it.Path + ":" + (it.Line + 1);
                _scannerLabels[i].style.display = DisplayStyle.Flex;
                AteViewStyle.Zebra(_scannerLabels[i], i);
            }
            for (int i = _scannerItems.Count; i < _scannerLabels.Count; i++)
                _scannerLabels[i].style.display = DisplayStyle.None;
            _scannerTab.style.display = DisplayStyle.Flex;
            SetConsoleVisible(true);
            SelectConsoleTab(2);
        }

        void JumpToScannerResult(int idx)
        {
            if (idx < 0 || idx >= _scannerItems.Count) return;
            var it = _scannerItems[idx];
            PushNavLocation();
            OpenExternal(it.Path, it.Line + 1, it.Col + 1); // opens the tab if needed
        }

        // ---- Bookmarks: same shape as Search Results, fed by
        // Edit > Bookmarks > View Bookmarks. Lists every bookmarked line of
        // every open document; rows jump to the line. ----

        VisualElement _bmPane;
        TextField _bmFilter;
        ScrollView _bmScroll;
        Label _bmHeader;
        List<PickLocation> _bmItems = new List<PickLocation>();
        // Disclosure state per file: survives re-renders and filter changes.
        readonly Dictionary<string, bool> _bmCollapsed = new Dictionary<string, bool>();

        void BuildBookmarksPane(VisualElement consolePane)
        {
            _bmPane = new VisualElement { name = "bookmarks-pane",
                style = { display = DisplayStyle.None, flexGrow = 1, flexDirection = FlexDirection.Column } };
            var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
            _bmHeader = new Label(L10n.Tr("(no bookmarks)"));
            _bmHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bmHeader.style.paddingLeft = 4;
            _bmHeader.style.flexGrow = 1;
            _bmHeader.style.whiteSpace = WhiteSpace.NoWrap;
            _bmHeader.style.overflow = Overflow.Hidden;
            top.Add(_bmHeader);
            top.Add(new Label(L10n.Tr("Filter")) { style = { marginRight = 2 } });
            _bmFilter = new TextField { name = "bookmarks-filter", style = { width = 160, marginRight = 4 },
                tooltip = L10n.Tr("Show only rows whose file name, path, or text contains this.") };
            _bmFilter.RegisterValueChangedCallback(_ => RenderBookmarks());
            top.Add(_bmFilter);
            _bmPane.Add(top);
            _bmScroll = new ScrollView(ScrollViewMode.Vertical) { name = "bookmarks-scroll", style = { flexGrow = 1 } };
            AteViewStyle.Frame(_bmScroll);
            AteViewStyle.Mono(_bmScroll);
            _bmPane.Add(_bmScroll);
            consolePane.Add(_bmPane);
        }

        /// <summary>Snapshots every open document's bookmarks into the
        /// Bookmarks console tab and brings it to the front.</summary>
        internal void ShowBookmarksTab()
        {
            var items = new List<PickLocation>();
            int docsWith = 0;
            for (int i = 0; i < _docs.Count; i++)
            {
                var d = _docs[i];
                if (d.IsSettings || d.Bookmarks == null || d.Bookmarks.Count == 0) continue;
                string[] lines = (GetDocContent(i) ?? "").Split('\n');
                var sorted = new List<int>(d.Bookmarks);
                sorted.Sort();
                bool any = false;
                foreach (int line in sorted)
                {
                    if (line < 0 || line >= lines.Length) continue;
                    string text = lines[line].TrimEnd('\r');
                    if (text.Length > 200) text = text.Substring(0, 200);
                    items.Add(new PickLocation
                    {
                        Path = d.HasFile ? d.FilePath : d.DisplayName,
                        DocId = d.HasFile ? 0 : i + 1,
                        Line = line,
                        Col = 0,
                        Preview = text,
                    });
                    any = true;
                }
                if (any) docsWith++;
            }
            _bmItems = items;
            _bmHeader.text = items.Count == 0 ? L10n.Tr("(no bookmarks)")
                : string.Format(L10n.Tr("{0} bookmark(s) in {1} document(s)"), items.Count, docsWith);
            RenderBookmarks();
            _bmTabVisible = true;
            ApplyConsoleTabVisibility();
            SetConsoleVisible(true);
            SelectConsoleTab(4);
        }

        /// <summary>(Re)renders the bookmark rows applying the Filter box:
        /// one Foldout per file (sorted by file name), rows inside jump to
        /// the line — the same disclosure-group shape Find in Files results
        /// used. Rebuilt outright; bookmark lists are small.</summary>
        void RenderBookmarks()
        {
            string filter = _bmFilter != null ? (_bmFilter.value ?? "").Trim().ToLowerInvariant() : "";
            _bmScroll.Clear();
            var groups = _bmItems
                .Where(it => filter.Length == 0 ||
                    (Path.GetFileName(it.Path) + " " + it.Path + " " + it.Preview).ToLowerInvariant().Contains(filter))
                .GroupBy(it => it.Path)
                .OrderBy(g => Path.GetFileName(g.Key), System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key, System.StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                string path = g.Key;
                var fold = new Foldout
                {
                    text = Path.GetFileName(path),
                    value = !(_bmCollapsed.TryGetValue(path, out bool collapsed) && collapsed),
                    tooltip = path,
                };
                var title = fold.Q<Toggle>();
                if (title != null) title.style.unityFontStyleAndWeight = FontStyle.Bold;
                fold.RegisterValueChangedCallback(e =>
                { if (e.target == fold) _bmCollapsed[path] = !e.newValue; });
                int row = 0;
                foreach (var item in g.OrderBy(x => x.Line))
                {
                    var it = item;
                    var l = new Label("L" + (it.Line + 1) + ":  " + it.Preview)
                    { tooltip = it.Path + ":" + (it.Line + 1) };
                    l.AddToClassList("code-line");
                    l.style.paddingTop = 1;
                    l.style.paddingBottom = 1;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.style.overflow = Overflow.Hidden;
                    int rowIdx = row;
                    AteViewStyle.Zebra(l, rowIdx);
                    l.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0) return;
                        JumpToLocation(it);
                        e.StopPropagation();
                    });
                    l.RegisterCallback<MouseEnterEvent>(_ =>
                        l.style.backgroundColor = AteViewStyle.HoverRow);
                    l.RegisterCallback<MouseLeaveEvent>(_ =>
                        AteViewStyle.Zebra(l, rowIdx));
                    fold.Add(l);
                    row++;
                }
                _bmScroll.Add(fold);
            }
        }
    }
}
#endif
