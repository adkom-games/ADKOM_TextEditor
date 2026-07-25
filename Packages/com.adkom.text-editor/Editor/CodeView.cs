using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Virtualized code editor view. The document is stored as lines and only
    /// the visible rows are rendered (one pooled Label each, colored per line
    /// by the formatter), so keystroke cost is independent of file size.
    /// Caret, selection, mouse, keyboard, clipboard, and undo are implemented
    /// here. Exposes a TextField-like surface (value / cursorIndex /
    /// selectIndex) so command code can treat it like a text field.
    /// Word wrap splits lines into visual rows at self-computed break points
    /// (per-character width table), so rendering and caret math always agree.
    /// </summary>
    public class CodeView : VisualElement
    {
        const float CaretWidth = 1.5f;
        const int UndoCap = 100;
        const float WrapPad = 6f;

        readonly List<string> _lines = new List<string> { string.Empty };
        string _cachedValue = string.Empty;
        bool _cacheValid = true;

        ITextFormatter _formatter;
        string[] _richLines; // null => render plain

        // Word wrap layout: per line, the columns where new visual rows start
        // (null = single row); _rowStarts[i] = first visual row of line i.
        bool _wordWrap;
        List<int>[] _breaks;
        int[] _rowStarts;
        int _totalRows;
        float _wrapWidth = -1;
        readonly Dictionary<char, float> _charW = new Dictionary<char, float>();

        // Caret/selection in (line, col). Anchor is the selection's fixed end.
        int _caretLine, _caretCol, _anchorLine, _anchorCol;
        int _preferredCol = -1;

        float _lineHeight = 16f;
        float _contentWidth = 200f;

        readonly ScrollView _scroll;
        readonly VisualElement _content;
        readonly List<Label> _linePool = new List<Label>();
        readonly List<VisualElement> _selPool = new List<VisualElement>();
        readonly VisualElement _caret;
        readonly Label _measure;
        readonly Label _gutterLabel;
        readonly VisualElement _gutterCol;
        IVisualElementScheduledItem _blink;
        bool _blinkOn = true;
        bool _dragging;

        Color _textColor = Color.white;
        Color _selectionColor = new Color(0.2f, 0.4f, 0.8f, 0.5f);

        struct Snapshot { public string text; public int cursor, select; }
        readonly List<Snapshot> _undo = new List<Snapshot>();
        readonly List<Snapshot> _redo = new List<Snapshot>();
        double _lastEditTime;
        bool _lastEditWasTyping;

        public event Action<string> onValueChanged;
        public int TabSize = 4;

        public bool wordWrap
        {
            get => _wordWrap;
            set
            {
                if (_wordWrap == value) return;
                _wordWrap = value;
                _scroll.horizontalScrollerVisibility =
                    value ? ScrollerVisibility.Hidden : ScrollerVisibility.Auto;
                if (value) _scroll.horizontalScroller.value = 0;
                RecomputeWrap();
                RefreshVisible();
            }
        }

        public CodeView()
        {
            focusable = true;
            tabIndex = 0;
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Row;
            style.overflow = Overflow.Hidden;

            _gutterCol = new VisualElement { name = "code-gutter" };
            _gutterCol.style.minWidth = 44;
            _gutterCol.style.flexShrink = 0;
            _gutterCol.style.overflow = Overflow.Hidden;
            _gutterCol.style.display = DisplayStyle.None;
            _gutterLabel = new Label();
            _gutterLabel.AddToClassList("code-line");
            _gutterLabel.style.position = Position.Absolute;
            _gutterLabel.style.right = 6;
            _gutterLabel.style.unityTextAlign = TextAnchor.UpperRight;
            _gutterLabel.style.opacity = 0.55f;
            _gutterCol.Add(_gutterLabel);
            Add(_gutterCol);

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scroll.style.flexGrow = 1;
            _content = new VisualElement { name = "code-content" };
            _content.style.position = Position.Relative;
            _scroll.Add(_content);
            Add(_scroll);

            _caret = new VisualElement { name = "code-caret" };
            _caret.style.position = Position.Absolute;
            _caret.style.width = CaretWidth;
            _caret.pickingMode = PickingMode.Ignore;
            _content.Add(_caret);

            _measure = new Label();
            _measure.AddToClassList("code-line");
            _measure.style.position = Position.Absolute;
            _measure.style.visibility = Visibility.Hidden;
            _content.Add(_measure);

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<FocusInEvent>(_ => StartBlink());
            RegisterCallback<FocusOutEvent>(_ => StopBlink());
            RegisterCallback<GeometryChangedEvent>(_ => OnViewportChanged());
            RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);
            // Tab must edit text, never move focus: the focus controller acts
            // on synthesized navigation events that KeyDownEvent.PreventDefault
            // does not stop, so consume them here.
            RegisterCallback<NavigationMoveEvent>(e =>
            {
                e.PreventDefault();
                e.StopPropagation();
            });
            // The viewport gets its size after this element's own geometry
            // event (scrollbars appear/disappear during layout), and the
            // initial fill must react to it or only the first pool of lines
            // renders until the user scrolls.
            _scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => OnViewportChanged());
            _scroll.verticalScroller.valueChanged += _ => RefreshVisible();
            _scroll.horizontalScroller.valueChanged += _ => RefreshVisible();
        }

        void OnViewportChanged()
        {
            RemeasureLineHeight();
            RecomputeWrap();
            RefreshVisible();
        }

        // ---------- Value / caret surface ----------

        public string value
        {
            get => GetValueInternal();
            set { SetValueWithoutNotify(value); Notify(); }
        }

        public void SetValueWithoutNotify(string v)
        {
            v = v ?? string.Empty;
            _lines.Clear();
            int start = 0;
            for (int i = 0; i <= v.Length; i++)
            {
                if (i == v.Length || v[i] == '\n')
                {
                    _lines.Add(v.Substring(start, i - start));
                    start = i + 1;
                }
            }
            _cachedValue = v;
            _cacheValid = true;
            ClampCaret();
            Reformat();
            RecomputeWrap();
            RefreshVisible();
            // Re-fill once post-layout state (viewport size, line height from
            // applied styles) is settled.
            schedule.Execute(OnViewportChanged).ExecuteLater(0);
        }

        string GetValueInternal()
        {
            if (!_cacheValid)
            {
                _cachedValue = string.Join("\n", _lines);
                _cacheValid = true;
            }
            return _cachedValue;
        }

        void Notify() => onValueChanged?.Invoke(GetValueInternal());

        public int cursorIndex
        {
            get => LineColToIndex(_caretLine, _caretCol);
            set { IndexToLineCol(value, out _caretLine, out _caretCol); _preferredCol = -1; AfterCaretMove(); }
        }

        public int selectIndex
        {
            get => LineColToIndex(_anchorLine, _anchorCol);
            set { IndexToLineCol(value, out _anchorLine, out _anchorCol); RefreshVisible(); }
        }

        public int LineColToIndex(int line, int col)
        {
            line = Mathf.Clamp(line, 0, _lines.Count - 1);
            int idx = 0;
            for (int i = 0; i < line; i++) idx += _lines[i].Length + 1;
            return idx + Mathf.Clamp(col, 0, _lines[line].Length);
        }

        public void IndexToLineCol(int index, out int line, out int col)
        {
            index = Mathf.Clamp(index, 0, GetValueInternal().Length);
            int run = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                int len = _lines[i].Length;
                if (index <= run + len) { line = i; col = index - run; return; }
                run += len + 1;
            }
            line = _lines.Count - 1;
            col = _lines[line].Length;
        }

        // ---------- Theme / formatter ----------

        public void SetTheme(Color text, Color background, Color selection)
        {
            _textColor = text;
            _selectionColor = new Color(selection.r, selection.g, selection.b, 0.55f);
            style.backgroundColor = background;
            _gutterCol.style.backgroundColor = background;
            _gutterLabel.style.color = text;
            _caret.style.backgroundColor = text;
            Reformat();
            RefreshVisible();
        }

        public void SetFormatter(ITextFormatter formatter)
        {
            _formatter = formatter is PlainTextFormatter ? null : formatter;
            Reformat();
            RefreshVisible();
        }

        public bool showLineNumbers
        {
            get => _gutterCol.resolvedStyle.display == DisplayStyle.Flex;
            set { _gutterCol.style.display = value ? DisplayStyle.Flex : DisplayStyle.None; RefreshVisible(); }
        }

        void Reformat()
        {
            if (_formatter == null) { _richLines = null; return; }
            string v = GetValueInternal();
            if (v.Length > 400_000) { _richLines = null; return; }
            _richLines = _formatter.Format(v).Split('\n');
        }

        // ---------- Word wrap layout ----------

        float CharWidth(char c)
        {
            if (_charW.TryGetValue(c, out float w)) return w;
            // Measure pairwise so space widths register correctly.
            float two = _measure.MeasureTextSize("|" + c, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
            float one = _measure.MeasureTextSize("|", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
            w = Mathf.Max(1f, two - one);
            _charW[c] = w;
            return w;
        }

        float AvailableWrapWidth() =>
            _scroll.contentViewport.layout.width - WrapPad;

        void RecomputeWrap()
        {
            int n = _lines.Count;
            _rowStarts = new int[n + 1];
            if (!_wordWrap)
            {
                _breaks = null;
                for (int i = 0; i <= n; i++) _rowStarts[i] = i;
                _totalRows = n;
                return;
            }

            float width = AvailableWrapWidth();
            if (float.IsNaN(width) || width < 40) width = 40;
            if (!Mathf.Approximately(width, _wrapWidth)) _wrapWidth = width;

            _breaks = new List<int>[n];
            int row = 0;
            for (int i = 0; i < n; i++)
            {
                _rowStarts[i] = row;
                _breaks[i] = ComputeBreaks(_lines[i], width);
                row += 1 + (_breaks[i]?.Count ?? 0);
            }
            _rowStarts[n] = row;
            _totalRows = row;
        }

        /// <summary>Greedy word wrap: returns the columns where new visual rows
        /// start, or null for a single-row line. Breaks prefer the last space
        /// in the row; a word longer than the width hard-breaks mid-word.</summary>
        List<int> ComputeBreaks(string line, float width)
        {
            if (line.Length == 0) return null;
            List<int> breaks = null;
            float x = 0;
            int rowStart = 0, lastSpace = -1;
            for (int i = 0; i < line.Length; i++)
            {
                x += CharWidth(line[i]);
                if (line[i] == ' ') lastSpace = i;
                if (x > width && i > rowStart)
                {
                    int br = lastSpace > rowStart ? lastSpace + 1 : i;
                    (breaks = breaks ?? new List<int>()).Add(br);
                    rowStart = br;
                    lastSpace = -1;
                    x = 0;
                    for (int j = br; j <= i; j++) x += CharWidth(line[j]);
                }
            }
            return breaks;
        }

        int RowOfLine(int line) => _rowStarts[Mathf.Clamp(line, 0, _lines.Count - 1)];

        int SubRowOfCol(int line, int col)
        {
            var br = _breaks?[line];
            if (br == null) return 0;
            int sub = 0;
            for (int i = 0; i < br.Count && col >= br[i]; i++) sub++;
            return sub;
        }

        void RowBounds(int line, int sub, out int startCol, out int endCol)
        {
            var br = _breaks?[line];
            startCol = sub == 0 || br == null ? 0 : br[sub - 1];
            endCol = br != null && sub < br.Count ? br[sub] : _lines[line].Length;
        }

        void RowToLineSub(int row, out int line, out int sub)
        {
            row = Mathf.Clamp(row, 0, Mathf.Max(0, _totalRows - 1));
            int lo = 0, hi = _lines.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_rowStarts[mid] <= row) lo = mid; else hi = mid - 1;
            }
            line = lo;
            sub = row - _rowStarts[lo];
        }

        int CaretRow() => RowOfLine(_caretLine) + SubRowOfCol(_caretLine, _caretCol);

        // ---------- Rendering ----------

        void RemeasureLineHeight()
        {
            var s = _measure.MeasureTextSize("Wg", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined);
            if (s.y > 1) _lineHeight = s.y;
        }

        float MeasureRange(int line, int fromCol, int toCol)
        {
            if (toCol <= fromCol) return 0;
            string text = _lines[line];
            toCol = Mathf.Min(toCol, text.Length);
            fromCol = Mathf.Min(fromCol, toCol);
            return _measure.MeasureTextSize(text.Substring(fromCol, toCol - fromCol),
                0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
        }

        int ColForXInRow(int line, int sub, float x)
        {
            RowBounds(line, sub, out int startCol, out int endCol);
            if (x <= 0 || endCol == startCol) return startCol;
            int lo = startCol, hi = endCol;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (MeasureRange(line, startCol, mid) <= x) lo = mid; else hi = mid - 1;
            }
            if (lo < endCol)
            {
                float wLo = MeasureRange(line, startCol, lo), wHi = MeasureRange(line, startCol, lo + 1);
                if (x - wLo > (wHi - wLo) * 0.5f) lo++;
            }
            return lo;
        }

        public void RefreshVisible()
        {
            if (float.IsNaN(_scroll.contentViewport.layout.height)) return;
            if (_rowStarts == null || _rowStarts.Length != _lines.Count + 1) RecomputeWrap();

            float viewH = _scroll.contentViewport.layout.height;
            float scrollY = _scroll.verticalScroller.value;
            int firstRow = Mathf.Max(0, (int)(scrollY / _lineHeight));
            int visible = Mathf.Min(_totalRows - firstRow, (int)(viewH / _lineHeight) + 2);
            if (visible < 0) visible = 0;

            _content.style.height = _totalRows * _lineHeight;
            _content.style.minWidth = _wordWrap ? 0 : _contentWidth;
            if (_wordWrap) _content.style.width = AvailableWrapWidth();
            else _content.style.width = StyleKeyword.Auto;

            while (_linePool.Count < visible)
            {
                var l = new Label { enableRichText = true };
                l.AddToClassList("code-line");
                l.style.position = Position.Absolute;
                l.pickingMode = PickingMode.Ignore;
                _content.Add(l);
                _linePool.Add(l);
            }

            float widest = _contentWidth;
            for (int k = 0; k < _linePool.Count; k++)
            {
                var label = _linePool[k];
                int row = firstRow + k;
                if (k >= visible || row >= _totalRows)
                {
                    label.style.display = DisplayStyle.None;
                    continue;
                }
                RowToLineSub(row, out int line, out int sub);
                RowBounds(line, sub, out int sc, out int ec);
                label.style.display = DisplayStyle.Flex;
                label.style.top = row * _lineHeight;
                label.style.left = 0;
                label.style.color = _textColor;
                bool rich = _richLines != null && line < _richLines.Length;
                label.enableRichText = rich;
                if (rich)
                {
                    var br = _breaks?[line];
                    label.text = br == null ? _richLines[line]
                        : MarkupSlice(_richLines[line], sc, ec);
                }
                else label.text = _lines[line].Substring(sc, ec - sc);

                if (!_wordWrap)
                {
                    float w = MeasureRange(line, 0, _lines[line].Length) + 60;
                    if (w > widest) widest = w;
                }
            }
            if (!_wordWrap && widest > _contentWidth)
            {
                _contentWidth = widest;
                _content.style.minWidth = _contentWidth;
            }

            RefreshGutter(firstRow, visible, scrollY);
            RefreshSelection(firstRow, visible);
            RefreshCaret();
        }

        /// <summary>Slices a rich-text line's markup to the plain-char range
        /// [fromCol, toCol), reopening color/noparse state at the slice start.
        /// Only tags this package's formatters emit are understood.</summary>
        static string MarkupSlice(string markup, int fromCol, int toCol)
        {
            var sb = new StringBuilder();
            string openColor = null;
            bool inNoparse = false;
            int plain = 0;
            bool started = false;

            for (int i = 0; i < markup.Length;)
            {
                if (inNoparse && string.CompareOrdinal(markup, i, "</noparse>", 0, 10) == 0)
                {
                    if (started && plain < toCol) sb.Append("</noparse>");
                    inNoparse = false;
                    i += 10;
                    continue;
                }
                if (!inNoparse && markup[i] == '<')
                {
                    int close = markup.IndexOf('>', i);
                    if (close < 0) close = markup.Length - 1;
                    string tag = markup.Substring(i, close - i + 1);
                    if (tag.StartsWith("<color", StringComparison.Ordinal)) openColor = tag;
                    else if (tag == "</color>") openColor = null;
                    else if (tag == "<noparse>") inNoparse = true;
                    if (started && plain < toCol)
                    {
                        sb.Append(tag);
                    }
                    i = close + 1;
                    continue;
                }

                if (plain >= toCol) break;
                if (plain >= fromCol)
                {
                    if (!started)
                    {
                        started = true;
                        if (openColor != null) sb.Append(openColor);
                        if (inNoparse) sb.Append("<noparse>");
                    }
                    sb.Append(markup[i]);
                }
                plain++;
                i++;
            }
            if (started)
            {
                if (inNoparse) sb.Append("</noparse>");
                if (openColor != null) sb.Append("</color>");
            }
            return sb.ToString();
        }

        void RefreshGutter(int firstRow, int visible, float scrollY)
        {
            if (_gutterCol.resolvedStyle.display == DisplayStyle.None) return;
            var sb = new StringBuilder(visible * 5);
            for (int i = 0; i < visible; i++)
            {
                RowToLineSub(firstRow + i, out int line, out int sub);
                if (sub == 0) sb.Append(line + 1);
                sb.Append('\n');
            }
            if (sb.Length > 0) sb.Length--;
            _gutterLabel.text = sb.ToString();
            _gutterLabel.style.top = firstRow * _lineHeight - scrollY;
            int digits = Mathf.Max(3, (_lines.Count + 1).ToString().Length);
            _gutterCol.style.minWidth = 14 + digits * 8;
        }

        void RefreshSelection(int firstRow, int visible)
        {
            NormalizedSelection(out int sl, out int sc, out int el, out int ec);
            int quad = 0;
            bool has = !(sl == el && sc == ec);
            if (has)
            {
                int rowFrom = Mathf.Max(RowOfLine(sl) + SubRowOfCol(sl, sc), firstRow);
                int rowTo = Mathf.Min(RowOfLine(el) + SubRowOfCol(el, ec), firstRow + visible - 1);
                for (int row = rowFrom; row <= rowTo; row++)
                {
                    RowToLineSub(row, out int line, out int sub);
                    RowBounds(line, sub, out int rs, out int re);
                    int selStart = line == sl ? Mathf.Max(sc, rs) : rs;
                    int selEnd = line == el ? Mathf.Min(ec, re) : re;
                    if (selEnd < selStart) continue;
                    float x0 = MeasureRange(line, rs, selStart);
                    float x1 = MeasureRange(line, rs, selEnd);
                    if (selEnd == re && (line < el || (line == el && re < ec))) x1 += 6; // show newline/continuation
                    if (quad >= _selPool.Count)
                    {
                        var q = new VisualElement();
                        q.style.position = Position.Absolute;
                        q.pickingMode = PickingMode.Ignore;
                        _content.Insert(0, q);
                        _selPool.Add(q);
                    }
                    var v = _selPool[quad++];
                    v.style.display = DisplayStyle.Flex;
                    v.style.backgroundColor = _selectionColor;
                    v.style.left = x0;
                    v.style.top = row * _lineHeight;
                    v.style.width = Mathf.Max(2, x1 - x0);
                    v.style.height = _lineHeight;
                }
            }
            for (int i = quad; i < _selPool.Count; i++) _selPool[i].style.display = DisplayStyle.None;
        }

        void RefreshCaret()
        {
            int sub = SubRowOfCol(_caretLine, _caretCol);
            RowBounds(_caretLine, sub, out int rs, out _);
            _caret.style.height = _lineHeight;
            _caret.style.top = (RowOfLine(_caretLine) + sub) * _lineHeight;
            _caret.style.left = MeasureRange(_caretLine, rs, _caretCol);
            _caret.style.display = _blinkOn ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void StartBlink()
        {
            _blinkOn = true;
            if (_blink == null)
                _blink = schedule.Execute(() => { _blinkOn = !_blinkOn; RefreshCaret(); }).Every(530);
            else _blink.Resume();
            RefreshCaret();
        }

        void StopBlink()
        {
            _blink?.Pause();
            _blinkOn = false;
            RefreshCaret();
        }

        public void EnsureCaretVisible()
        {
            float y = CaretRow() * _lineHeight;
            float viewH = _scroll.contentViewport.layout.height;
            float sy = _scroll.verticalScroller.value;
            if (y < sy) _scroll.verticalScroller.value = y;
            else if (y + _lineHeight > sy + viewH) _scroll.verticalScroller.value = y + _lineHeight - viewH;

            if (_wordWrap) return;
            int subN = SubRowOfCol(_caretLine, _caretCol);
            RowBounds(_caretLine, subN, out int rsN, out _);
            float x = MeasureRange(_caretLine, rsN, _caretCol);
            float viewW = _scroll.contentViewport.layout.width;
            float sx = _scroll.horizontalScroller.value;
            if (x < sx + 10) _scroll.horizontalScroller.value = Mathf.Max(0, x - 40);
            else if (x > sx + viewW - 10) _scroll.horizontalScroller.value = x - viewW + 40;
        }

        void AfterCaretMove()
        {
            _blinkOn = true;
            EnsureCaretVisible();
            RefreshVisible();
        }

        // ---------- Selection helpers ----------

        void NormalizedSelection(out int sl, out int sc, out int el, out int ec)
        {
            if (_anchorLine < _caretLine || (_anchorLine == _caretLine && _anchorCol <= _caretCol))
            { sl = _anchorLine; sc = _anchorCol; el = _caretLine; ec = _caretCol; }
            else
            { sl = _caretLine; sc = _caretCol; el = _anchorLine; ec = _anchorCol; }
        }

        bool HasSelection => _anchorLine != _caretLine || _anchorCol != _caretCol;

        void CollapseAnchor() { _anchorLine = _caretLine; _anchorCol = _caretCol; }

        void ClampCaret()
        {
            _caretLine = Mathf.Clamp(_caretLine, 0, _lines.Count - 1);
            _caretCol = Mathf.Clamp(_caretCol, 0, _lines[_caretLine].Length);
            _anchorLine = Mathf.Clamp(_anchorLine, 0, _lines.Count - 1);
            _anchorCol = Mathf.Clamp(_anchorCol, 0, _lines[_anchorLine].Length);
        }

        string SelectedText()
        {
            if (!HasSelection) return string.Empty;
            int s = Mathf.Min(cursorIndex, selectIndex), e = Mathf.Max(cursorIndex, selectIndex);
            return GetValueInternal().Substring(s, e - s);
        }

        // ---------- Editing ----------

        void PushUndo(bool typing)
        {
            double now = EditorApplication.timeSinceStartup;
            if (typing && _lastEditWasTyping && now - _lastEditTime < 1.0)
            {
                _lastEditTime = now;
                return; // coalesce
            }
            _undo.Add(new Snapshot { text = GetValueInternal(), cursor = cursorIndex, select = selectIndex });
            if (_undo.Count > UndoCap) _undo.RemoveAt(0);
            _redo.Clear();
            _lastEditTime = now;
            _lastEditWasTyping = typing;
        }

        public void ReplaceRangeInternal(int start, int end, string replacement, int caret, bool typing)
        {
            PushUndo(typing);
            string v = GetValueInternal();
            start = Mathf.Clamp(start, 0, v.Length);
            end = Mathf.Clamp(end, start, v.Length);
            SetValueWithoutNotify(v.Substring(0, start) + replacement + v.Substring(end));
            cursorIndex = Mathf.Clamp(caret, 0, GetValueInternal().Length);
            CollapseAnchor();
            Notify();
            AfterCaretMove();
        }

        void InsertText(string text, bool typing)
        {
            int s = Mathf.Min(cursorIndex, selectIndex), e = Mathf.Max(cursorIndex, selectIndex);
            ReplaceRangeInternal(s, e, text, s + text.Length, typing);
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var snap = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(new Snapshot { text = GetValueInternal(), cursor = cursorIndex, select = selectIndex });
            SetValueWithoutNotify(snap.text);
            cursorIndex = snap.cursor;
            selectIndex = snap.select;
            _lastEditWasTyping = false;
            Notify();
            AfterCaretMove();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var snap = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(new Snapshot { text = GetValueInternal(), cursor = cursorIndex, select = selectIndex });
            SetValueWithoutNotify(snap.text);
            cursorIndex = snap.cursor;
            selectIndex = snap.select;
            _lastEditWasTyping = false;
            Notify();
            AfterCaretMove();
        }

        // ---------- Input ----------

        void OnKeyDown(KeyDownEvent e)
        {
            bool ctrl = e.ctrlKey || e.commandKey;

            // Character-only events (second event of each key press)
            if (e.keyCode == KeyCode.None && e.character != '\0')
            {
                char c = e.character;
                if (c == '\n' || c == '\r' || c == '\t' || c == 25 /*EM from shift-tab*/)
                { e.StopPropagation(); return; } // handled on keyCode events
                if (!ctrl && c >= ' ')
                {
                    InsertText(c.ToString(), typing: true);
                    e.StopPropagation();
                }
                return;
            }

            bool handled = true;
            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    // Auto-indent: copy the current line's leading spaces.
                    string line = _lines[_caretLine];
                    int indent = 0;
                    while (indent < line.Length && indent < _caretCol && line[indent] == ' ') indent++;
                    InsertText("\n" + new string(' ', indent), typing: true);
                    break;
                }
                case KeyCode.Backspace:
                {
                    if (HasSelection) { InsertText(string.Empty, true); break; }
                    int idx = cursorIndex;
                    if (idx == 0) { handled = true; break; }
                    // Whitespace deletes back to the previous tab stop.
                    int p = _caretCol > 0 ? PrevTabStopInSpaces(_lines[_caretLine], _caretCol) : -1;
                    int remove = p >= 0 ? _caretCol - p : 1;
                    ReplaceRangeInternal(idx - remove, idx, string.Empty, idx - remove, true);
                    break;
                }
                case KeyCode.Delete:
                {
                    if (HasSelection) { InsertText(string.Empty, true); break; }
                    int idx = cursorIndex;
                    if (idx >= GetValueInternal().Length) break;
                    // Whitespace deletes forward to the next tab stop.
                    int nx = NextTabStopInSpaces(_lines[_caretLine], _caretCol);
                    int count = nx >= 0 ? nx - _caretCol : 1;
                    ReplaceRangeInternal(idx, idx + count, string.Empty, idx, true);
                    break;
                }
                case KeyCode.LeftArrow: MoveCaretH(-1, e.shiftKey, ctrl); break;
                case KeyCode.RightArrow: MoveCaretH(1, e.shiftKey, ctrl); break;
                case KeyCode.UpArrow: MoveCaretV(-1, e.shiftKey); break;
                case KeyCode.DownArrow: MoveCaretV(1, e.shiftKey); break;
                case KeyCode.Home:
                {
                    if (ctrl) { _caretLine = 0; _caretCol = 0; }
                    else
                    {
                        // Smart home: toggle between first non-space and col 0.
                        string line = _lines[_caretLine];
                        int ns = 0; while (ns < line.Length && line[ns] == ' ') ns++;
                        _caretCol = _caretCol == ns ? 0 : ns;
                    }
                    _preferredCol = -1;
                    if (!e.shiftKey) CollapseAnchor();
                    AfterCaretMove();
                    break;
                }
                case KeyCode.End:
                {
                    if (ctrl) _caretLine = _lines.Count - 1;
                    _caretCol = _lines[_caretLine].Length;
                    _preferredCol = -1;
                    if (!e.shiftKey) CollapseAnchor();
                    AfterCaretMove();
                    break;
                }
                case KeyCode.PageUp: PageMove(-1, e.shiftKey); break;
                case KeyCode.PageDown: PageMove(1, e.shiftKey); break;
                case KeyCode.A when ctrl:
                    _anchorLine = 0; _anchorCol = 0;
                    _caretLine = _lines.Count - 1; _caretCol = _lines[_caretLine].Length;
                    RefreshVisible();
                    break;
                case KeyCode.C when ctrl: CopySelection(false); break;
                case KeyCode.X when ctrl: CopySelection(true); break;
                case KeyCode.V when ctrl:
                {
                    string clip = EditorGUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(clip))
                        InsertText(clip.Replace("\r\n", "\n").Replace("\r", "\n"), false);
                    break;
                }
                case KeyCode.Z when ctrl && e.shiftKey: Redo(); break;
                case KeyCode.Z when ctrl: Undo(); break;
                case KeyCode.Y when ctrl && EditorConfig.Keymap != KeymapLayout.Rider: Redo(); break;
                default: handled = false; break;
            }
            if (handled)
            {
                e.PreventDefault();
                e.StopPropagation();
            }
        }

        public void Copy() => CopySelection(false);
        public void Cut() => CopySelection(true);

        public void Paste()
        {
            string clip = EditorGUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip))
                InsertText(clip.Replace("\r\n", "\n").Replace("\r", "\n"), false);
        }

        /// <summary>Places the caret at a 1-based line/column (as Unity's
        /// external-editor API supplies) and scrolls it into view.</summary>
        public void GoToLine(int line1Based, int column1Based)
        {
            int line = Mathf.Clamp(line1Based - 1, 0, _lines.Count - 1);
            int col = Mathf.Clamp(column1Based - 1, 0, _lines[line].Length);
            _caretLine = line;
            _caretCol = Mathf.Max(0, col);
            _preferredCol = -1;
            CollapseAnchor();
            AfterCaretMove();
        }

        public void SelectAll()
        {
            _anchorLine = 0; _anchorCol = 0;
            _caretLine = _lines.Count - 1; _caretCol = _lines[_caretLine].Length;
            RefreshVisible();
        }

        public bool HasSelectionPublic => HasSelection;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        void CopySelection(bool cut)
        {
            if (!HasSelection) return;
            EditorGUIUtility.systemCopyBuffer = SelectedText();
            if (cut) InsertText(string.Empty, false);
        }

        /// <summary>Previous tab-stop-aligned column within the whitespace run
        /// behind <paramref name="col"/> (bounded by the run start), or -1 when
        /// the char behind the caret is not a space.</summary>
        int PrevTabStopInSpaces(string line, int col)
        {
            if (col <= 0 || line[col - 1] != ' ') return -1;
            int runStart = col;
            while (runStart > 0 && line[runStart - 1] == ' ') runStart--;
            int stop = ((col - 1) / TabSize) * TabSize;
            return Mathf.Max(stop, runStart);
        }

        /// <summary>Next tab-stop-aligned column within the whitespace run at
        /// <paramref name="col"/> (bounded by the run end), or -1 when the char
        /// at the caret is not a space.</summary>
        int NextTabStopInSpaces(string line, int col)
        {
            if (col >= line.Length || line[col] != ' ') return -1;
            int runEnd = col;
            while (runEnd < line.Length && line[runEnd] == ' ') runEnd++;
            int stop = (col / TabSize + 1) * TabSize;
            return Mathf.Min(stop, runEnd);
        }

        static int PrevWord(string line, int col)
        {
            int i = col;
            while (i > 0 && !char.IsLetterOrDigit(line[i - 1])) i--;
            while (i > 0 && char.IsLetterOrDigit(line[i - 1])) i--;
            return i;
        }

        static int NextWord(string line, int col)
        {
            int i = col;
            while (i < line.Length && !char.IsLetterOrDigit(line[i])) i++;
            while (i < line.Length && char.IsLetterOrDigit(line[i])) i++;
            return i;
        }

        void MoveCaretH(int dir, bool extend, bool wordwise)
        {
            _preferredCol = -1;
            if (!extend && HasSelection)
            {
                NormalizedSelection(out int sl, out int sc, out int el, out int ec);
                if (dir < 0) { _caretLine = sl; _caretCol = sc; }
                else { _caretLine = el; _caretCol = ec; }
                CollapseAnchor();
                AfterCaretMove();
                return;
            }

            if (dir < 0)
            {
                if (_caretCol > 0)
                {
                    if (wordwise) _caretCol = PrevWord(_lines[_caretLine], _caretCol);
                    else
                    {
                        // Tab-stop jump inside any whitespace run.
                        int p = PrevTabStopInSpaces(_lines[_caretLine], _caretCol);
                        _caretCol = p >= 0 ? p : _caretCol - 1;
                    }
                }
                else if (_caretLine > 0) { _caretLine--; _caretCol = _lines[_caretLine].Length; }
            }
            else
            {
                string line = _lines[_caretLine];
                if (_caretCol < line.Length)
                {
                    if (wordwise) _caretCol = NextWord(line, _caretCol);
                    else
                    {
                        // Tab-stop jump inside any whitespace run.
                        int nx = NextTabStopInSpaces(line, _caretCol);
                        _caretCol = nx >= 0 ? nx : _caretCol + 1;
                    }
                }
                else if (_caretLine < _lines.Count - 1) { _caretLine++; _caretCol = 0; }
            }
            if (!extend) CollapseAnchor();
            AfterCaretMove();
        }

        /// <summary>Vertical movement is by VISUAL row so wrapped lines feel
        /// natural; the preferred column is row-relative while wrapping.</summary>
        void MoveCaretV(int dir, bool extend)
        {
            int row = CaretRow();
            RowBounds(_caretLine, SubRowOfCol(_caretLine, _caretCol), out int rs, out _);
            if (_preferredCol < 0) _preferredCol = _caretCol - rs;
            int target = Mathf.Clamp(row + dir, 0, _totalRows - 1);
            RowToLineSub(target, out int line, out int sub);
            RowBounds(line, sub, out int trs, out int tre);
            _caretLine = line;
            _caretCol = Mathf.Min(trs + _preferredCol, tre);
            if (!extend) CollapseAnchor();
            AfterCaretMove();
        }

        void PageMove(int dir, bool extend)
        {
            int page = Mathf.Max(1, (int)(_scroll.contentViewport.layout.height / _lineHeight) - 1);
            int row = CaretRow();
            RowBounds(_caretLine, SubRowOfCol(_caretLine, _caretCol), out int rs, out _);
            if (_preferredCol < 0) _preferredCol = _caretCol - rs;
            int target = Mathf.Clamp(row + dir * page, 0, _totalRows - 1);
            RowToLineSub(target, out int line, out int sub);
            RowBounds(line, sub, out int trs, out int tre);
            _caretLine = line;
            _caretCol = Mathf.Min(trs + _preferredCol, tre);
            if (!extend) CollapseAnchor();
            AfterCaretMove();
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            Focus();
            PlaceCaretAt(e.position, e.shiftKey);
            _dragging = true;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            PlaceCaretAt(e.position, true);
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            this.ReleasePointer(e.pointerId);
        }

        void PlaceCaretAt(Vector2 worldPos, bool extend)
        {
            Vector2 local = _content.WorldToLocal(worldPos);
            int row = Mathf.Clamp((int)(local.y / _lineHeight), 0, Mathf.Max(0, _totalRows - 1));
            RowToLineSub(row, out int line, out int sub);
            _caretLine = line;
            _caretCol = ColForXInRow(line, sub, local.x);
            _preferredCol = -1;
            if (!extend) CollapseAnchor();
            AfterCaretMove();
        }

        void OnValidateCommand(ValidateCommandEvent e)
        {
            switch (e.commandName)
            {
                case "Copy": case "Cut": case "Paste": case "SelectAll":
                case "UndoRedoPerformed":
                    e.StopPropagation();
                    break;
            }
        }

        void OnExecuteCommand(ExecuteCommandEvent e)
        {
            switch (e.commandName)
            {
                case "Copy": CopySelection(false); e.StopPropagation(); break;
                case "Cut": CopySelection(true); e.StopPropagation(); break;
                case "Paste":
                    string clip = EditorGUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(clip))
                        InsertText(clip.Replace("\r\n", "\n").Replace("\r", "\n"), false);
                    e.StopPropagation();
                    break;
                case "SelectAll":
                    _anchorLine = 0; _anchorCol = 0;
                    _caretLine = _lines.Count - 1; _caretCol = _lines[_caretLine].Length;
                    RefreshVisible();
                    e.StopPropagation();
                    break;
            }
        }
    }
}
