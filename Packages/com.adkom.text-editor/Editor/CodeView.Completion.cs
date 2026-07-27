#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Word-based autocomplete (must-have #5): a popup of prefix-matched words
    // harvested from the document (and, via completionTextSources, every
    // other open tab). Ctrl+Space opens it explicitly; it also appears while
    // typing word characters once the prefix is 2+ chars. Up/Down navigate,
    // Enter/Tab accept, Escape dismisses, further typing refines.
    public partial class CodeView
    {
        VisualElement _acPopup;
        ScrollView _acScroll;
        readonly List<Label> _acLabels = new List<Label>();
        readonly List<string> _acItems = new List<string>();
        int _acSel;
        int _acWordStartIdx;
        const int AcMaxItems = 50;
        const int AcVisibleRows = 8;

        /// <summary>Window hook: raw texts of the OTHER open documents so
        /// completions can come from every tab, not just this one.</summary>
        internal Func<IEnumerable<string>> completionTextSources;

        internal bool CompletionVisible =>
            _acPopup != null && _acPopup.style.display == DisplayStyle.Flex;

        void EnsureAcPopup()
        {
            if (_acPopup != null) return;
            _acPopup = new VisualElement { name = "ac-popup" };
            _acPopup.style.position = Position.Absolute;
            _acPopup.style.backgroundColor = new Color(0.13f, 0.13f, 0.14f, 0.98f);
            _acPopup.style.borderLeftWidth = _acPopup.style.borderRightWidth = 1;
            _acPopup.style.borderTopWidth = _acPopup.style.borderBottomWidth = 1;
            var border = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            _acPopup.style.borderLeftColor = _acPopup.style.borderRightColor = border;
            _acPopup.style.borderTopColor = _acPopup.style.borderBottomColor = border;
            _acPopup.style.minWidth = 160;
            _acPopup.style.display = DisplayStyle.None;
            _acPopup.pickingMode = PickingMode.Position;
            _acScroll = new ScrollView(ScrollViewMode.Vertical);
            _acPopup.Add(_acScroll);
            _content.Add(_acPopup); // scrolls with the text; added last = on top
        }

        /// <summary>Opens (or refreshes) the popup for the word prefix at the
        /// caret. Manual (Ctrl+Space) allows an empty prefix.</summary>
        internal void ShowCompletion(bool manual)
        {
            if (HasMultiCarets || HasSelection) { HideCompletion(); return; }
            string line = _lines[_caretLine];
            int ws = _caretCol;
            while (ws > 0 && IsWordCharUndo(line[ws - 1])) ws--;
            string prefix = line.Substring(ws, _caretCol - ws);
            if (!manual && prefix.Length < 2) { HideCompletion(); return; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var matches = new List<string>();
            void Harvest(string text)
            {
                if (string.IsNullOrEmpty(text) || text.Length > 300000) return;
                int i = 0, n = text.Length;
                while (i < n)
                {
                    if (!IsWordCharUndo(text[i])) { i++; continue; }
                    int start = i;
                    while (i < n && IsWordCharUndo(text[i])) i++;
                    if (i - start >= 2 && i - start >= prefix.Length)
                    {
                        string w = text.Substring(start, i - start);
                        if (w.Length > prefix.Length || !w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            if (w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(w, prefix, StringComparison.Ordinal) && seen.Add(w))
                                matches.Add(w);
                        }
                    }
                }
            }
            Harvest(GetValueInternal());
            if (completionTextSources != null)
                foreach (var t in completionTextSources())
                {
                    if (matches.Count >= AcMaxItems * 4) break;
                    Harvest(t);
                }
            // Best first: case-sensitive prefix beats case-insensitive,
            // shorter beats longer, then ordinal.
            matches.Sort((a, b) =>
            {
                bool ca = a.StartsWith(prefix, StringComparison.Ordinal);
                bool cb = b.StartsWith(prefix, StringComparison.Ordinal);
                if (ca != cb) return ca ? -1 : 1;
                if (a.Length != b.Length) return a.Length - b.Length;
                return string.CompareOrdinal(a, b);
            });
            if (matches.Count > AcMaxItems) matches.RemoveRange(AcMaxItems, matches.Count - AcMaxItems);
            if (matches.Count == 0) { HideCompletion(); return; }

            EnsureAcPopup();
            _acItems.Clear();
            _acItems.AddRange(matches);
            _acSel = 0;
            _acWordStartIdx = LineColToIndex(_caretLine, ws);
            RebuildAcList();

            int sub = SubRowOfCol(_caretLine, _caretCol);
            RowBounds(_caretLine, sub, out int rs, out _);
            _acPopup.style.left = MeasureRange(_caretLine, rs, ws >= rs ? ws : _caretCol);
            _acPopup.style.top = (RowOfLine(_caretLine) + sub + 1) * _lineHeight;
            _acPopup.style.maxHeight = AcVisibleRows * _lineHeight + 8;
            _acPopup.style.display = DisplayStyle.Flex;
        }

        void RebuildAcList()
        {
            for (int i = 0; i < _acItems.Count; i++)
            {
                if (i >= _acLabels.Count)
                {
                    var l = new Label();
                    l.AddToClassList("code-line");
                    l.style.paddingLeft = 6;
                    l.style.paddingRight = 6;
                    int idx = i;
                    l.RegisterCallback<PointerDownEvent>(e =>
                    {
                        _acSel = idx;
                        AcceptCompletion();
                        e.StopPropagation();
                    });
                    _acScroll.Add(l);
                    _acLabels.Add(l);
                }
                _acLabels[i].text = _acItems[i];
                _acLabels[i].style.display = DisplayStyle.Flex;
                _acLabels[i].style.color = _textColor;
                _acLabels[i].style.backgroundColor = i == _acSel
                    ? _selectionColor : Color.clear;
            }
            for (int i = _acItems.Count; i < _acLabels.Count; i++)
                _acLabels[i].style.display = DisplayStyle.None;
        }

        internal void HideCompletion()
        {
            if (_acPopup != null) _acPopup.style.display = DisplayStyle.None;
        }

        void AcceptCompletion()
        {
            if (!CompletionVisible || _acSel < 0 || _acSel >= _acItems.Count) return;
            string word = _acItems[_acSel];
            int end = cursorIndex;
            HideCompletion();
            ReplaceRangeInternal(_acWordStartIdx, end, word,
                _acWordStartIdx + word.Length, EditKind.Programmatic);
        }

        /// <summary>Popup keyboard handling; runs before normal key handling.
        /// True = the popup consumed the key.</summary>
        bool HandleCompletionKey(KeyDownEvent e)
        {
            if (!CompletionVisible) return false;
            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                    _acSel = Mathf.Min(_acSel + 1, _acItems.Count - 1);
                    RebuildAcList();
                    _acScroll.ScrollTo(_acLabels[_acSel]);
                    return true;
                case KeyCode.UpArrow:
                    _acSel = Mathf.Max(_acSel - 1, 0);
                    RebuildAcList();
                    _acScroll.ScrollTo(_acLabels[_acSel]);
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Tab:
                    AcceptCompletion();
                    return true;
                case KeyCode.Escape:
                    HideCompletion();
                    return true;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    HideCompletion();
                    return false; // let the caret move normally
                default:
                    return false; // typing/backspace refresh after the edit
            }
        }
    }
}
#endif
