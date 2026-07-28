#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
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
            public string Path;    // absolute file path
            public int Line, Col;  // 0-based
            public string Preview; // trimmed line text
        }

        ScrollView _searchScroll;
        Label _searchHeader;
        readonly List<Label> _searchLabels = new List<Label>();
        List<PickLocation> _searchItems = new List<PickLocation>();

        /// <summary>Fills the Search Results tab and brings it to the front
        /// (showing the console pane if hidden).</summary>
        internal void ShowSearchResults(string title, List<PickLocation> items)
        {
            _searchItems = items ?? new List<PickLocation>();
            _searchHeader.text = title;
            for (int i = 0; i < _searchItems.Count; i++)
            {
                if (i >= _searchLabels.Count)
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
                        JumpToSearchResult(idx);
                        e.StopPropagation();
                    });
                    l.RegisterCallback<MouseEnterEvent>(_ =>
                        l.style.backgroundColor = new Color(0.25f, 0.42f, 0.6f, 0.4f));
                    l.RegisterCallback<MouseLeaveEvent>(_ =>
                        l.style.backgroundColor = Color.clear);
                    _searchScroll.Add(l);
                    _searchLabels.Add(l);
                }
                var it = _searchItems[i];
                _searchLabels[i].text = "  " + Path.GetFileName(it.Path) + ":" + (it.Line + 1) + "   " + it.Preview;
                _searchLabels[i].tooltip = it.Path + ":" + (it.Line + 1);
                _searchLabels[i].style.display = DisplayStyle.Flex;
            }
            for (int i = _searchItems.Count; i < _searchLabels.Count; i++)
                _searchLabels[i].style.display = DisplayStyle.None;
            SetConsoleVisible(true);
            SelectConsoleTab(1);
        }

        void JumpToSearchResult(int idx)
        {
            if (idx < 0 || idx >= _searchItems.Count) return;
            var it = _searchItems[idx];
            PushNavLocation();
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
                        l.style.backgroundColor = new Color(0.25f, 0.42f, 0.6f, 0.4f));
                    l.RegisterCallback<MouseLeaveEvent>(_ =>
                        l.style.backgroundColor = Color.clear);
                    _scannerScroll.Add(l);
                    _scannerLabels.Add(l);
                }
                var it = _scannerItems[i];
                _scannerLabels[i].text = "  " + Path.GetFileName(it.Path) + ":" + (it.Line + 1) + "   " + it.Preview;
                _scannerLabels[i].tooltip = it.Path + ":" + (it.Line + 1);
                _scannerLabels[i].style.display = DisplayStyle.Flex;
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
    }
}
#endif
