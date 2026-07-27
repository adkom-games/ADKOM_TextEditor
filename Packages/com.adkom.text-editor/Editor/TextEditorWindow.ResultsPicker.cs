#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Results picker: a Quick-Open-style centered overlay listing locations
    // (references, search hits). Up/Down navigate, Enter or click jumps to
    // the location (opening the file if needed), Escape dismisses.
    public partial class TextEditorWindow
    {
        internal struct PickLocation
        {
            public string Path;    // absolute file path
            public int Line, Col;  // 0-based
            public string Preview; // trimmed line text
        }

        VisualElement _rpOverlay;
        Label _rpTitle;
        ScrollView _rpList;
        readonly List<Label> _rpLabels = new List<Label>();
        List<PickLocation> _rpItems = new List<PickLocation>();
        int _rpSel;

        internal void ShowResultsPicker(string title, List<PickLocation> items)
        {
            EnsureResultsPickerUi();
            _rpItems = items ?? new List<PickLocation>();
            _rpSel = 0;
            _rpTitle.text = title;
            PaintResultsPicker();
            _rpOverlay.style.display = DisplayStyle.Flex;
            _rpOverlay.schedule.Execute(() => _rpOverlay.Focus()).ExecuteLater(0);
        }

        void HideResultsPicker()
        {
            if (_rpOverlay != null) _rpOverlay.style.display = DisplayStyle.None;
            _code?.schedule.Execute(() => _code.Focus()).ExecuteLater(0);
        }

        void EnsureResultsPickerUi()
        {
            if (_rpOverlay != null) return;
            _rpOverlay = new VisualElement { name = "results-picker", focusable = true };
            _rpOverlay.style.position = Position.Absolute;
            _rpOverlay.style.top = 60;
            _rpOverlay.style.left = Length.Percent(15);
            _rpOverlay.style.right = Length.Percent(15);
            _rpOverlay.style.maxHeight = 380;
            _rpOverlay.style.backgroundColor = new Color(0.13f, 0.13f, 0.14f, 0.98f);
            var border = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            _rpOverlay.style.borderLeftWidth = _rpOverlay.style.borderRightWidth = 1;
            _rpOverlay.style.borderTopWidth = _rpOverlay.style.borderBottomWidth = 1;
            _rpOverlay.style.borderLeftColor = _rpOverlay.style.borderRightColor = border;
            _rpOverlay.style.borderTopColor = _rpOverlay.style.borderBottomColor = border;
            _rpOverlay.style.paddingLeft = _rpOverlay.style.paddingRight = 8;
            _rpOverlay.style.paddingTop = 6;
            _rpOverlay.style.paddingBottom = 6;
            _rpOverlay.style.display = DisplayStyle.None;

            _rpTitle = new Label();
            _rpTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _rpTitle.style.marginBottom = 4;
            _rpOverlay.Add(_rpTitle);

            _rpList = new ScrollView(ScrollViewMode.Vertical);
            _rpOverlay.Add(_rpList);

            _rpOverlay.RegisterCallback<KeyDownEvent>(e =>
            {
                switch (e.keyCode)
                {
                    case KeyCode.Escape: HideResultsPicker(); e.StopPropagation(); break;
                    case KeyCode.DownArrow:
                        _rpSel = Mathf.Min(_rpSel + 1, _rpItems.Count - 1);
                        PaintResultsPicker(); e.StopPropagation(); break;
                    case KeyCode.UpArrow:
                        _rpSel = Mathf.Max(_rpSel - 1, 0);
                        PaintResultsPicker(); e.StopPropagation(); break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        AcceptResultsPicker(); e.StopPropagation(); break;
                }
            }, TrickleDown.TrickleDown);
            rootVisualElement.Add(_rpOverlay);
        }

        void AcceptResultsPicker()
        {
            if (_rpSel < 0 || _rpSel >= _rpItems.Count) { HideResultsPicker(); return; }
            var it = _rpItems[_rpSel];
            HideResultsPicker();
            PushNavLocation();
            OpenExternal(it.Path, it.Line + 1, it.Col + 1); // opens the tab if needed
        }

        void PaintResultsPicker()
        {
            for (int i = 0; i < _rpItems.Count; i++)
            {
                if (i >= _rpLabels.Count)
                {
                    var l = new Label();
                    l.style.paddingTop = 2;
                    l.style.paddingBottom = 2;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    int idx = i;
                    l.RegisterCallback<PointerDownEvent>(e =>
                    {
                        _rpSel = idx;
                        AcceptResultsPicker();
                        e.StopPropagation();
                    });
                    _rpList.Add(l);
                    _rpLabels.Add(l);
                }
                var it = _rpItems[i];
                _rpLabels[i].text = Path.GetFileName(it.Path) + ":" + (it.Line + 1) + "   " + it.Preview;
                _rpLabels[i].tooltip = it.Path;
                _rpLabels[i].style.display = DisplayStyle.Flex;
                _rpLabels[i].style.backgroundColor = i == _rpSel
                    ? new Color(0.25f, 0.42f, 0.6f, 0.6f) : Color.clear;
            }
            for (int i = _rpItems.Count; i < _rpLabels.Count; i++)
                _rpLabels[i].style.display = DisplayStyle.None;
            if (_rpSel >= 0 && _rpSel < _rpLabels.Count)
                _rpList.ScrollTo(_rpLabels[_rpSel]);
        }
    }
}
#endif
