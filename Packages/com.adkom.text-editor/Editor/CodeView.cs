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
    /// the visible lines are rendered (one pooled Label each, colored per line
    /// by the formatter), so keystroke cost is independent of file size.
    /// Caret, selection, mouse, keyboard, clipboard, and undo are implemented
    /// here. Exposes a TextField-like surface (value / cursorIndex /
    /// selectIndex) so command code can treat it like a text field.
    /// Word wrap is not supported; long lines scroll horizontally.
    /// </summary>
    public class CodeView : VisualElement
    {
        const float CaretWidth = 1.5f;
        const int UndoCap = 100;

        readonly List<string> _lines = new List<string> { string.Empty };
        string _cachedValue = string.Empty;
        bool _cacheValid = true;

        ITextFormatter _formatter;
        string[] _richLines; // null => render plain

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
            RegisterCallback<GeometryChangedEvent>(_ => { RemeasureLineHeight(); RefreshVisible(); });
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
            _scroll.verticalScroller.valueChanged += _ => RefreshVisible();
            _scroll.horizontalScroller.valueChanged += _ => RefreshVisible();
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
            RefreshVisible();
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

        // ---------- Rendering ----------

        void RemeasureLineHeight()
        {
            var s = _measure.MeasureTextSize("Wg", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined);
            if (s.y > 1) _lineHeight = s.y;
        }

        float MeasureCols(int line, int col)
        {
            if (col <= 0) return 0;
            string text = _lines[line];
            return _measure.MeasureTextSize(text.Substring(0, Mathf.Min(col, text.Length)),
                0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
        }

        int ColForX(int line, float x)
        {
            string text = _lines[line];
            if (x <= 0 || text.Length == 0) return 0;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (MeasureCols(line, mid) <= x) lo = mid; else hi = mid - 1;
            }
            // snap to nearer boundary
            if (lo < text.Length)
            {
                float wLo = MeasureCols(line, lo), wHi = MeasureCols(line, lo + 1);
                if (x - wLo > (wHi - wLo) * 0.5f) lo++;
            }
            return lo;
        }

        public void RefreshVisible()
        {
            if (float.IsNaN(_scroll.contentViewport.layout.height)) return;

            float viewH = _scroll.contentViewport.layout.height;
            float scrollY = _scroll.verticalScroller.value;
            int first = Mathf.Max(0, (int)(scrollY / _lineHeight));
            int visible = Mathf.Min(_lines.Count - first, (int)(viewH / _lineHeight) + 2);

            _content.style.height = _lines.Count * _lineHeight;
            _content.style.minWidth = _contentWidth;

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
                int line = first + k;
                if (k >= visible || line >= _lines.Count)
                {
                    label.style.display = DisplayStyle.None;
                    continue;
                }
                label.style.display = DisplayStyle.Flex;
                label.style.top = line * _lineHeight;
                label.style.left = 0;
                label.style.color = _textColor;
                bool rich = _richLines != null && line < _richLines.Length;
                label.enableRichText = rich;
                label.text = rich ? _richLines[line] : _lines[line];
                float w = _measure.MeasureTextSize(_lines[line], 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x + 60;
                if (w > widest) widest = w;
            }
            if (widest > _contentWidth) { _contentWidth = widest; _content.style.minWidth = _contentWidth; }

            RefreshGutter(first, visible, scrollY);
            RefreshSelection(first, visible);
            RefreshCaret();
        }

        void RefreshGutter(int first, int visible, float scrollY)
        {
            if (_gutterCol.resolvedStyle.display == DisplayStyle.None) return;
            var sb = new StringBuilder(visible * 5);
            for (int i = 0; i < visible; i++) sb.Append(first + i + 1).Append('\n');
            if (sb.Length > 0) sb.Length--;
            _gutterLabel.text = sb.ToString();
            _gutterLabel.style.top = first * _lineHeight - scrollY;
            int digits = Mathf.Max(3, (_lines.Count + 1).ToString().Length);
            _gutterCol.style.minWidth = 14 + digits * 8;
        }

        void RefreshSelection(int first, int visible)
        {
            NormalizedSelection(out int sl, out int sc, out int el, out int ec);
            int quad = 0;
            bool has = !(sl == el && sc == ec);
            if (has)
            {
                for (int line = Mathf.Max(sl, first); line <= Mathf.Min(el, first + visible - 1); line++)
                {
                    float x0 = line == sl ? MeasureCols(line, sc) : 0;
                    float x1 = line == el ? MeasureCols(line, ec)
                        : MeasureCols(line, _lines[line].Length) + 6;
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
                    v.style.top = line * _lineHeight;
                    v.style.width = Mathf.Max(2, x1 - x0);
                    v.style.height = _lineHeight;
                }
            }
            for (int i = quad; i < _selPool.Count; i++) _selPool[i].style.display = DisplayStyle.None;
        }

        void RefreshCaret()
        {
            _caret.style.height = _lineHeight;
            _caret.style.top = _caretLine * _lineHeight;
            _caret.style.left = MeasureCols(_caretLine, _caretCol);
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
            float y = _caretLine * _lineHeight;
            float viewH = _scroll.contentViewport.layout.height;
            float sy = _scroll.verticalScroller.value;
            if (y < sy) _scroll.verticalScroller.value = y;
            else if (y + _lineHeight > sy + viewH) _scroll.verticalScroller.value = y + _lineHeight - viewH;

            float x = MeasureCols(_caretLine, _caretCol);
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
                    int remove = 1;
                    // Un-indent whole tab stops of spaces.
                    string line = _lines[_caretLine];
                    if (_caretCol > 0 && line.Length >= _caretCol)
                    {
                        bool allSpaces = true;
                        for (int i = 0; i < _caretCol; i++) if (line[i] != ' ') { allSpaces = false; break; }
                        if (allSpaces) remove = _caretCol - ((_caretCol - 1) / TabSize) * TabSize;
                    }
                    ReplaceRangeInternal(idx - remove, idx, string.Empty, idx - remove, true);
                    break;
                }
                case KeyCode.Delete:
                {
                    if (HasSelection) { InsertText(string.Empty, true); break; }
                    int idx = cursorIndex;
                    if (idx < GetValueInternal().Length)
                        ReplaceRangeInternal(idx, idx + 1, string.Empty, idx, true);
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

        void CopySelection(bool cut)
        {
            if (!HasSelection) return;
            EditorGUIUtility.systemCopyBuffer = SelectedText();
            if (cut) InsertText(string.Empty, false);
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
                        // Tab-stop jump inside pure-space leading indentation.
                        string line = _lines[_caretLine];
                        bool leading = true;
                        for (int i = 0; i < _caretCol; i++) if (line[i] != ' ') { leading = false; break; }
                        _caretCol = leading ? ((_caretCol - 1) / TabSize) * TabSize : _caretCol - 1;
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
                        bool leading = true;
                        for (int i = 0; i < _caretCol; i++) if (line[i] != ' ') { leading = false; break; }
                        int jump = TabSize - (_caretCol % TabSize);
                        bool spacesAhead = leading && _caretCol + jump <= line.Length;
                        if (spacesAhead)
                            for (int i = 0; i < jump; i++) if (line[_caretCol + i] != ' ') { spacesAhead = false; break; }
                        _caretCol = spacesAhead ? _caretCol + jump : _caretCol + 1;
                    }
                }
                else if (_caretLine < _lines.Count - 1) { _caretLine++; _caretCol = 0; }
            }
            if (!extend) CollapseAnchor();
            AfterCaretMove();
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

        void MoveCaretV(int dir, bool extend)
        {
            if (_preferredCol < 0) _preferredCol = _caretCol;
            int line = Mathf.Clamp(_caretLine + dir, 0, _lines.Count - 1);
            _caretLine = line;
            _caretCol = Mathf.Min(_preferredCol, _lines[line].Length);
            if (!extend) CollapseAnchor();
            AfterCaretMove();
        }

        void PageMove(int dir, bool extend)
        {
            int page = Mathf.Max(1, (int)(_scroll.contentViewport.layout.height / _lineHeight) - 1);
            if (_preferredCol < 0) _preferredCol = _caretCol;
            _caretLine = Mathf.Clamp(_caretLine + dir * page, 0, _lines.Count - 1);
            _caretCol = Mathf.Min(_preferredCol, _lines[_caretLine].Length);
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
            int line = Mathf.Clamp((int)(local.y / _lineHeight), 0, _lines.Count - 1);
            int col = ColForX(line, local.x);
            _caretLine = line;
            _caretCol = col;
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
