#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Autocomplete (must-have #5) + IntelliSense. The popup blends two
    // sources: compiler-accurate symbols from the semantics module when it is
    // available (members after '.', scope symbols otherwise — one background
    // query per word, filtered locally as the prefix grows), and word tokens
    // harvested from the document / other open tabs / language keywords.
    // Ctrl+Space opens it explicitly; it also appears while typing word
    // characters once the prefix is 2+ chars, and immediately after '.' when
    // semantics are on. Up/Down navigate, Enter/Tab accept, Escape dismisses.
    public partial class CodeView
    {
        VisualElement _acPopup;
        ScrollView _acScroll;
        readonly List<Label> _acLabels = new List<Label>();
        readonly List<CompletionItem> _acItems = new List<CompletionItem>();
        int _acSel;
        int _acWordStartIdx;
        const int AcMaxItems = 50;
        const int AcVisibleRows = 8;

        /// <summary>Window hook: raw texts of the OTHER open documents so
        /// completions can come from every tab, not just this one.</summary>
        internal Func<IEnumerable<string>> completionTextSources;

        /// <summary>Window hook: asynchronous semantic completions
        /// (IntelliSense). Wired only when Semantic Features are enabled and
        /// the document is C#; the callback arrives on the main thread.</summary>
        internal Action<int, Action<List<CompletionItem>>> requestSemanticCompletions;

        // One semantic query serves the whole word: cached by word-start index
        // until the popup closes (edits outside the word invalidate offsets).
        List<CompletionItem> _acSem;
        int _acSemStart = -1;

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
            bool afterDot = ws > 0 && line[ws - 1] == '.';
            bool semantic = requestSemanticCompletions != null;
            if (!manual && prefix.Length < 2 && !(afterDot && semantic)) { HideCompletion(); return; }

            int wordStartIdx = LineColToIndex(_caretLine, ws);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var matches = new List<CompletionItem>();

            // Compiler-accurate candidates first. One query per word start —
            // fire it in the background and re-open when the answer lands.
            if (semantic)
            {
                if (_acSemStart == wordStartIdx && _acSem != null)
                {
                    foreach (var it in _acSem)
                    {
                        if (prefix.Length > 0 &&
                            !it.Display.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        if (seen.Add(it.Insert)) matches.Add(it);
                        if (matches.Count >= AcMaxItems * 4) break;
                    }
                }
                else if (_acSemStart != wordStartIdx)
                {
                    _acSemStart = wordStartIdx;
                    _acSem = null;
                    int reqStart = wordStartIdx, reqCaret = cursorIndex;
                    requestSemanticCompletions(reqStart, items =>
                    {
                        if (_acSemStart != reqStart) return; // moved to another word
                        _acSem = items ?? new List<CompletionItem>();
                        // Merge into a live popup — or open one for the '.' case
                        // — but never resurrect a context the user left.
                        if (CompletionVisible || cursorIndex == reqCaret)
                            ShowCompletion(manual: false);
                    });
                }
            }

            // Word-based candidates (skipped after '.', where identifier soup
            // from elsewhere in the file is pure noise).
            if (!afterDot)
            {
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
                            if (w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(w, prefix, StringComparison.Ordinal) && seen.Add(w))
                                matches.Add(new CompletionItem { Insert = w, Display = w, Kind = TokenClass.Default });
                        }
                    }
                }
                // Language keywords of the ACTIVE classifier rank as first-class
                // candidates (C# today; any future language that implements
                // ICompletionKeywords joins automatically).
                if (_classifier is ICompletionKeywords kw)
                    foreach (var k in kw.CompletionKeywords)
                        if (k.Length >= prefix.Length &&
                            k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(k, prefix, StringComparison.Ordinal) && seen.Add(k))
                            matches.Add(new CompletionItem { Insert = k, Display = k, Kind = TokenClass.Keyword });
                Harvest(GetValueInternal());
                if (completionTextSources != null)
                    foreach (var t in completionTextSources())
                    {
                        if (matches.Count >= AcMaxItems * 4) break;
                        Harvest(t);
                    }
            }

            // Best first: case-sensitive prefix beats case-insensitive,
            // shorter beats longer, then ordinal.
            matches.Sort((a, b) =>
            {
                bool ca = a.Display.StartsWith(prefix, StringComparison.Ordinal);
                bool cb = b.Display.StartsWith(prefix, StringComparison.Ordinal);
                if (ca != cb) return ca ? -1 : 1;
                if (a.Display.Length != b.Display.Length) return a.Display.Length - b.Display.Length;
                return string.CompareOrdinal(a.Display, b.Display);
            });
            if (matches.Count > AcMaxItems) matches.RemoveRange(AcMaxItems, matches.Count - AcMaxItems);
            if (matches.Count == 0) { HideCompletionPopupOnly(); return; }

            EnsureAcPopup();
            _acItems.Clear();
            _acItems.AddRange(matches);
            _acSel = 0;
            _acWordStartIdx = wordStartIdx;
            RebuildAcList();

            int sub = SubRowOfCol(_caretLine, _caretCol);
            RowBounds(_caretLine, sub, out int rs, out _);
            _acPopup.style.left = MeasureRange(_caretLine, rs, ws >= rs ? ws : _caretCol);
            _acPopup.style.top = (RowOfLine(_caretLine) + sub + 1) * _lineHeight;
            _acPopup.style.maxHeight = AcVisibleRows * _lineHeight + 8;
            _acPopup.style.display = DisplayStyle.Flex;
        }

        /// <summary>Rich text with literal '&lt;' kept literal (generics).</summary>
        static string AcEsc(string s) =>
            s.IndexOf('<') >= 0 ? "<noparse>" + s + "</noparse>" : s;

        void RebuildAcList()
        {
            for (int i = 0; i < _acItems.Count; i++)
            {
                if (i >= _acLabels.Count)
                {
                    var l = new Label { enableRichText = true };
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
                var it = _acItems[i];
                string color = it.Kind != TokenClass.Default ? _palette?.ColorFor(it.Kind) : null;
                string name = color != null
                    ? "<color=" + color + ">" + AcEsc(it.Display) + "</color>"
                    : AcEsc(it.Display);
                _acLabels[i].text = string.IsNullOrEmpty(it.Detail)
                    ? name
                    : name + "  <color=#8A8A8A>" + AcEsc(it.Detail) + "</color>";
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
            HideCompletionPopupOnly();
            // Any later reopen re-queries: edits outside the popup's lifetime
            // can shift the cached word-start offset.
            _acSemStart = -1;
            _acSem = null;
        }

        // Keeps the semantic cache: used mid-word when the current prefix has
        // no matches yet but the in-flight/ cached query is still valid.
        void HideCompletionPopupOnly()
        {
            if (_acPopup != null) _acPopup.style.display = DisplayStyle.None;
        }

        void AcceptCompletion()
        {
            if (!CompletionVisible || _acSel < 0 || _acSel >= _acItems.Count) return;
            string word = _acItems[_acSel].Insert;
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
