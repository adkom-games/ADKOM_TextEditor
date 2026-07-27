#if UNITY_EDITOR
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
    public partial class CodeView : VisualElement
    {
        const float CaretWidth = 1.5f;
        const int UndoCap = 100;
        const float WrapPad = 6f;

        readonly List<string> _lines = new List<string> { string.Empty };
        string _cachedValue = string.Empty;
        bool _cacheValid = true;

        ISyntaxClassifier _classifier;
        List<SyntaxSpan>[] _lineSpans;   // per-line, sorted by Start; null => plain
        string[] _lineMarkup;            // lazy per-line markup cache
        HighlightTheme.Palette _palette;
        int _docVersion;

        /// <summary>Fires on Ctrl+Click with the (line, col) under the cursor.</summary>
        public event Action<int, int> onNavigateRequest;
        public int DocVersion => _docVersion;
        public string ClassifierName => _classifier?.Name;

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
        readonly List<Label> _gutterPool = new List<Label>();
        readonly VisualElement _gutterCol;
        VisualElement _minimap;
        bool _minimapDragging;
        readonly Dictionary<TokenClass, Color> _minimapColors = new Dictionary<TokenClass, Color>();
        IVisualElementScheduledItem _blink;
        bool _blinkOn = true;
        bool _dragging;

        Color _textColor = Color.white;
        Color _selectionColor = new Color(0.2f, 0.4f, 0.8f, 0.5f);
        Color _matchColor = new Color(0.7f, 0.7f, 0.7f, 0.18f);
        readonly List<VisualElement> _matchPool = new List<VisualElement>();

        // Range-based undo (defect #1): each entry stores only the DELTA of
        // its group — the text the group inserted and the text it replaced —
        // not a full document snapshot, so undo memory scales with edit size,
        // never with file size. Coalescing extends the top op in place.
        internal sealed class UndoOp
        {
            public int Start;                       // group's change origin
            public string Inserted = string.Empty;  // present AFTER the group
            public string Removed = string.Empty;   // present BEFORE the group
            public int CursorBefore, SelectBefore;  // caret to restore on undo
            public int CursorAfter, SelectAfter;    // caret to restore on redo
            public List<UndoSeg> Segments;          // multi-caret op (null = simple)
        }

        /// <summary>One segment of a multi-caret edit; Start is in PRE-edit
        /// coordinates. Undo applies inverses ascending (each lands exactly
        /// at Start), redo applies forward descending.</summary>
        internal sealed class UndoSeg
        {
            public int Start;
            public string Inserted = string.Empty;
            public string Removed = string.Empty;
        }

        /// <summary>The complete per-document undo world: both stacks plus
        /// the coalescing state, swapped by the window on tab switches so
        /// undo history is scoped to its document.</summary>
        internal sealed class UndoWorld
        {
            public readonly List<UndoOp> Undo = new List<UndoOp>();
            public readonly List<UndoOp> Redo = new List<UndoOp>();
            public EditKind LastKind = EditKind.Programmatic;
            public double LastEditTime, GroupStartTime;
            public int LastEditEnd = -1;
            public int GroupChars;
            public char LastTypedChar = '\0';
        }

        UndoWorld _world = new UndoWorld();
        List<UndoOp> _undo => _world.Undo;
        List<UndoOp> _redo => _world.Redo;

        /// <summary>Detaches the current undo world (the caller stores it on
        /// the document) — used when the window swaps documents.</summary>
        internal object DetachUndoWorld()
        {
            SyncGroupStateIntoWorld();
            var w = _world;
            _world = new UndoWorld();
            AttachGroupStateFromWorld();
            return w;
        }

        /// <summary>Attaches a document's undo world (null = fresh).</summary>
        internal void AttachUndoWorld(object world)
        {
            _world = world as UndoWorld ?? new UndoWorld();
            AttachGroupStateFromWorld();
        }

        void SyncGroupStateIntoWorld()
        {
            _world.LastKind = _lastKind;
            _world.LastEditTime = _lastEditTime;
            _world.GroupStartTime = _groupStartTime;
            _world.LastEditEnd = _lastEditEnd;
            _world.GroupChars = _groupChars;
            _world.LastTypedChar = _lastTypedChar;
        }

        void AttachGroupStateFromWorld()
        {
            _lastKind = _world.LastKind;
            _lastEditTime = _world.LastEditTime;
            _groupStartTime = _world.GroupStartTime;
            _lastEditEnd = _world.LastEditEnd;
            _groupChars = _world.GroupChars;
            _lastTypedChar = _world.LastTypedChar;
        }

        /// <summary>What kind of edit an undoable change is. Only TypeChar,
        /// Backspace, and ForwardDelete ever coalesce — each strictly with its
        /// own kind; everything else is one undo unit per edit.</summary>
        internal enum EditKind
        {
            Programmatic, TypeChar, TypeNewline, Backspace,
            ForwardDelete, ReplaceSelection, Paste, LineOp
        }

        // Undo-group state. A group chains same-kind, contiguous edits with
        // short gaps, breaks at word starts, and is hard-capped in size and
        // age so one undo step is always a humanly predictable amount.
        EditKind _lastKind = EditKind.Programmatic;
        double _lastEditTime;
        double _groupStartTime;
        int _lastEditEnd = -1;   // doc offset where the next chained edit must occur
        int _groupChars;
        char _lastTypedChar = '\0';
        const double CoalesceGapSeconds = 0.75; // pause since LAST keystroke breaks
        const double MaxGroupSeconds = 5.0;     // hard age cap per group
        const int MaxGroupChars = 100;          // hard size cap per group

        /// <summary>Forces the next edit to start a new undo group (called on
        /// save and when the window loses focus).</summary>
        internal void BreakUndoGroup()
        {
            _lastKind = EditKind.Programmatic;
            _lastEditEnd = -1;
        }

        /// <summary>Raised after Undo/Redo with a human-readable summary
        /// (e.g. "Undid 12 chars") for the status bar.</summary>
        public event Action<string> onUndoStatus;

        public event Action<string> onValueChanged;

        /// <summary>Window hook: is this line bookmarked? Drives the gutter accent.</summary>
        internal Func<int, bool> isLineBookmarked;

        /// <summary>(afterLine, lineDelta) whenever an edit adds/removes lines
        /// so the window can shift line-anchored state (bookmarks).</summary>
        internal event Action<int, int> onLineDelta;
        public int TabSize = 4;

        // ---------- Game mode + color overlay (AteApi 1.1) ----------

        /// <summary>One colored run on one line. Columns are 0-based, spans
        /// per line are kept sorted and non-overlapping by ColorOverlay.Set.</summary>
        internal struct OvSpan
        {
            public int Start, End;
            public Color Fg, Bg;
            public bool HasFg, HasBg;
        }

        /// <summary>Per-document color overlay, owned by the TextDocument and
        /// attached to the view on tab switch (like the undo world). Purely a
        /// render attribute layer: never part of the text, positional (does
        /// not track edits).</summary>
        internal sealed class ColorOverlay
        {
            public readonly Dictionary<int, List<OvSpan>> Lines = new Dictionary<int, List<OvSpan>>();

            public void Set(int line, int colStart, int colEnd, Color? fg, Color? bg)
            {
                if (colEnd <= colStart || line < 0) return;
                if (!Lines.TryGetValue(line, out var spans))
                {
                    if (fg == null && bg == null) return;
                    Lines[line] = spans = new List<OvSpan>(4);
                }
                // Clip existing spans against the new range so the list stays
                // sorted and non-overlapping (last write wins).
                for (int i = spans.Count - 1; i >= 0; i--)
                {
                    var s = spans[i];
                    if (s.End <= colStart || s.Start >= colEnd) continue;
                    spans.RemoveAt(i);
                    if (s.Start < colStart)
                    {
                        var left = s; left.End = colStart;
                        spans.Insert(i, left);
                        i++; // the right remainder check below may still apply
                    }
                    if (s.End > colEnd)
                    {
                        var right = s; right.Start = colEnd;
                        spans.Insert(FindInsert(spans, right.Start), right);
                    }
                }
                if (fg != null || bg != null)
                {
                    var ns = new OvSpan
                    {
                        Start = colStart,
                        End = colEnd,
                        HasFg = fg != null,
                        HasBg = bg != null,
                        Fg = fg ?? default,
                        Bg = bg ?? default
                    };
                    spans.Insert(FindInsert(spans, colStart), ns);
                }
                if (spans.Count == 0) Lines.Remove(line);
            }

            static int FindInsert(List<OvSpan> spans, int start)
            {
                int i = 0;
                while (i < spans.Count && spans[i].Start < start) i++;
                return i;
            }
        }

        ColorOverlay _overlay;
        readonly List<VisualElement> _ovBgPool = new List<VisualElement>();
        bool _gameMode;

        internal void AttachOverlay(ColorOverlay overlay)
        {
            if (ReferenceEquals(_overlay, overlay)) return;
            _overlay = overlay;
            RefreshVisible();
        }

        /// <summary>Game mode: programmatic writes bypass undo (the stack is
        /// cleared on entry — recorded ops would reference stale offsets),
        /// Undo/Redo are inert, and syntax spans are dropped in favor of the
        /// color overlay.</summary>
        internal bool gameMode
        {
            get => _gameMode;
            set
            {
                if (_gameMode == value) return;
                _gameMode = value;
                if (value)
                {
                    _undo.Clear();
                    _redo.Clear();
                    BreakUndoGroup();
                }
                Reclassify();
                RefreshVisible();
            }
        }

        List<OvSpan> OverlayFor(int line) =>
            _overlay != null && _overlay.Lines.TryGetValue(line, out var s) ? s : null;

        /// <summary>Rich-text markup for line columns [fromCol, toCol) from
        /// the color overlay's foreground runs (the overlay analog of
        /// MarkupForRange; overlay lines bypass the syntax markup cache).</summary>
        string OverlayMarkupForRange(int line, int fromCol, int toCol, List<OvSpan> spans)
        {
            string text = _lines[line];
            toCol = Mathf.Min(toCol, text.Length);
            var sb = new StringBuilder((toCol - fromCol) + 32);
            int pos = fromCol;
            foreach (var s in spans)
            {
                if (!s.HasFg) continue;
                int ss = Mathf.Max(s.Start, fromCol);
                int se = Mathf.Min(s.End, toCol);
                if (se <= ss) continue;
                if (ss > pos) AppendEscaped(sb, text, pos, ss, null);
                AppendEscaped(sb, text, ss, se, "#" + ColorUtility.ToHtmlStringRGBA(s.Fg));
                pos = se;
            }
            if (pos < toCol) AppendEscaped(sb, text, pos, toCol, null);
            return sb.ToString();
        }

        /// <summary>Positions one quad per visible background run, beneath the
        /// text labels (same technique as the selection quads).</summary>
        void RefreshOverlayBg(int firstRow, int visible)
        {
            int quad = 0;
            if (_overlay != null && _overlay.Lines.Count > 0)
            {
                for (int i = 0; i < visible; i++)
                {
                    int row = firstRow + i;
                    if (row >= _totalRows) break;
                    RowToLineSub(row, out int line, out int sub);
                    var spans = OverlayFor(line);
                    if (spans == null) continue;
                    RowBounds(line, sub, out int rs, out int re);
                    foreach (var s in spans)
                    {
                        if (!s.HasBg) continue;
                        int cs = Mathf.Max(s.Start, rs);
                        int ce = Mathf.Min(Mathf.Min(s.End, re), _lines[line].Length);
                        if (ce <= cs) continue;
                        if (quad >= _ovBgPool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Insert(0, q); // beneath selection and text
                            _ovBgPool.Add(q);
                        }
                        var v = _ovBgPool[quad++];
                        v.style.display = DisplayStyle.Flex;
                        v.style.backgroundColor = s.Bg;
                        float x0 = MeasureRange(line, rs, cs);
                        v.style.left = x0;
                        v.style.top = row * _lineHeight;
                        v.style.width = Mathf.Max(1, MeasureRange(line, rs, ce) - x0);
                        v.style.height = _lineHeight;
                    }
                }
            }
            for (int i = quad; i < _ovBgPool.Count; i++)
                _ovBgPool[i].style.display = DisplayStyle.None;
        }

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

        static Font _monoFont;

        /// <summary>The editor's bundled RobotoMono (Console font), falling
        /// back to common OS monospace fonts per platform.</summary>
        static Font MonoFont()
        {
            if (_monoFont != null) return _monoFont;
            _monoFont = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
            if (_monoFont != null) return _monoFont;
            foreach (var name in new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" })
            {
                try
                {
                    _monoFont = Font.CreateDynamicFontFromOSFont(name, 13);
                    if (_monoFont != null) return _monoFont;
                }
                catch { }
            }
            return null;
        }

        static readonly Dictionary<string, Font> _fontCache = new Dictionary<string, Font>();

        /// <summary>Raised when the font size changes via zoom gestures so the
        /// Settings pane can stay in sync.</summary>
        public event Action onFontSizeChanged;

        /// <summary>(Re)applies the configured font family and size, then
        /// invalidates every metric derived from them.</summary>
        public void ApplyFontConfig()
        {
            style.fontSize = EditorConfig.FontSize;
            Font f = null;
            string name = EditorConfig.FontName;
            if (!string.IsNullOrEmpty(name) && !_fontCache.TryGetValue(name, out f))
            {
                try { f = Font.CreateDynamicFontFromOSFont(name, EditorConfig.FontSize); }
                catch { f = null; }
                _fontCache[name] = f;
            }
            if (f == null) f = MonoFont();
            if (f != null) style.unityFontDefinition = FontDefinition.FromFont(f);

            _charW.Clear();
            schedule.Execute(OnViewportChanged).ExecuteLater(0); // metrics after style resolve
        }

        // --- Smooth scrolling: exponential ease toward a wheel-accumulated
        // target; per-notch distance matches the ScrollView's own stepping. ---

        float _smoothTarget;
        bool _smoothActive;
        double _smoothLastTime;
        IVisualElementScheduledItem _smoothAnim;

        void SmoothScrollBy(float pixels)
        {
            var sc = _scroll.verticalScroller;
            if (!_smoothActive) _smoothTarget = sc.value; // re-anchor after drags
            _smoothTarget = Mathf.Clamp(_smoothTarget + pixels, sc.lowValue, sc.highValue);
            _smoothLastTime = EditorApplication.timeSinceStartup;
            _smoothActive = true;
            if (_smoothAnim == null) _smoothAnim = schedule.Execute(SmoothStep).Every(15);
            else _smoothAnim.Resume();
        }

        void SmoothStep()
        {
            var sc = _scroll.verticalScroller;
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min(0.05f, (float)(now - _smoothLastTime));
            _smoothLastTime = now;
            float diff = _smoothTarget - sc.value;
            if (Mathf.Abs(diff) < 0.5f)
            {
                sc.value = _smoothTarget;
                _smoothActive = false;
                _smoothAnim.Pause();
                return;
            }
            // Snap every animation frame to a WHOLE pixel: fractional scroll
            // offsets rasterize the transparent input field and the colored
            // overlay label differently, producing a 1px color-misalignment
            // shimmer during the ease (Defects round 2026-07-27, item 2).
            float next = Mathf.Round(sc.value + diff * (1f - Mathf.Exp(-dt * 14f)));
            if (Mathf.Approximately(next, sc.value)) next += Mathf.Sign(diff); // don't stall short of target
            sc.value = next;
        }

        void ZoomBy(int delta)
        {
            int size = Mathf.Clamp(delta == 0 ? EditorConfig.DefaultFontSize : EditorConfig.FontSize + delta, 8, 40);
            if (size == EditorConfig.FontSize && delta != 0) return;
            EditorConfig.FontSize = size;
            ApplyFontConfig();
            onFontSizeChanged?.Invoke();
        }

        public CodeView()
        {
            focusable = true;
            tabIndex = 0;
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Row;
            style.overflow = Overflow.Hidden;

            // Font family/size inherit to every line label, the gutter, and
            // the measuring label.
            ApplyFontConfig();

            _gutterCol = new VisualElement { name = "code-gutter" };
            _gutterCol.style.minWidth = 44;
            _gutterCol.style.flexShrink = 0;
            _gutterCol.style.overflow = Overflow.Hidden;
            _gutterCol.style.display = DisplayStyle.None;
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
            // Trickle-down wheel handling: Ctrl = zoom (Cmd on macOS); with
            // smooth scrolling on, plain wheel input animates toward the same
            // per-notch distance the ScrollView would jump.
            RegisterCallback<WheelEvent>(e =>
            {
                if (e.ctrlKey || e.commandKey)
                {
                    ZoomBy(e.delta.y < 0 ? 1 : -1);
                    e.StopImmediatePropagation();
                    return;
                }
                if (!EditorConfig.SmoothScrolling) return; // ScrollView steps as usual
                SmoothScrollBy(e.delta.y * _scroll.mouseWheelScrollSize);
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_linkTip != null) _linkTip.style.display = DisplayStyle.None;
            });
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
                focusController?.IgnoreEvent(e);
                e.StopPropagation();
            });
            // The viewport gets its size after this element's own geometry
            // event (scrollbars appear/disappear during layout), and the
            // initial fill must react to it or only the first pool of lines
            // renders until the user scrolls.
            _scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => OnViewportChanged());
            _scroll.verticalScroller.valueChanged += _ => RefreshVisible();
            _scroll.horizontalScroller.valueChanged += _ => RefreshVisible();

            // Minimap: a code-shape overview between the content viewport and
            // the vertical scrollbar, painted with Painter2D (one mesh, not
            // per-line elements). Click/drag jumps the view.
            _minimap = new VisualElement { name = "code-minimap" };
            _minimap.style.width = 90;
            _minimap.style.flexShrink = 0;
            _minimap.generateVisualContent += OnMinimapPaint;
            _minimap.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                _minimapDragging = true;
                _minimap.CapturePointer(e.pointerId);
                MinimapJump(_minimap.WorldToLocal(e.position).y);
                e.StopPropagation();
            });
            _minimap.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_minimapDragging) MinimapJump(_minimap.WorldToLocal(e.position).y);
            });
            _minimap.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_minimapDragging) return;
                _minimapDragging = false;
                _minimap.ReleasePointer(e.pointerId);
            });
            var scrollRow = _scroll.verticalScroller.parent;
            scrollRow.Insert(scrollRow.IndexOf(_scroll.verticalScroller), _minimap);
        }

        void MinimapJump(float localY)
        {
            float h = _minimap.contentRect.height;
            if (h <= 0 || _totalRows == 0) return;
            float mapScale = Mathf.Min(h / _totalRows, 2f); // matches the paint scale
            float mapContentH = _totalRows * mapScale;
            float frac = Mathf.Clamp01(localY / Mathf.Min(h, mapContentH));
            float viewH = _scroll.contentViewport.layout.height;
            float target = frac * _totalRows * _lineHeight - viewH * 0.5f;
            _scroll.verticalScroller.value = Mathf.Clamp(target,
                _scroll.verticalScroller.lowValue, _scroll.verticalScroller.highValue);
        }

        void OnMinimapPaint(MeshGenerationContext ctx)
        {
            float h = _minimap.contentRect.height, w = _minimap.contentRect.width;
            if (h <= 4 || _totalRows == 0) return;
            var p = ctx.painter2D;
            float rowH = Mathf.Min(h / _totalRows, 2f);
            int drawRows = Mathf.Min(_totalRows, Mathf.Max(1, (int)(h / Mathf.Max(rowH, 1f))));
            float step = (float)_totalRows / drawRows;
            // Vertical spacing between SAMPLED rows: each drawn row stands in
            // for `step` real rows, so samples must spread across the whole
            // strip (issue #7 — using rowH alone squished big files upward).
            float ySpacing = rowH * step;
            float charPx = w / 120f; // fit ~120 columns across the strip

            // Colorized: segments follow the syntax spans, batched by color
            // into one fill pass per color.
            var batches = new Dictionary<Color, List<Rect>>();
            Color defaultColor = new Color(_textColor.r, _textColor.g, _textColor.b, 0.35f);
            void Add(Color color, float x, float y, float bw, float bh)
            {
                if (bw < 0.5f) return;
                if (!batches.TryGetValue(color, out var list)) batches[color] = list = new List<Rect>(64);
                list.Add(new Rect(x, y, bw, bh));
            }
            for (int i = 0; i < drawRows; i++)
            {
                int row = Mathf.Min(_totalRows - 1, (int)(i * step));
                RowToLineSub(row, out int line, out int sub);
                RowBounds(line, sub, out int rs, out int re);
                string text = _lines[line];
                float y = i * ySpacing;
                float bh = Mathf.Max(1f, rowH * 0.7f);
                var spans = _lineSpans != null && line < _lineSpans.Length ? _lineSpans[line] : null;
                int pos = rs;
                // skip leading whitespace of the row
                while (pos < re && pos < text.Length && text[pos] == ' ') pos++;
                if (spans != null)
                {
                    foreach (var sp in spans)
                    {
                        int ss = Mathf.Max(sp.Start, pos), se = Mathf.Min(sp.Start + sp.Length, re);
                        if (se <= ss) continue;
                        if (ss > pos) // default-colored gap
                            Add(defaultColor, Mathf.Min(pos * charPx, w - 2), y,
                                Mathf.Min((ss - pos) * charPx, w - 2 - pos * charPx), bh);
                        Add(_minimapColors.TryGetValue(sp.Class, out var c) ? c : defaultColor,
                            Mathf.Min(ss * charPx, w - 2), y,
                            Mathf.Min((se - ss) * charPx, w - 2 - ss * charPx), bh);
                        pos = se;
                    }
                }
                if (pos < re)
                    Add(defaultColor, Mathf.Min(pos * charPx, w - 2), y,
                        Mathf.Min((re - pos) * charPx, w - 2 - pos * charPx), bh);
            }
            foreach (var kv in batches)
            {
                p.fillColor = kv.Key;
                p.BeginPath();
                foreach (var r in kv.Value)
                {
                    p.MoveTo(new Vector2(r.x, r.y));
                    p.LineTo(new Vector2(r.xMax, r.y));
                    p.LineTo(new Vector2(r.xMax, r.yMax));
                    p.LineTo(new Vector2(r.x, r.yMax));
                    p.ClosePath();
                }
                p.Fill();
            }

            // Viewport indicator
            float contentH = _totalRows * _lineHeight;
            float viewH = _scroll.contentViewport.layout.height;
            if (contentH > 0)
            {
                float mapContentH = drawRows * ySpacing;
                float top = _scroll.verticalScroller.value / contentH * mapContentH;
                float ih = Mathf.Max(6, viewH / contentH * mapContentH);
                p.fillColor = new Color(_textColor.r, _textColor.g, _textColor.b, 0.12f);
                p.BeginPath();
                p.MoveTo(new Vector2(0, top));
                p.LineTo(new Vector2(w, top));
                p.LineTo(new Vector2(w, top + ih));
                p.LineTo(new Vector2(0, top + ih));
                p.ClosePath();
                p.Fill();
            }
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
            // Wholesale content replacement (tab switch, reload) invalidates
            // extra carets — but not when it's a multi-caret edit itself.
            if (!_inMultiEdit && _extra.Count > 0) _extra.Clear();
            if (!_inMultiEdit && !_internalReplace && _folds.Count > 0) _folds.Clear();
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
            _docVersion++;
            ClampCaret();
            Reclassify();
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

        /// <summary>0-based caret position for the API facade's cursor query.</summary>
        internal int caretLine => _caretLine;
        internal int caretColumn => _caretCol;

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

        public void SetPalette(HighlightTheme.Palette palette)
        {
            _palette = palette;
            _textColor = palette.TextColor;
            var selection = palette.SelectionColor;
            _selectionColor = new Color(selection.r, selection.g, selection.b, 0.55f);
            // Occurrence highlight: related to the selection color but clearly
            // weaker, so the active selection stays dominant.
            _matchColor = new Color(_textColor.r, _textColor.g, _textColor.b, 0.16f);
            style.backgroundColor = palette.BackgroundColor;
            _gutterCol.style.backgroundColor = palette.BackgroundColor;
            _caret.style.backgroundColor = _textColor;
            _minimapColors.Clear();
            foreach (TokenClass cls in System.Enum.GetValues(typeof(TokenClass)))
            {
                string hex = palette.ColorFor(cls);
                _minimapColors[cls] = hex != null && ColorUtility.TryParseHtmlString(hex, out var c)
                    ? new Color(c.r, c.g, c.b, 0.55f)
                    : new Color(_textColor.r, _textColor.g, _textColor.b, 0.35f);
            }
            _lineMarkup = null; // colors changed; markup rebuilds lazily
            RefreshVisible();
        }

        public void SetClassifier(ISyntaxClassifier classifier)
        {
            _classifier = classifier;
            Reclassify();
            RefreshVisible();
        }

        /// <summary>Replaces heuristic spans with compiler-accurate ones;
        /// ignored if the document changed since they were computed.</summary>
        internal void ApplySemanticSpans(List<SyntaxSpan> spans, int forVersion)
        {
            if (forVersion != _docVersion || spans == null) return;
            BucketSpans(spans);
            _lineMarkup = null;
            RefreshVisible();
        }

        public bool minimapVisible
        {
            get => _minimap != null && _minimap.resolvedStyle.display == DisplayStyle.Flex;
            set
            {
                if (_minimap != null)
                    _minimap.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public bool showLineNumbers
        {
            get => _gutterCol.resolvedStyle.display == DisplayStyle.Flex;
            set { _gutterCol.style.display = value ? DisplayStyle.Flex : DisplayStyle.None; RefreshVisible(); }
        }

        void Reclassify()
        {
            _lineMarkup = null;
            if (_gameMode) { _lineSpans = null; return; } // overlay colors only
            if (_classifier == null || GetValueInternal().Length > 400_000)
            {
                _lineSpans = null;
                return;
            }
            BucketSpans(_classifier.Classify(_lines));
        }

        void BucketSpans(List<SyntaxSpan> spans)
        {
            var buckets = new List<SyntaxSpan>[_lines.Count];
            foreach (var s in spans)
            {
                if (s.Line < 0 || s.Line >= _lines.Count || s.Length <= 0) continue;
                (buckets[s.Line] = buckets[s.Line] ?? new List<SyntaxSpan>(8)).Add(s);
            }
            for (int i = 0; i < buckets.Length; i++)
                buckets[i]?.Sort((a, b) => a.Start.CompareTo(b.Start));
            _lineSpans = buckets;
        }

        /// <summary>Builds rich-text markup for line columns [fromCol, toCol)
        /// from that line's spans, escaping literal '&lt;' with noparse.</summary>
        string MarkupForRange(int line, int fromCol, int toCol)
        {
            string text = _lines[line];
            toCol = Mathf.Min(toCol, text.Length);
            var sb = new StringBuilder((toCol - fromCol) + 32);
            var spans = _lineSpans[line];
            int pos = fromCol;
            if (spans != null)
            {
                foreach (var s in spans)
                {
                    int ss = Mathf.Max(s.Start, fromCol);
                    int se = Mathf.Min(s.Start + s.Length, toCol);
                    if (se <= ss) continue;
                    if (ss > pos) AppendEscaped(sb, text, pos, ss, null);
                    AppendEscaped(sb, text, ss, se, _palette?.ColorFor(s.Class));
                    pos = se;
                }
            }
            if (pos < toCol) AppendEscaped(sb, text, pos, toCol, null);
            return sb.ToString();
        }

        static void AppendEscaped(StringBuilder sb, string text, int from, int to, string color)
        {
            if (to <= from) return;
            if (color != null) sb.Append("<color=").Append(color).Append('>');
            bool hasLt = text.IndexOf('<', from, to - from) >= 0;
            if (hasLt) sb.Append("<noparse>");
            sb.Append(text, from, to - from);
            if (hasLt) sb.Append("</noparse>");
            if (color != null) sb.Append("</color>");
        }

        // ---------- Word wrap layout ----------


        static string PluralChars(int n, string one, string many) =>
            n == 1 ? one : string.Format(many, n);

        float CharWidth(char c)
        {
            if (_charW.TryGetValue(c, out float w)) return w;
            // Bracket the char on BOTH sides: measurers trim trailing
            // whitespace, so "|" + ' ' measured the same as "|" and every
            // space came back 1px — which crushed the indent guides against
            // the gutter (Defects round 2026-07-27, item 7).
            float three = _measure.MeasureTextSize("|" + c + "|", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
            float two = _measure.MeasureTextSize("||", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
            w = Mathf.Max(1f, three - two);
            // Before the first layout pass the measurer reports NaN; return a
            // font-derived estimate WITHOUT caching so the real width is
            // measured (and cached) once layout exists.
            if (float.IsNaN(w)) return Mathf.Max(1f, resolvedStyle.fontSize * 0.6f);
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
                int r = 0;
                for (int i = 0; i < n; i++)
                {
                    _rowStarts[i] = r;
                    r += VisualRowsOfLine(i, 1);
                }
                _rowStarts[n] = r;
                _totalRows = r;
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
                row += VisualRowsOfLine(i, 1 + (_breaks[i]?.Count ?? 0));
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

        int CaretRow()
        {
            if (_folds.Count > 0 && IsLineHidden(_caretLine)) UnfoldContaining(_caretLine);
            return RowOfLine(_caretLine) + SubRowOfCol(_caretLine, _caretCol);
        }

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

            _content.style.height = (_totalRows + _ghostExtraRows) * _lineHeight;
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
                var ov = OverlayFor(line);
                bool rich = ov != null || (_lineSpans != null && line < _lineSpans.Length);
                label.enableRichText = rich;
                if (ov != null)
                    label.text = OverlayMarkupForRange(line, sc, ec, ov);
                else if (rich)
                {
                    var br = _breaks?[line];
                    if (br == null)
                    {
                        // Cache full-line markup for the common unwrapped case.
                        if (_lineMarkup == null || _lineMarkup.Length != _lines.Count)
                            _lineMarkup = new string[_lines.Count];
                        label.text = _lineMarkup[line] ??
                            (_lineMarkup[line] = MarkupForRange(line, 0, _lines[line].Length));
                    }
                    else label.text = MarkupForRange(line, sc, ec);
                }
                else label.text = _lines[line].Substring(sc, ec - sc);

                // Folded header: show the whole collapsed shape "{ ⋯ }" so the
                // region reads as closed (Defects round 2026-07-27, item 4).
                if (ec == _lines[line].Length && IsFoldedHeader(line))
                    label.text += rich ? " <color=#9A9A9A>⋯ }</color>" : " ⋯ }";

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
            _minimap?.MarkDirtyRepaint();
            RefreshOverlayBg(firstRow, visible);
            RefreshSelection(firstRow, visible);
            RefreshSelectionMatches(firstRow, visible);
            RefreshBracketMatch(firstRow, visible);
            RefreshExtraCarets(firstRow, visible);
            RefreshIndentGuides(firstRow, visible);
            RefreshCaret();
        }

        // One pooled label per visible row, positioned with the SAME row math
        // as the code lines. (A single multi-line label drifts: its natural
        // leading differs subtly from _lineHeight, compounding down the file.)
        void RefreshGutter(int firstRow, int visible, float scrollY)
        {
            if (_gutterCol.resolvedStyle.display == DisplayStyle.None) return;
            while (_gutterPool.Count < visible)
            {
                var g = new Label();
                g.AddToClassList("code-line");
                g.style.position = Position.Absolute;
                g.style.left = 0;
                g.style.right = 6;
                g.style.unityTextAlign = TextAnchor.UpperRight;
                g.style.opacity = 0.55f;
                // MUST be pickable: the fold-arrow click handler below never
                // fired with PickingMode.Ignore (Defects follow-up 2026-07-27).
                g.pickingMode = PickingMode.Position;
                _gutterCol.Add(g);
                g.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    var lbl = (Label)evt.currentTarget;
                    string t = lbl.text;
                    if (string.IsNullOrEmpty(t)) return;
                    bool marker = t[0] == '▸' || t[0] == '▾';
                    if (!marker) return;
                    if (int.TryParse(t.Substring(1), out int ln1)) ToggleFoldAt(ln1 - 1);
                    evt.StopPropagation();
                });
                _gutterPool.Add(g);
            }
            for (int i = 0; i < _gutterPool.Count; i++)
            {
                var g = _gutterPool[i];
                int row = firstRow + i;
                if (i >= visible || row >= _totalRows)
                {
                    g.style.display = DisplayStyle.None;
                    continue;
                }
                RowToLineSub(row, out int line, out int sub);
                g.style.display = DisplayStyle.Flex;
                g.style.top = row * _lineHeight - scrollY;
                g.style.height = _lineHeight;
                g.style.color = isLineBookmarked != null && isLineBookmarked(line)
                    ? new Color(1f, 0.65f, 0.2f) : _textColor;
                if (sub == 0)
                {
                    // Fold indicator: collapsed shows a right arrow, foldable
                    // shows a down arrow; clicking the gutter toggles it.
                    string num = (line + 1).ToString();
                    if (IsFoldedHeader(line)) g.text = "▸" + num;
                    else if (FoldEndLine(line) >= 0) g.text = "▾" + num;
                    else g.text = num;
                }
                else g.text = string.Empty;
            }
            int digits = Mathf.Max(3, (_lines.Count + 1).ToString().Length);
            _gutterCol.style.minWidth = 14 + digits * 8;
        }

        /// <summary>The selected text when it is a sensible search needle:
        /// single-line, 1..200 chars, not only whitespace.</summary>
        string SelectionNeedle(out int selLine, out int selStart)
        {
            NormalizedSelection(out int sl, out int sc, out int el, out int ec);
            selLine = sl; selStart = sc;
            if (sl != el || ec <= sc || ec - sc > 200) return null;
            // Clamp: a selection column can momentarily exceed the line (state
            // mutated between refreshes, e.g. folding) — never throw (issue #8).
            int len = _lines[sl].Length;
            if (sc >= len) return null;
            ec = Mathf.Min(ec, len);
            if (ec <= sc) return null;
            string s = _lines[sl].Substring(sc, ec - sc);
            return s.Trim().Length == 0 ? null : s;
        }

        /// <summary>Highlights every other occurrence of the selected text on
        /// the visible rows, in a weaker color than the selection itself.</summary>
        void RefreshSelectionMatches(int firstRow, int visible)
        {
            int quad = 0;
            string needle = SelectionNeedle(out int selLine, out int selStart);
            if (needle != null)
            {
                for (int i = 0; i < visible; i++)
                {
                    int row = firstRow + i;
                    if (row >= _totalRows) break;
                    RowToLineSub(row, out int line, out int sub);
                    RowBounds(line, sub, out int rs, out int re);
                    string text = _lines[line];
                    int idx = text.IndexOf(needle, StringComparison.Ordinal);
                    while (idx >= 0)
                    {
                        int cs = Mathf.Max(idx, rs), ce = Mathf.Min(idx + needle.Length, re);
                        if (ce > cs && !(line == selLine && idx == selStart))
                        {
                            if (quad >= _matchPool.Count)
                            {
                                var q = new VisualElement();
                                q.style.position = Position.Absolute;
                                q.pickingMode = PickingMode.Ignore;
                                _content.Insert(0, q); // beneath selection and text
                                _matchPool.Add(q);
                            }
                            var v = _matchPool[quad++];
                            v.style.display = DisplayStyle.Flex;
                            v.style.backgroundColor = _matchColor;
                            float x0 = MeasureRange(line, rs, cs);
                            v.style.left = x0;
                            v.style.top = row * _lineHeight;
                            v.style.width = Mathf.Max(2, MeasureRange(line, rs, ce) - x0);
                            v.style.height = _lineHeight;
                        }
                        idx = text.IndexOf(needle, idx + 1, StringComparison.Ordinal);
                    }
                }
            }
            for (int i = quad; i < _matchPool.Count; i++)
                _matchPool[i].style.display = DisplayStyle.None;
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
            // A stale ghost anchored elsewhere is just noise — drop it.
            if (HasGhost && (_ghostLine != _caretLine || _ghostCol != _caretCol))
                ClearGhost();
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

        // ---------- Clickable links (Ctrl+Click opens; hover tooltip) ----------

        static readonly System.Text.RegularExpressions.Regex LinkRx =
            new System.Text.RegularExpressions.Regex(
                @"\[[^\]]*\]\((?<t>[^)\s]+)[^)]*\)|(?<u>(?:https?://|mailto:)[^\s\)\]>""']+)");

        /// <summary>URL under (line, col): a bare http(s)/mailto URL, or a
        /// markdown [label](target) whose target has such a scheme — the
        /// whole [label](target) is the clickable span.</summary>
        internal bool TryGetLinkAt(int line, int col, out string url)
        {
            url = null;
            if (line < 0 || line >= _lines.Count) return false;
            foreach (System.Text.RegularExpressions.Match m in LinkRx.Matches(_lines[line]))
            {
                if (col < m.Index || col > m.Index + m.Length) continue;
                string u = m.Groups["u"].Success ? m.Groups["u"].Value
                    : m.Groups["t"].Success ? m.Groups["t"].Value : null;
                if (u == null) continue;
                u = u.TrimEnd('.', ',', ';', ':', '!', '?');
                if (!u.StartsWith("http://") && !u.StartsWith("https://") && !u.StartsWith("mailto:"))
                    continue;
                url = u;
                return true;
            }
            return false;
        }

        Label _linkTip;

        void UpdateLinkHover(Vector2 worldPos)
        {
            HitTest(worldPos, out int line, out int col);
            if (TryGetLinkAt(line, col, out string url))
            {
                if (_linkTip == null)
                {
                    _linkTip = new Label();
                    _linkTip.style.position = Position.Absolute;
                    _linkTip.pickingMode = PickingMode.Ignore;
                    _linkTip.style.backgroundColor = new Color(0.14f, 0.14f, 0.15f, 0.97f);
                    _linkTip.style.color = new Color(0.85f, 0.85f, 0.85f);
                    _linkTip.style.paddingLeft = _linkTip.style.paddingRight = 6;
                    _linkTip.style.paddingTop = _linkTip.style.paddingBottom = 3;
                    _linkTip.style.borderTopWidth = _linkTip.style.borderBottomWidth = 1;
                    _linkTip.style.borderLeftWidth = _linkTip.style.borderRightWidth = 1;
                    var bc = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                    _linkTip.style.borderTopColor = _linkTip.style.borderBottomColor = bc;
                    _linkTip.style.borderLeftColor = _linkTip.style.borderRightColor = bc;
                    _linkTip.style.fontSize = 10;
                    Add(_linkTip);
                }
                string shown = url.Length > 60 ? url.Substring(0, 57) + "..." : url;
                _linkTip.text = string.Format(L10n.Tr("Ctrl+Click to open {0}"), shown);
                var local = this.WorldToLocal(worldPos);
                _linkTip.style.left = Mathf.Max(0, local.x + 12);
                _linkTip.style.top = Mathf.Max(0, local.y - 26);
                _linkTip.style.display = DisplayStyle.Flex;
            }
            else if (_linkTip != null)
                _linkTip.style.display = DisplayStyle.None;
        }

        /// <summary>Ctrl+Click handler: opens the link under the caret in the
        /// system browser/mail client. Returns true when handled.</summary>
        bool TryOpenLinkAtCaret()
        {
            if (!TryGetLinkAt(_caretLine, _caretCol, out string url)) return false;
            Application.OpenURL(url);
            return true;
        }

        // ---------- Editing ----------

        void PushUndo(EditKind kind, int start, int end, string replacement)
        {
            double now = EditorApplication.timeSinceStartup;
            string removed = GetValueInternal().Substring(start, end - start);

            // Coalesce only same-kind, contiguous, recent edits within caps.
            bool coalesce = false;
            if (kind == _lastKind && _undo.Count > 0 &&
                now - _lastEditTime < CoalesceGapSeconds &&
                now - _groupStartTime < MaxGroupSeconds &&
                _groupChars < MaxGroupChars)
            {
                switch (kind)
                {
                    case EditKind.TypeChar:
                        // Starting a word char after space/punct opens a new
                        // group, so words undo one at a time (VS Code model).
                        char c = replacement.Length > 0 ? replacement[0] : ' ';
                        bool startsWord = IsWordCharUndo(c) && !IsWordCharUndo(_lastTypedChar);
                        coalesce = start == _lastEditEnd && !startsWord;
                        break;
                    case EditKind.Backspace: coalesce = end == _lastEditEnd; break;
                    case EditKind.ForwardDelete: coalesce = start == _lastEditEnd; break;
                }
            }

            if (!coalesce)
            {
                _undo.Add(new UndoOp
                {
                    Start = start, Removed = removed, Inserted = replacement,
                    CursorBefore = cursorIndex, SelectBefore = selectIndex
                });
                if (_undo.Count > UndoCap) _undo.RemoveAt(0);
                _groupStartTime = now;
                _groupChars = 0;
            }
            else
            {
                // Extend the group's delta in place.
                var op = _undo[_undo.Count - 1];
                switch (kind)
                {
                    case EditKind.TypeChar: op.Inserted += replacement; break;
                    case EditKind.Backspace: op.Removed = removed + op.Removed; op.Start = start; break;
                    case EditKind.ForwardDelete: op.Removed += removed; break;
                }
            }
            _redo.Clear();
            _lastEditTime = now;
            _lastKind = kind;
            _groupChars += Mathf.Max(replacement.Length, end - start);
            _lastEditEnd = kind == EditKind.Backspace ? start : start + replacement.Length;
            _lastTypedChar = kind == EditKind.TypeChar && replacement.Length > 0
                ? replacement[replacement.Length - 1] : '\0';
        }

        static bool IsWordCharUndo(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Compatibility overload: true maps to TypeChar, false to
        /// Programmatic (each programmatic edit is its own undo unit).</summary>
        internal void ReplaceRangeInternal(int start, int end, string replacement, int caret, bool typing)
            => ReplaceRangeInternal(start, end, replacement, caret,
                typing ? EditKind.TypeChar : EditKind.Programmatic);

        internal void ReplaceRangeInternal(int start, int end, string replacement, int caret, EditKind kind)
        {
            string v = GetValueInternal();
            start = Mathf.Clamp(start, 0, v.Length);
            end = Mathf.Clamp(end, start, v.Length);
            AdjustFoldsForEdit(start, end, replacement);
            if (onLineDelta != null)
            {
                int nl = 0;
                string curv = value;
                for (int i2 = start; i2 < end && i2 < curv.Length; i2++) if (curv[i2] == '\n') nl--;
                foreach (char ch2 in replacement) if (ch2 == '\n') nl++;
                if (nl != 0)
                {
                    IndexToLineCol(Mathf.Min(end, curv.Length), out int afterLine2, out _);
                    onLineDelta(afterLine2, nl);
                }
            }
            if (!_gameMode) PushUndo(kind, start, end, replacement); // game writes are not history
            _internalReplace = true;
            _selHistory.Clear(); // edits invalidate expand/shrink history
            if (!_inMultiEdit) CollapseExtraCarets(); // single-point edits drop extras
            SetValueWithoutNotify(v.Substring(0, start) + replacement + v.Substring(end));
            _internalReplace = false;
            cursorIndex = Mathf.Clamp(caret, 0, GetValueInternal().Length);
            CollapseAnchor();
            // Keep the group's redo caret current across coalesced edits.
            if (_undo.Count > 0)
            {
                var op = _undo[_undo.Count - 1];
                op.CursorAfter = cursorIndex;
                op.SelectAfter = selectIndex;
            }
            Notify();
            AfterCaretMove();
        }

        void InsertText(string text, EditKind kind)
        {
            int s = Mathf.Min(cursorIndex, selectIndex), e = Mathf.Max(cursorIndex, selectIndex);
            // Replacing a selection is never part of a typing chain.
            if (s != e && (kind == EditKind.TypeChar || kind == EditKind.TypeNewline))
                kind = EditKind.ReplaceSelection;
            ReplaceRangeInternal(s, e, text, s + text.Length, kind);
        }

        void InsertText(string text, bool typing) =>
            InsertText(text, typing ? EditKind.TypeChar : EditKind.Programmatic);

        public void Undo()
        {
            if (_gameMode || _undo.Count == 0) return;
            var op = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            CollapseExtraCarets();
            string v = GetValueInternal();
            if (op.Segments != null)
            {
                // Multi-caret op: inverses ascending land exactly at Start.
                var sb = new System.Text.StringBuilder(v);
                foreach (var seg in op.Segments)
                    sb.Remove(seg.Start, seg.Inserted.Length).Insert(seg.Start, seg.Removed);
                SetValueWithoutNotify(sb.ToString());
            }
            else
            {
            // The current text contains op.Inserted at op.Start; put back
            // op.Removed. Clamp defensively — offsets are ours to maintain.
            int start = Mathf.Clamp(op.Start, 0, v.Length);
            int end = Mathf.Clamp(start + op.Inserted.Length, start, v.Length);
            SetValueWithoutNotify(v.Substring(0, start) + op.Removed + v.Substring(end));
            }
            int len = GetValueInternal().Length;
            cursorIndex = Mathf.Clamp(op.CursorBefore, 0, len);
            selectIndex = Mathf.Clamp(op.SelectBefore, 0, len);
            _redo.Add(op);
            BreakUndoGroup();
            onUndoStatus?.Invoke(PluralChars(Mathf.Max(op.Inserted.Length, op.Removed.Length), L10n.Tr("Undid 1 character."), L10n.Tr("Undid {0} characters.")));
            Notify();
            AfterCaretMove();
        }

        public void Redo()
        {
            if (_gameMode || _redo.Count == 0) return;
            var op = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            CollapseExtraCarets();
            string v = GetValueInternal();
            if (op.Segments != null)
            {
                // Multi-caret op forward: descending keeps earlier starts valid.
                var sb = new System.Text.StringBuilder(v);
                for (int i = op.Segments.Count - 1; i >= 0; i--)
                {
                    var seg = op.Segments[i];
                    sb.Remove(seg.Start, seg.Removed.Length).Insert(seg.Start, seg.Inserted);
                }
                SetValueWithoutNotify(sb.ToString());
            }
            else
            {
            int start = Mathf.Clamp(op.Start, 0, v.Length);
            int end = Mathf.Clamp(start + op.Removed.Length, start, v.Length);
            SetValueWithoutNotify(v.Substring(0, start) + op.Inserted + v.Substring(end));
            }
            int len = GetValueInternal().Length;
            cursorIndex = Mathf.Clamp(op.CursorAfter, 0, len);
            selectIndex = Mathf.Clamp(op.SelectAfter, 0, len);
            _undo.Add(op);
            BreakUndoGroup();
            onUndoStatus?.Invoke(PluralChars(Mathf.Max(op.Inserted.Length, op.Removed.Length), L10n.Tr("Redid 1 character."), L10n.Tr("Redid {0} characters.")));
            Notify();
            AfterCaretMove();
        }

        // ---------- Input ----------

        void OnKeyDown(KeyDownEvent e)
        {
            // Game mode: the game owns the buffer — typing/caret keys that the
            // game did not consume must not edit it. (Window-level commands
            // ran before this handler and still work.)
            if (_gameMode) { e.StopPropagation(); return; }
            bool ctrl = e.ctrlKey || e.commandKey;

            if (HandleCompletionKey(e))
            {
                e.StopImmediatePropagation();
                return;
            }

            // Character-only events (second event of each key press)
            if (e.keyCode == KeyCode.None && e.character != '\0')
            {
                char c = e.character;
                if (c == '\n' || c == '\r' || c == '\t' || c == 25 /*EM from shift-tab*/)
                { e.StopPropagation(); return; } // handled on keyCode events
                if (!ctrl && c >= ' ')
                {
                    if (HasMultiCarets)
                    {
                        MultiType(c.ToString());
                        e.StopPropagation();
                        return;
                    }
                    if (EditorConfig.AutoCloseBrackets && HandleAutoClose(c))
                    {
                        if (IsWordCharUndo(c)) ShowCompletion(manual: false); else HideCompletion();
                        e.StopPropagation();
                        return;
                    }
                    InsertText(c.ToString(), typing: true);
                    if (IsWordCharUndo(c)) ShowCompletion(manual: false); else HideCompletion();
                    e.StopPropagation();
                }
                return;
            }

            // Zoom: Ctrl+'+'/'=', Ctrl+'-', Ctrl+0 — the browser/terminal set.
            if (ctrl && !e.altKey)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Equals:
                    case KeyCode.Plus:
                    case KeyCode.KeypadPlus:
                        ZoomBy(1);
                        e.StopImmediatePropagation();
                        return;
                    case KeyCode.Minus:
                    case KeyCode.KeypadMinus:
                        ZoomBy(-1);
                        e.StopImmediatePropagation();
                        return;
                    case KeyCode.Alpha0:
                    case KeyCode.Keypad0:
                        ZoomBy(0); // reset to default size
                        e.StopImmediatePropagation();
                        return;
                }
            }

            bool handled = true;
            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    if (HasGhost && AcceptGhost()) break; // Enter accepts Copilot ghost
                    if (HasMultiCarets) { MultiType("\n"); break; }
                    // Auto-indent: copy the current line's leading spaces.
                    string line = _lines[_caretLine];
                    int indent = 0;
                    while (indent < line.Length && indent < _caretCol && line[indent] == ' ') indent++;
                    InsertText("\n" + new string(' ', indent), EditKind.TypeNewline);
                    break;
                }
                case KeyCode.Backspace:
                {
                    if (HasMultiCarets) { MultiDelete(forward: false); break; }
                    if (HasSelection) { InsertText(string.Empty, EditKind.ReplaceSelection); break; }
                    int idx = cursorIndex;
                    if (idx == 0) { handled = true; break; }
                    // Deleting the opener of an empty auto-closed pair takes
                    // the closer with it.
                    string bv = GetValueInternal();
                    if (!ctrl && EditorConfig.AutoCloseBrackets && idx < bv.Length &&
                        ((BracketDir(bv[idx - 1]) == 1 && bv[idx] == BracketPartner(bv[idx - 1])) ||
                         ((bv[idx - 1] == '"' || bv[idx - 1] == '\'') && bv[idx] == bv[idx - 1])))
                    {
                        ReplaceRangeInternal(idx - 1, idx + 1, string.Empty, idx - 1, EditKind.Backspace);
                        break;
                    }
                    int remove;
                    if (ctrl) // word-wise delete (Ctrl+Backspace)
                        remove = _caretCol > 0 ? _caretCol - PrevWord(_lines[_caretLine], _caretCol) : 1;
                    else
                    {
                        // Whitespace deletes back to the previous tab stop.
                        int p = _caretCol > 0 ? PrevTabStopInSpaces(_lines[_caretLine], _caretCol) : -1;
                        remove = p >= 0 ? _caretCol - p : 1;
                    }
                    ReplaceRangeInternal(idx - remove, idx, string.Empty, idx - remove, EditKind.Backspace);
                    if (CompletionVisible) ShowCompletion(manual: false);
                    break;
                }
                case KeyCode.Delete:
                {
                    if (HasMultiCarets) { MultiDelete(forward: true); break; }
                    if (HasSelection) { InsertText(string.Empty, EditKind.ReplaceSelection); break; }
                    int idx = cursorIndex;
                    if (idx >= GetValueInternal().Length) break;
                    int count;
                    if (ctrl) // word-wise delete (Ctrl+Delete)
                        count = _caretCol < _lines[_caretLine].Length
                            ? NextWord(_lines[_caretLine], _caretCol) - _caretCol : 1;
                    else
                    {
                        // Whitespace deletes forward to the next tab stop.
                        int nx = NextTabStopInSpaces(_lines[_caretLine], _caretCol);
                        count = nx >= 0 ? nx - _caretCol : 1;
                    }
                    ReplaceRangeInternal(idx, idx + count, string.Empty, idx, EditKind.ForwardDelete);
                    break;
                }
                case KeyCode.LeftBracket when e.altKey && HasGhost:
                    CycleGhost(-1);
                    break;
                case KeyCode.RightBracket when e.altKey && HasGhost:
                    CycleGhost(1);
                    break;
                case KeyCode.Escape:
                    if (HasGhost) ClearGhost();
                    else if (HasMultiCarets) CollapseExtraCarets();
                    else handled = false;
                    break;
                case KeyCode.LeftArrow:
                    if (HasMultiCarets && !e.shiftKey && !ctrl) MultiMoveH(-1);
                    MoveCaretH(-1, e.shiftKey, ctrl);
                    break;
                case KeyCode.RightArrow:
                    if (HasMultiCarets && !e.shiftKey && !ctrl) MultiMoveH(1);
                    MoveCaretH(1, e.shiftKey, ctrl);
                    break;
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
                case KeyCode.Space when ctrl && !e.altKey && !e.shiftKey:
                    ShowCompletion(manual: true);
                    break;
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
                    if (string.IsNullOrEmpty(clip)) break;
                    clip = clip.Replace("\r\n", "\n").Replace("\r", "\n");
                    if (HasMultiCarets) MultiPaste(clip);
                    else InsertText(clip, EditKind.Paste);
                    break;
                }
                case KeyCode.Z when ctrl && e.shiftKey: Redo(); break;
                case KeyCode.Z when ctrl: Undo(); break;
                case KeyCode.Y when ctrl && EditorConfig.Keymap != KeymapLayout.Rider: Redo(); break;
                default: handled = false; break;
            }
            if (handled)
            {
                e.StopPropagation();
            }
        }

        public void Copy() => CopySelection(false);
        public void Cut() => CopySelection(true);

        public void Paste()
        {
            string clip = EditorGUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip))
                InsertText(clip.Replace("\r\n", "\n").Replace("\r", "\n"), EditKind.Paste);
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
        internal void RefreshVisiblePublic() => RefreshVisible();
        public int LineCount => _lines.Count;

        /// <summary>Selected text, or null when there is no selection.</summary>
        internal string SelectedTextPublic => HasSelection ? SelectedText() : null;

        /// <summary>Maps a world position to a 0-based line/column.</summary>
        internal void HitTestPublic(Vector2 worldPos, out int line, out int col)
            => HitTest(worldPos, out line, out col);

        /// <summary>The word at (line, col), optionally selecting it.
        /// Returns null when the position holds no word character.</summary>
        internal string WordAt(int line, int col, bool select)
        {
            if (line < 0 || line >= _lines.Count) return null;
            WordRangeAt(line, col, out int ws, out int we);
            if (we <= ws) return null;
            string word = _lines[line].Substring(ws, we - ws);
            if (word.Trim().Length == 0) return null;
            if (select)
            {
                _anchorLine = line; _anchorCol = ws;
                _caretLine = line; _caretCol = we;
                RefreshVisible();
            }
            return word;
        }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        void CopySelection(bool cut)
        {
            if (!HasSelection)
            {
                // Whole-line copy/cut on an empty selection — the VS /
                // VS Code / Rider standard. The copied text keeps its
                // trailing newline so a paste inserts a full line.
                int line = _caretLine;
                int start = LineColToIndex(line, 0);
                string v = GetValueInternal();
                int end = line + 1 < _lines.Count ? LineColToIndex(line + 1, 0) : v.Length;
                string text = v.Substring(start, end - start);
                if (!text.EndsWith("\n")) text += "\n";
                EditorGUIUtility.systemCopyBuffer = text;
                if (cut)
                {
                    int remStart = start, remEnd = end;
                    if (end == v.Length && start > 0) remStart = start - 1; // last line eats the preceding newline
                    ReplaceRangeInternal(remStart, remEnd, string.Empty,
                        Mathf.Min(remStart, v.Length), EditKind.LineOp);
                }
                return;
            }
            EditorGUIUtility.systemCopyBuffer = SelectedText();
            if (cut) InsertText(string.Empty, EditKind.ReplaceSelection);
        }

        // ---------- Multi-caret editing (Batch 3 must-haves) ----------
        // The primary caret stays in line/col space (_caretLine/_caretCol);
        // EXTRA selections live in document-index space as (anchor, caret)
        // pairs. While extras exist, typing/backspace/delete/enter/paste
        // apply at every caret as ONE undo op; Escape or a plain click
        // collapses back to the primary caret.

        readonly List<(int anchor, int caret)> _extra = new List<(int, int)>();
        readonly List<VisualElement> _extraCaretPool = new List<VisualElement>();
        readonly List<VisualElement> _extraSelPool = new List<VisualElement>();
        bool _inMultiEdit;
        bool _internalReplace;

        internal bool HasMultiCarets => _extra.Count > 0;
        internal int CaretCount => 1 + _extra.Count;

        internal void CollapseExtraCarets()
        {
            if (_extra.Count == 0) return;
            _extra.Clear();
            RefreshVisible();
        }

        /// <summary>Adds an extra caret at a document index (Alt+Click).</summary>
        internal void AddCaretAt(int index)
        {
            index = Mathf.Clamp(index, 0, GetValueInternal().Length);
            if (index == cursorIndex && !HasSelection) return;
            for (int i = 0; i < _extra.Count; i++)
                if (_extra[i].caret == index)
                {
                    _extra.RemoveAt(i); // Alt+Click on an existing caret removes it
                    RefreshVisible();
                    return;
                }
            _extra.Add((index, index));
            RefreshVisible();
        }

        /// <summary>First press selects the word at the caret; subsequent
        /// presses add the next occurrence of the primary selection as an
        /// extra selection (VS Code Ctrl+D model).</summary>
        internal void AddNextOccurrence()
        {
            if (!HasSelection)
            {
                WordRangeAt(_caretLine, _caretCol, out int ws, out int we);
                if (we <= ws) return;
                _anchorLine = _caretLine; _anchorCol = ws;
                _caretCol = we;
                RefreshVisible();
                AfterCaretMove();
                return;
            }
            string v = GetValueInternal();
            int ps = Mathf.Min(cursorIndex, selectIndex);
            int pe = Mathf.Max(cursorIndex, selectIndex);
            string needle = v.Substring(ps, pe - ps);
            if (needle.Length == 0 || needle.Contains("\n")) return;
            // Search after the furthest existing caret, wrapping once.
            int from = pe;
            foreach (var (a, c) in _extra) from = Mathf.Max(from, Mathf.Max(a, c));
            int idx = v.IndexOf(needle, from, StringComparison.Ordinal);
            if (idx < 0) idx = v.IndexOf(needle, 0, StringComparison.Ordinal);
            if (idx < 0) return;
            if (idx == ps) return; // wrapped all the way around
            foreach (var (a, c) in _extra)
                if (Mathf.Min(a, c) == idx) return; // already covered
            _extra.Add((idx, idx + needle.Length));
            RefreshVisible();
        }

        /// <summary>Selects every occurrence of the selection (or the word at
        /// the caret): the first stays primary, the rest become extras.</summary>
        internal void SelectAllOccurrences()
        {
            if (!HasSelection) AddNextOccurrence(); // select the word first
            if (!HasSelection) return;
            string v = GetValueInternal();
            int ps = Mathf.Min(cursorIndex, selectIndex);
            int pe = Mathf.Max(cursorIndex, selectIndex);
            string needle = v.Substring(ps, pe - ps);
            if (needle.Length == 0 || needle.Contains("\n")) return;
            _extra.Clear();
            int idx = v.IndexOf(needle, 0, StringComparison.Ordinal);
            while (idx >= 0)
            {
                if (idx != ps) _extra.Add((idx, idx + needle.Length));
                idx = v.IndexOf(needle, idx + needle.Length, StringComparison.Ordinal);
            }
            RefreshVisible();
        }

        /// <summary>Adds a caret one line above/below every current caret at
        /// the same column (the practical column-selection workflow).</summary>
        internal void AddCaretOnAdjacentLine(int dir)
        {
            var carets = new List<int> { cursorIndex };
            foreach (var (_, c) in _extra) carets.Add(c);
            var toAdd = new List<int>();
            foreach (int idx in carets)
            {
                IndexToLineCol(idx, out int line, out int col);
                int nl = line + dir;
                if (nl < 0 || nl >= _lines.Count) continue;
                int nIdx = LineColToIndex(nl, Mathf.Min(col, _lines[nl].Length));
                bool exists = nIdx == cursorIndex;
                foreach (var (_, c) in _extra) if (c == nIdx) exists = true;
                foreach (int t in toAdd) if (t == nIdx) exists = true;
                if (!exists) toAdd.Add(nIdx);
            }
            foreach (int t in toAdd) _extra.Add((t, t));
            if (toAdd.Count > 0) RefreshVisible();
        }

        /// <summary>Every selection region, primary first, ascending by start.</summary>
        List<(int s, int e)> AllRegions()
        {
            int len = GetValueInternal().Length;
            var list = new List<(int, int)>
            {
                (Mathf.Min(cursorIndex, selectIndex), Mathf.Max(cursorIndex, selectIndex))
            };
            foreach (var (a, c) in _extra)
                list.Add((Mathf.Clamp(Mathf.Min(a, c), 0, len), Mathf.Clamp(Mathf.Max(a, c), 0, len)));
            list.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            // Merge overlaps so one edit never double-applies.
            for (int i = list.Count - 1; i > 0; i--)
                if (list[i].Item1 < list[i - 1].Item2)
                {
                    list[i - 1] = (list[i - 1].Item1, Mathf.Max(list[i - 1].Item2, list[i].Item2));
                    list.RemoveAt(i);
                }
            return list;
        }

        /// <summary>Applies one replacement per region as a single undo op;
        /// carets land after each replacement. texts is either one string
        /// (same everywhere) or exactly one per region.</summary>
        internal void MultiReplace(List<(int s, int e)> regions, List<string> texts,
            int backspace = 0, int forwardDelete = 0)
        {
            string v = GetValueInternal();
            var op = new UndoOp
            {
                Segments = new List<UndoSeg>(),
                CursorBefore = cursorIndex, SelectBefore = selectIndex
            };
            var newCarets = new List<int>();
            var sb = new System.Text.StringBuilder(v);
            for (int i = regions.Count - 1; i >= 0; i--)
            {
                var (s, e) = regions[i];
                if (s == e && backspace > 0) s = Mathf.Max(0, s - backspace);
                else if (s == e && forwardDelete > 0) e = Mathf.Min(v.Length, e + forwardDelete);
                string text = texts.Count == 1 ? texts[0] : texts[i];
                op.Segments.Insert(0, new UndoSeg
                {
                    Start = s, Removed = v.Substring(s, e - s), Inserted = text
                });
                sb.Remove(s, e - s).Insert(s, text);
            }
            // New caret indices: ascending with cumulative delta.
            int cum = 0;
            foreach (var seg in op.Segments)
            {
                newCarets.Add(seg.Start + cum + seg.Inserted.Length);
                cum += seg.Inserted.Length - seg.Removed.Length;
            }
            _inMultiEdit = true;
            SetValueWithoutNotify(sb.ToString());
            _inMultiEdit = false;
            _extra.Clear();
            cursorIndex = newCarets[0];
            CollapseAnchor();
            for (int i = 1; i < newCarets.Count; i++) _extra.Add((newCarets[i], newCarets[i]));
            op.CursorAfter = cursorIndex;
            op.SelectAfter = selectIndex;
            _undo.Add(op);
            if (_undo.Count > UndoCap) _undo.RemoveAt(0);
            _redo.Clear();
            BreakUndoGroup(); // multi ops never coalesce
            Notify();
            RefreshVisible();
            AfterCaretMove();
        }

        void MultiType(string text) =>
            MultiReplace(AllRegions(), new List<string> { text });

        void MultiDelete(bool forward)
        {
            var regions = AllRegions();
            MultiReplace(regions, new List<string> { string.Empty },
                backspace: forward ? 0 : 1, forwardDelete: forward ? 1 : 0);
        }

        void MultiPaste(string clip)
        {
            var regions = AllRegions();
            var lines = clip.TrimEnd('\n').Split('\n');
            var texts = lines.Length == regions.Count
                ? new List<string>(lines)              // one line per caret
                : new List<string> { clip };           // same text everywhere
            MultiReplace(regions, texts);
        }

        /// <summary>Moves every extra caret by ±1 (arrows in multi mode).</summary>
        void MultiMoveH(int dir)
        {
            int len = GetValueInternal().Length;
            for (int i = 0; i < _extra.Count; i++)
            {
                int c = Mathf.Clamp(_extra[i].caret + dir, 0, len);
                _extra[i] = (c, c);
            }
        }

        /// <summary>Extra carets and their selections on the visible rows.</summary>
        void RefreshExtraCarets(int firstRow, int visible)
        {
            int caretQuad = 0, selQuad = 0;
            foreach (var (a, c) in _extra)
            {
                // selection region
                int s = Mathf.Min(a, c), e = Mathf.Max(a, c);
                if (e > s)
                {
                    IndexToLineCol(s, out int sl, out int sc);
                    IndexToLineCol(e, out int el, out int ec);
                    for (int i = 0; i < visible; i++)
                    {
                        int row = firstRow + i;
                        if (row >= _totalRows) break;
                        RowToLineSub(row, out int line, out int sub);
                        if (line < sl || line > el) continue;
                        RowBounds(line, sub, out int rs, out int re);
                        int cs = line == sl ? Mathf.Max(sc, rs) : rs;
                        int ce = line == el ? Mathf.Min(ec, re) : re;
                        if (ce <= cs) continue;
                        if (selQuad >= _extraSelPool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Insert(0, q);
                            _extraSelPool.Add(q);
                        }
                        var sq = _extraSelPool[selQuad++];
                        sq.style.display = DisplayStyle.Flex;
                        sq.style.backgroundColor = _selectionColor;
                        float sx0 = MeasureRange(line, rs, cs);
                        sq.style.left = sx0;
                        sq.style.top = row * _lineHeight;
                        sq.style.width = Mathf.Max(2, MeasureRange(line, rs, ce) - sx0);
                        sq.style.height = _lineHeight;
                    }
                }
                // caret bar
                IndexToLineCol(c, out int cl, out int cc);
                int subRow = SubRowOfCol(cl, cc);
                int rowAbs = RowOfLine(cl) + subRow;
                if (rowAbs >= firstRow && rowAbs < firstRow + visible)
                {
                    RowBounds(cl, subRow, out int crs, out _);
                    if (caretQuad >= _extraCaretPool.Count)
                    {
                        var q = new VisualElement();
                        q.style.position = Position.Absolute;
                        q.pickingMode = PickingMode.Ignore;
                        _content.Add(q);
                        _extraCaretPool.Add(q);
                    }
                    var cq = _extraCaretPool[caretQuad++];
                    cq.style.display = DisplayStyle.Flex;
                    cq.style.backgroundColor = _textColor;
                    cq.style.left = MeasureRange(cl, crs, cc);
                    cq.style.top = rowAbs * _lineHeight;
                    cq.style.width = 2;
                    cq.style.height = _lineHeight;
                }
            }
            for (int i = caretQuad; i < _extraCaretPool.Count; i++)
                _extraCaretPool[i].style.display = DisplayStyle.None;
            for (int i = selQuad; i < _extraSelPool.Count; i++)
                _extraSelPool[i].style.display = DisplayStyle.None;
        }

        // ---------- Brackets (Batch 2 must-haves) ----------

        readonly List<VisualElement> _bracketPool = new List<VisualElement>();
        readonly Color _bracketColor = new Color(0.85f, 0.7f, 0.2f, 0.35f);

        static int BracketDir(char c) =>
            c == '(' || c == '[' || c == '{' ? 1 :
            c == ')' || c == ']' || c == '}' ? -1 : 0;

        static char BracketPartner(char c) => c switch
        {
            '(' => ')', ')' => '(',
            '[' => ']', ']' => '[',
            '{' => '}', '}' => '{',
            _ => '\0'
        };

        /// <summary>Bracket adjacent to the caret (preferring the char before
        /// it, VS-style); -1 when none.</summary>
        int BracketAtCaret(string v, int caret)
        {
            if (caret > 0 && caret <= v.Length && BracketDir(v[caret - 1]) != 0) return caret - 1;
            if (caret >= 0 && caret < v.Length && BracketDir(v[caret]) != 0) return caret;
            return -1;
        }

        /// <summary>Index of the bracket matching the one at
        /// <paramref name="index"/> (nesting-aware, plain text scan); -1 if
        /// unbalanced or not a bracket.</summary>
        internal int FindMatchingBracket(int index)
        {
            string v = GetValueInternal();
            if (index < 0 || index >= v.Length) return -1;
            char c = v[index];
            int dir = BracketDir(c);
            if (dir == 0) return -1;
            char partner = BracketPartner(c);
            int depth = 0;
            for (int i = index; i >= 0 && i < v.Length; i += dir)
            {
                if (v[i] == c) depth++;
                else if (v[i] == partner && --depth == 0) return i;
            }
            return -1;
        }

        /// <summary>Jumps the caret to the bracket matching the caret's one.</summary>
        internal void GoToMatchingBracket()
        {
            string v = GetValueInternal();
            int at = BracketAtCaret(v, cursorIndex);
            int m = at >= 0 ? FindMatchingBracket(at) : -1;
            if (m < 0) return;
            IndexToLineCol(m + 1, out int l, out int c);
            GoToLine(l + 1, c + 1);
        }

        /// <summary>Highlights the caret-adjacent bracket and its match on
        /// the visible rows (two quads, weaker than selection).</summary>
        void RefreshBracketMatch(int firstRow, int visible)
        {
            int quad = 0;
            string v = GetValueInternal();
            int at = HasSelection ? -1 : BracketAtCaret(v, cursorIndex);
            int match = at >= 0 ? FindMatchingBracket(at) : -1;
            if (match >= 0)
            {
                foreach (int pos in new[] { at, match })
                {
                    IndexToLineCol(pos, out int bl, out int bc);
                    for (int i = 0; i < visible; i++)
                    {
                        int row = firstRow + i;
                        if (row >= _totalRows) break;
                        RowToLineSub(row, out int line, out int sub);
                        if (line != bl) continue;
                        RowBounds(line, sub, out int rs, out int re);
                        if (bc < rs || bc >= re && re < _lines[line].Length) continue;
                        if (quad >= _bracketPool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Insert(0, q);
                            _bracketPool.Add(q);
                        }
                        var b = _bracketPool[quad++];
                        b.style.display = DisplayStyle.Flex;
                        b.style.backgroundColor = _bracketColor;
                        float x0 = MeasureRange(line, rs, bc);
                        b.style.left = x0;
                        b.style.top = row * _lineHeight;
                        b.style.width = Mathf.Max(2, MeasureRange(line, rs, bc + 1) - x0);
                        b.style.height = _lineHeight;
                        break;
                    }
                }
            }
            for (int i = quad; i < _bracketPool.Count; i++)
                _bracketPool[i].style.display = DisplayStyle.None;
        }

        /// <summary>Auto-closing pairs: openers insert their closer (caret
        /// between), typing a closer over the identical next char steps past
        /// it, selections get wrapped. Returns true when handled.</summary>
        bool HandleAutoClose(char c)
        {
            string v = GetValueInternal();
            bool isOpen = c == '(' || c == '[' || c == '{';
            bool isCloser = c == ')' || c == ']' || c == '}';
            bool isQuote = c == '"' || c == '\'';
            if (!isOpen && !isCloser && !isQuote) return false;
            char close = isOpen ? BracketPartner(c) : c;

            // Type-over: the very next char is the closer we'd insert.
            if ((isCloser || isQuote) && !HasSelection &&
                cursorIndex < v.Length && v[cursorIndex] == c)
            {
                cursorIndex = cursorIndex + 1;
                CollapseAnchor();
                AfterCaretMove();
                return true;
            }
            if (isCloser) return false;

            // Wrap the selection in the pair, keeping it selected inside.
            if (HasSelection)
            {
                int s = Mathf.Min(cursorIndex, selectIndex);
                int e = Mathf.Max(cursorIndex, selectIndex);
                string sel = v.Substring(s, e - s);
                ReplaceRangeInternal(s, e, c + sel + close.ToString(),
                    s + 1 + sel.Length, EditKind.ReplaceSelection);
                selectIndex = s + 1;
                cursorIndex = s + 1 + sel.Length;
                RefreshVisible();
                return true;
            }

            // Quotes: never pair right after a word char or the same quote
            // (apostrophes inside words, adjacent string literals).
            if (isQuote)
            {
                char prev = cursorIndex > 0 ? v[cursorIndex - 1] : ' ';
                if (char.IsLetterOrDigit(prev) || prev == c) return false;
            }
            // Pair only when what follows won't be glued to the closer.
            char next = cursorIndex < v.Length ? v[cursorIndex] : '\n';
            if (!(next == '\n' || char.IsWhiteSpace(next) || BracketDir(next) == -1 ||
                  next == ',' || next == ';'))
                return false;

            int at = cursorIndex;
            ReplaceRangeInternal(at, at, c.ToString() + close, at + 1, EditKind.TypeChar);
            return true;
        }

        // ---------- Expand / shrink selection (Batch 2) ----------

        readonly List<(int a, int b)> _selHistory = new List<(int, int)>();

        /// <summary>Grows the selection: caret → word → line → enclosing
        /// bracket block → whole document. Shrink walks back down.</summary>
        internal void ExpandSelection()
        {
            string v = GetValueInternal();
            int s = Mathf.Min(cursorIndex, selectIndex);
            int e = Mathf.Max(cursorIndex, selectIndex);
            int ns = s, ne = e;
            if (s == e)
            {
                WordRangeAt(_caretLine, _caretCol, out int ws, out int we);
                if (we > ws)
                {
                    ns = LineColToIndex(_caretLine, ws);
                    ne = LineColToIndex(_caretLine, we);
                }
                else
                {
                    ns = LineColToIndex(_caretLine, 0);
                    ne = LineColToIndex(_caretLine, _lines[_caretLine].Length);
                }
            }
            else
            {
                IndexToLineCol(s, out int sl, out _);
                IndexToLineCol(e, out int el, out _);
                int ls = LineColToIndex(sl, 0);
                int le = LineColToIndex(el, _lines[el].Length);
                if (s > ls || e < le) { ns = ls; ne = le; }
                else
                {
                    int open = FindEnclosingOpen(v, s, e);
                    int closeIdx = open >= 0 ? FindMatchingBracket(open) : -1;
                    if (closeIdx > open && (open < s || closeIdx + 1 > e)) { ns = open; ne = closeIdx + 1; }
                    else { ns = 0; ne = v.Length; }
                }
            }
            if (ns == s && ne == e) return;
            _selHistory.Add((s, e));
            selectIndex = ns;
            cursorIndex = ne;
            RefreshVisible();
            AfterCaretMove();
        }

        internal void ShrinkSelection()
        {
            if (_selHistory.Count == 0) return;
            var (a, b) = _selHistory[_selHistory.Count - 1];
            _selHistory.RemoveAt(_selHistory.Count - 1);
            int len = GetValueInternal().Length;
            selectIndex = Mathf.Clamp(a, 0, len);
            cursorIndex = Mathf.Clamp(b, 0, len);
            RefreshVisible();
            AfterCaretMove();
        }

        static int FindEnclosingOpen(string v, int s, int e)
        {
            int depth = 0;
            for (int i = s - 1; i >= 0; i--)
            {
                int d = BracketDir(v[i]);
                if (d == -1) depth++;
                else if (d == 1)
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return -1;
        }

        // ---------- Editing primitives (Batch 1 must-haves) ----------

        /// <summary>Selects the caret's whole line including its newline.</summary>
        internal void SelectCurrentLine()
        {
            _anchorLine = _caretLine;
            _anchorCol = 0;
            if (_caretLine + 1 < _lines.Count) { _caretLine++; _caretCol = 0; }
            else _caretCol = _lines[_caretLine].Length;
            _preferredCol = -1;
            RefreshVisible();
            AfterCaretMove();
        }

        /// <summary>Joins the selected lines (or the caret line with the next)
        /// into one, separating with a single space, as one undo step.</summary>
        internal void JoinLines()
        {
            int sl = _caretLine, el = _caretLine;
            if (HasSelection)
            {
                NormalizedSelection(out sl, out _, out el, out int ec);
                if (ec == 0 && el > sl) el--; // selection ending at line start excludes it
            }
            int last = Mathf.Max(sl, el);
            if (last <= sl) last = sl; // caret-only: join with the next line
            if (sl >= _lines.Count - 1) return;
            int joinTo = HasSelection && last > sl ? last : sl + 1;
            joinTo = Mathf.Min(joinTo, _lines.Count - 1);
            var sb = new System.Text.StringBuilder(_lines[sl].TrimEnd());
            for (int i = sl + 1; i <= joinTo; i++)
            {
                string next = _lines[i].TrimStart();
                if (sb.Length > 0 && next.Length > 0) sb.Append(' ');
                sb.Append(next);
            }
            int from = LineColToIndex(sl, 0);
            int to = LineColToIndex(joinTo, _lines[joinTo].Length);
            string joined = sb.ToString();
            ReplaceRangeInternal(from, to, joined, from + joined.Length, EditKind.LineOp);
        }

        /// <summary>Replaces the selection with f(selection), keeping it
        /// selected (UPPERCASE / lowercase transforms). No-op without one.</summary>
        internal void TransformSelection(System.Func<string, string> f)
        {
            if (!HasSelection) return;
            int s = Mathf.Min(cursorIndex, selectIndex);
            int e = Mathf.Max(cursorIndex, selectIndex);
            string r = f(GetValueInternal().Substring(s, e - s));
            ReplaceRangeInternal(s, e, r, s + r.Length, EditKind.LineOp);
            selectIndex = s;
            cursorIndex = s + r.Length;
            RefreshVisible();
        }

        /// <summary>Sorts the full lines covered by the selection (ordinal).</summary>
        internal void SortSelectedLines()
        {
            if (!HasSelection) return;
            NormalizedSelection(out int sl, out _, out int el, out int ec);
            if (ec == 0 && el > sl) el--;
            if (el <= sl) return;
            int from = LineColToIndex(sl, 0);
            int to = LineColToIndex(el, _lines[el].Length);
            var seg = GetValueInternal().Substring(from, to - from).Split('\n');
            System.Array.Sort(seg, System.StringComparer.Ordinal);
            string r = string.Join("\n", seg);
            ReplaceRangeInternal(from, to, r, from + r.Length, EditKind.LineOp);
            selectIndex = from;
            cursorIndex = from + r.Length;
            RefreshVisible();
        }

        /// <summary>Opens a new auto-indented line below the caret's line
        /// without splitting it (Ctrl+Enter family).</summary>
        internal void InsertLineBelow()
        {
            string line = _lines[_caretLine];
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            int idx = LineColToIndex(_caretLine, line.Length);
            ReplaceRangeInternal(idx, idx, "\n" + new string(' ', indent),
                idx + 1 + indent, EditKind.TypeNewline);
        }

        /// <summary>Opens a new auto-indented line above the caret's line.</summary>
        internal void InsertLineAbove()
        {
            string line = _lines[_caretLine];
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            int idx = LineColToIndex(_caretLine, 0);
            ReplaceRangeInternal(idx, idx, new string(' ', indent) + "\n",
                idx + indent, EditKind.TypeNewline);
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

        // Word-snap drag state: set by double-click, extends by whole words.
        bool _wordDrag;
        int _wordDragLine, _wordDragStart, _wordDragEnd;

        void OnPointerDown(PointerDownEvent e)
        {
            // Addon mouse hook (API 1.1): text-coordinate event first; a
            // handled button-down never reaches caret/selection handling.
            if (Scripting.AteApi.HasMouseSubscribers)
            {
                HitTest(e.position, out int al, out int ac);
                if (Scripting.AteApi.RaiseMouseButtonDown(al + 1, ac + 1, e.button))
                {
                    e.StopPropagation();
                    return;
                }
            }
            _selHistory.Clear();
            HideCompletion();
            if (e.button == 0 && e.altKey && !e.ctrlKey)
            {
                // Alt+Click: add (or remove) an extra caret; the primary stays.
                HitTest(e.position, out int mcLine, out int mcCol);
                AddCaretAt(LineColToIndex(mcLine, mcCol));
                e.StopPropagation();
                return;
            }
            if (e.button == 0 && !e.altKey) CollapseExtraCarets();
            if (e.button != 0) return;
            Focus();
            // Pressing INSIDE the selection starts a potential text drag
            // (must-have #19c): the selection is preserved until we know
            // whether this is a drag or a plain caret-placing click.
            if (HasSelection && e.clickCount == 1 && !e.shiftKey && !(e.ctrlKey || e.commandKey))
            {
                HitTest(e.position, out int hl, out int hc);
                int hitIdx = LineColToIndex(hl, hc);
                int selA = Mathf.Min(cursorIndex, selectIndex);
                int selB = Mathf.Max(cursorIndex, selectIndex);
                if (hitIdx > selA && hitIdx < selB)
                {
                    _textDragPending = true;
                    _textDragging = false;
                    _textDragOrigin = e.position;
                    _dragging = false;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
            }
            PlaceCaretAt(e.position, e.shiftKey);
            if (e.ctrlKey || e.commandKey)
            {
                // Links win over Go to Definition: a URL (or markdown link)
                // under the click opens in the browser/mail client.
                if (!TryOpenLinkAtCaret())
                    onNavigateRequest?.Invoke(_caretLine, _caretCol);
                e.StopPropagation();
                return; // no drag-select from a navigate gesture
            }
            if (e.clickCount >= 2)
            {
                // Double-click folding gestures (before word selection):
                // - on a folded header's "⋯ }" indicator (past the real end of
                //   the line) → reopen the region;
                // - on a lone '{' or '}' → fold the region that brace bounds.
                if (HandleFoldDoubleClick())
                {
                    e.StopPropagation();
                    return;
                }
                // Double-click: select the word under the cursor; dragging from
                // here extends the selection a whole word at a time.
                WordRangeAt(_caretLine, _caretCol, out _wordDragStart, out _wordDragEnd);
                _wordDragLine = _caretLine;
                _anchorLine = _caretLine;
                _anchorCol = _wordDragStart;
                _caretCol = _wordDragEnd;
                _wordDrag = true;
                RefreshVisible();
            }
            _dragging = true;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (Scripting.AteApi.HasMouseSubscribers)
            {
                HitTest(e.position, out int al, out int ac);
                Scripting.AteApi.RaiseMouseMove(al + 1, ac + 1);
            }
            if (_textDragPending)
            {
                if (!_textDragging &&
                    ((Vector2)e.position - _textDragOrigin).sqrMagnitude > 16f)
                    _textDragging = true;
                return;
            }
            if (!_dragging)
            {
                UpdateLinkHover(e.position); // plain hover: link tooltip
                return;
            }
            if (_wordDrag) WordSnapSelectTo(e.position);
            else PlaceCaretAt(e.position, true);
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (Scripting.AteApi.HasMouseSubscribers)
            {
                HitTest(e.position, out int al, out int ac);
                Scripting.AteApi.RaiseMouseButtonUp(al + 1, ac + 1, e.button);
            }
            if (_textDragPending)
            {
                bool wasDragging = _textDragging;
                _textDragPending = false;
                _textDragging = false;
                this.ReleasePointer(e.pointerId);
                if (wasDragging) DropSelectionAt(e.position, e.ctrlKey || e.commandKey);
                else
                {
                    // No drag happened: behave like the click it was.
                    PlaceCaretAt(e.position, false);
                    RefreshVisible();
                }
                return;
            }
            if (!_dragging) return;
            _dragging = false;
            _wordDrag = false;
            this.ReleasePointer(e.pointerId);
        }

        // ---------- Drag-and-drop of the selected text (#19c) ----------

        bool _textDragPending, _textDragging;
        Vector2 _textDragOrigin;

        /// <summary>Moves (or, with Ctrl, copies) the selected text to the
        /// drop position — one undo step via the multi-edit machinery.</summary>
        void DropSelectionAt(Vector2 worldPos, bool copy)
        {
            HitTest(worldPos, out int tl, out int tc);
            DropSelectionAtIndex(LineColToIndex(tl, tc), copy);
        }

        internal void DropSelectionAtIndex(int target, bool copy)
        {
            if (!HasSelection) return;
            int selA = Mathf.Min(cursorIndex, selectIndex);
            int selB = Mathf.Max(cursorIndex, selectIndex);
            if (target >= selA && target <= selB) return; // dropped on itself
            string sel = GetValueInternal().Substring(selA, selB - selA);
            var regions = new List<(int s, int e)>();
            var texts = new List<string>();
            if (copy)
            {
                regions.Add((target, target));
                texts.Add(sel);
            }
            else if (target < selA)
            {
                regions.Add((target, target)); texts.Add(sel);
                regions.Add((selA, selB)); texts.Add(string.Empty);
            }
            else // target > selB
            {
                regions.Add((selA, selB)); texts.Add(string.Empty);
                regions.Add((target, target)); texts.Add(sel);
            }
            MultiReplace(regions, texts);
            CollapseExtraCarets();
            // Select the dropped text at its new home.
            int newStart = copy || target < selA ? target : target - sel.Length;
            selectIndex = newStart;
            cursorIndex = newStart + sel.Length;
            RefreshVisible();
        }

        void HitTest(Vector2 worldPos, out int line, out int col)
        {
            Vector2 local = _content.WorldToLocal(worldPos);
            int row = Mathf.Clamp((int)(local.y / _lineHeight), 0, Mathf.Max(0, _totalRows - 1));
            RowToLineSub(row, out line, out int sub);
            col = ColForXInRow(line, sub, local.x);
        }

        void PlaceCaretAt(Vector2 worldPos, bool extend)
        {
            HitTest(worldPos, out int line, out int col);
            _caretLine = line;
            _caretCol = col;
            _preferredCol = -1;
            if (!extend) CollapseAnchor();
            AfterCaretMove();
        }

        /// <summary>Extends the double-click selection so it always covers
        /// whole words: the original word stays selected, and the moving end
        /// snaps to the boundary of the word under the cursor.</summary>
        void WordSnapSelectTo(Vector2 worldPos)
        {
            HitTest(worldPos, out int line, out int col);
            WordRangeAt(line, col, out int ws, out int we);
            bool before = line < _wordDragLine || (line == _wordDragLine && ws < _wordDragStart);
            if (before)
            {
                _anchorLine = _wordDragLine; _anchorCol = _wordDragEnd;
                _caretLine = line; _caretCol = ws;
            }
            else
            {
                _anchorLine = _wordDragLine; _anchorCol = _wordDragStart;
                _caretLine = line; _caretCol = we;
            }
            _preferredCol = -1;
            EnsureCaretVisible();
            RefreshVisible();
        }

        /// <summary>The word at (line, col): an identifier run when touching
        /// one (preferring the run just left of the boundary), else a
        /// whitespace run, else the single character.</summary>
        void WordRangeAt(int line, int col, out int start, out int end)
        {
            string t = _lines[Mathf.Clamp(line, 0, _lines.Count - 1)];
            col = Mathf.Clamp(col, 0, t.Length);
            bool Id(char c) => char.IsLetterOrDigit(c) || c == '_';
            if (t.Length == 0) { start = end = 0; return; }

            if ((col < t.Length && Id(t[col])) || (col > 0 && Id(t[col - 1])))
            {
                start = col; while (start > 0 && Id(t[start - 1])) start--;
                end = col; while (end < t.Length && Id(t[end])) end++;
            }
            else if (col < t.Length && char.IsWhiteSpace(t[col]))
            {
                start = col; while (start > 0 && char.IsWhiteSpace(t[start - 1])) start--;
                end = col; while (end < t.Length && char.IsWhiteSpace(t[end])) end++;
            }
            else if (col < t.Length) { start = col; end = col + 1; }
            else { start = col - 1; end = col; }
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
                        InsertText(clip.Replace("\r\n", "\n").Replace("\r", "\n"), EditKind.Paste);
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
#endif
