#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Quick Open (must-have #17): a centered overlay with a filter field and
    // a result list — open tabs first, then recent files, then every text
    // file under Assets/ and the embedded packages (cached per session).
    // Enter/click opens, Up/Down navigate, Escape dismisses.
    public partial class TextEditorWindow
    {
        VisualElement _qoOverlay;
        TextField _qoField;
        ScrollView _qoList;
        readonly List<Label> _qoLabels = new List<Label>();
        readonly List<string> _qoResults = new List<string>();
        int _qoSel;
        static List<string> _qoProjectFiles; // cached; refreshed per open
        const int QoMaxResults = 30;

        static readonly string[] QoExtensions =
        {
            ".cs", ".md", ".txt", ".json", ".xml", ".yaml", ".yml", ".shader",
            ".uss", ".uxml", ".asmdef", ".cginc", ".hlsl", ".ini", ".cfg"
        };

        internal void ShowQuickOpen()
        {
            EnsureQuickOpenUi();
            _qoProjectFiles = null; // refresh the scan lazily on first filter
            _qoField.SetValueWithoutNotify(string.Empty);
            RefreshQuickOpen(string.Empty);
            _qoOverlay.style.display = DisplayStyle.Flex;
            _qoField.schedule.Execute(() => _qoField.Focus()).ExecuteLater(0);
        }

        void HideQuickOpen()
        {
            if (_qoOverlay != null) _qoOverlay.style.display = DisplayStyle.None;
            _code?.schedule.Execute(() => _code.Focus()).ExecuteLater(0);
        }

        void EnsureQuickOpenUi()
        {
            if (_qoOverlay != null) return;
            _qoOverlay = new VisualElement { name = "quick-open" };
            _qoOverlay.style.position = Position.Absolute;
            _qoOverlay.style.top = 40;
            _qoOverlay.style.left = Length.Percent(20);
            _qoOverlay.style.right = Length.Percent(20);
            _qoOverlay.style.maxHeight = 320;
            _qoOverlay.style.backgroundColor = new Color(0.13f, 0.13f, 0.14f, 0.98f);
            var border = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            _qoOverlay.style.borderLeftWidth = _qoOverlay.style.borderRightWidth = 1;
            _qoOverlay.style.borderTopWidth = _qoOverlay.style.borderBottomWidth = 1;
            _qoOverlay.style.borderLeftColor = _qoOverlay.style.borderRightColor = border;
            _qoOverlay.style.borderTopColor = _qoOverlay.style.borderBottomColor = border;
            _qoOverlay.style.paddingLeft = _qoOverlay.style.paddingRight = 6;
            _qoOverlay.style.paddingTop = _qoOverlay.style.paddingBottom = 6;
            _qoOverlay.style.display = DisplayStyle.None;

            _qoField = new TextField { tooltip = L10n.Tr("Type to filter project files; Enter opens the selected one, Escape closes.") };
            _qoField.RegisterValueChangedCallback(e => RefreshQuickOpen(e.newValue));
            _qoField.RegisterCallback<KeyDownEvent>(e =>
            {
                switch (e.keyCode)
                {
                    case KeyCode.Escape: HideQuickOpen(); e.StopPropagation(); break;
                    case KeyCode.DownArrow:
                        _qoSel = Mathf.Min(_qoSel + 1, _qoResults.Count - 1);
                        PaintQuickOpen();
                        e.StopPropagation();
                        break;
                    case KeyCode.UpArrow:
                        _qoSel = Mathf.Max(_qoSel - 1, 0);
                        PaintQuickOpen();
                        e.StopPropagation();
                        break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        AcceptQuickOpen();
                        e.StopPropagation();
                        break;
                }
            }, TrickleDown.TrickleDown);
            _qoOverlay.Add(_qoField);

            _qoList = new ScrollView(ScrollViewMode.Vertical);
            _qoOverlay.Add(_qoList);
            rootVisualElement.Add(_qoOverlay);
        }

        void AcceptQuickOpen()
        {
            if (_qoSel < 0 || _qoSel >= _qoResults.Count) { HideQuickOpen(); return; }
            string pick = _qoResults[_qoSel];
            HideQuickOpen();
            PushNavLocation();
            OpenPath(pick);
        }

        static List<string> ScanProjectFiles()
        {
            var list = new List<string>();
            string root = Path.GetDirectoryName(Application.dataPath);
            void Scan(string dir)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (System.Array.IndexOf(QoExtensions, ext) >= 0) list.Add(f);
                    }
                    foreach (var d in Directory.GetDirectories(dir))
                    {
                        string name = Path.GetFileName(d);
                        if (name.StartsWith(".") || name == "Library" || name == "Temp" ||
                            name == "Logs" || name == "obj") continue;
                        Scan(d);
                        if (list.Count > 20000) return; // sanity ceiling
                    }
                }
                catch (System.Exception) { }
            }
            Scan(Path.Combine(root, "Assets"));
            Scan(Path.Combine(root, "Packages"));
            return list;
        }

        void RefreshQuickOpen(string filter)
        {
            _qoResults.Clear();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            void Consider(string path)
            {
                if (_qoResults.Count >= QoMaxResults || string.IsNullOrEmpty(path)) return;
                string name = Path.GetFileName(path);
                if (filter.Length > 0 &&
                    name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    path.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    return;
                if (seen.Add(Path.GetFullPath(path))) _qoResults.Add(Path.GetFullPath(path));
            }
            foreach (var d in _docs)
                if (d.HasFile) Consider(d.FilePath);
            foreach (var p in EditorConfig.RecentFiles) Consider(p);
            if (filter.Length > 0) // full scan only once a filter narrows it
            {
                if (_qoProjectFiles == null) _qoProjectFiles = ScanProjectFiles();
                foreach (var p in _qoProjectFiles) Consider(p);
            }
            _qoSel = 0;
            PaintQuickOpen();
        }

        void PaintQuickOpen()
        {
            for (int i = 0; i < _qoResults.Count; i++)
            {
                if (i >= _qoLabels.Count)
                {
                    var l = new Label();
                    l.style.paddingTop = 2;
                    l.style.paddingBottom = 2;
                    int idx = i;
                    l.RegisterCallback<PointerDownEvent>(e =>
                    {
                        _qoSel = idx;
                        AcceptQuickOpen();
                        e.StopPropagation();
                    });
                    _qoList.Add(l);
                    _qoLabels.Add(l);
                }
                string full = _qoResults[i];
                _qoLabels[i].text = Path.GetFileName(full) + "   —  " + full;
                _qoLabels[i].style.display = DisplayStyle.Flex;
                _qoLabels[i].style.backgroundColor = i == _qoSel
                    ? new Color(0.25f, 0.42f, 0.6f, 0.6f) : Color.clear;
            }
            for (int i = _qoResults.Count; i < _qoLabels.Count; i++)
                _qoLabels[i].style.display = DisplayStyle.None;
        }
    }
}
#endif
