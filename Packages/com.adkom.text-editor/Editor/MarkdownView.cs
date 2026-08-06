#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental; // Pointer*LinkTagEvent

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Rendered Markdown mode with block-level WYSIWYG editing: the document
    /// renders as styled blocks (headers, paragraphs, lists, quotes, code
    /// fences, rules); clicking a block swaps in an inline source editor for
    /// just that block, and committing (Ctrl+Enter or focus loss) raises
    /// onEditBlock with the block's character range so the window can apply
    /// the edit through the CodeView — keeping undo/redo and dirty tracking.
    /// Escape cancels. The document text remains the single source of truth.
    /// </summary>
    public class MarkdownView : VisualElement
    {
        class Block
        {
            public int StartOffset, EndOffset; // [start, end) in document chars
            public string Source;              // raw markdown of the block
            public string Kind;                // h1..h6, p, list, quote, code, hr
        }

        readonly ScrollView _scroll;
        HighlightTheme.Palette _palette;
        string _text = string.Empty;
        readonly List<Block> _blocks = new List<Block>();

        /// <summary>(startOffset, endOffset, replacementSource)</summary>
        public event Action<int, int, string> onEditBlock;

        /// <summary>Raised by formatting actions when no block is being
        /// edited: the window appends the given source as a new block.</summary>
        public event Action<string> onInsertBlock;

        /// <summary>Directory of the rendered document; resolves relative
        /// image paths. Null for unsaved buffers.</summary>
        public string BaseDir;

        /// <summary>Read-only mode: clicks never open block editors; labels
        /// become selectable so text can be copied (plain, no formatting).
        /// Set before <see cref="Render"/> — the window re-renders on toggle.</summary>
        public bool Locked;

        /// <summary>Raised by the locked-mode context menu's Unlock item; the
        /// owning window flips the document's lock state and re-renders.</summary>
        public event Action onUnlockRequest;

        /// <summary>The owning window appends its shared context-menu
        /// entries (Tabs, file ops, the Git submenu) here — the rendered
        /// view itself only knows its clipboard/lock items.</summary>
        public Action<DropdownMenu> onContextMenuExtend;

        TextField _activeEditor;
        public bool HasActiveEditor => _activeEditor != null;

        /// <summary>Commits the open inline block editor, if any. Keyboard
        /// shortcuts (Ctrl+S, …) run WITHOUT moving focus, so they must flush
        /// the edit the way clicking a menu does via FocusOut — blurring the
        /// editor drives the same commit path.</summary>
        public void CommitActiveEdit() => _activeEditor?.Blur();

        string _hoverLink; // link under the pointer, for "Copy Link URL"

        public MarkdownView()
        {
            style.flexGrow = 1;
            // The view takes keyboard focus when a selection drag starts, so
            // Ctrl+A / Ctrl+C keep working after the labels stop receiving
            // pointer-downs (the view captures the pointer during drags).
            focusable = true;
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.contentContainer.style.paddingLeft = 16;
            _scroll.contentContainer.style.paddingRight = 16;
            _scroll.contentContainer.style.paddingTop = 10;
            Add(_scroll);

            // Selection highlight, painted BEHIND the text: absolutely
            // positioned so it adds no layout, stretched over the whole
            // content, and re-inserted as the first child on every Render.
            _selOverlay = new VisualElement { name = "selection-overlay", pickingMode = PickingMode.Ignore };
            _selOverlay.style.position = Position.Absolute;
            _selOverlay.style.left = 0;
            _selOverlay.style.right = 0;
            _selOverlay.style.top = 0;
            _selOverlay.style.bottom = 0;
            _selOverlay.generateVisualContent += PaintSelection;
            // A window resize re-wraps every label WITHOUT a Render: all the
            // memoized cursor positions (and the line heights derived from
            // them) go stale — measured two lines of drift after a 250 px
            // narrowing. The character offsets survive a re-wrap; only their
            // positions move, so re-derive the geometry.
            _scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.oldRect.size == e.newRect.size) return;
                _cursorPos.Clear();
                if (HasDocSelection) ApplyDocSelection();
                else if (_selRects.Count > 0) ClearDrawnSelection();
            });

            // Rendered-mode links: Ctrl+Click on a <link>-tagged span opens
            // it; hovering shows the instruction tooltip on the label.
            RegisterCallback<PointerDownLinkTagEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey) || string.IsNullOrEmpty(e.linkID)) return;
                OpenLinkTarget(e.linkID);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            // Link hover tip: a custom floating label at the CURSOR. The
            // native element tooltip anchors to the hovered element's box —
            // and links live inside tall multi-block segment labels, so the
            // tooltip appeared at the label's bottom edge, far from the
            // pointer.
            _linkTip = AteTooltip.MakeTip(); // ONE tooltip look across ATE
            Add(_linkTip);
            RegisterCallback<PointerOverLinkTagEvent>(e =>
            {
                _hoverLink = e.linkID;
                if (string.IsNullOrEmpty(e.linkID)) return;
                // Destination plus the link's title/alt when the source
                // carried one ([text](url "title"), image alts).
                string tip = string.Format(L10n.Tr("Ctrl+Click to open {0}"), e.linkID);
                if (_linkTitles.TryGetValue(e.linkID, out var title) && !string.IsNullOrEmpty(title))
                    tip = title + "\n" + tip;
                _linkTip.text = tip;
                _linkTip.style.display = DisplayStyle.Flex;
                MoveLinkTip(e.position);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_linkTip.style.display == DisplayStyle.Flex) MoveLinkTip(e.position);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerOutLinkTagEvent>(e =>
            {
                _hoverLink = null;
                _linkTip.style.display = DisplayStyle.None;
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _hoverLink = null;
                _linkTip.style.display = DisplayStyle.None;
            });

            // Right-click menu, via UITK's ContextualMenuPopulateEvent — the
            // only reliable hook here: the selectable labels bring their own
            // native "Copy" menu which displays AFTER (and over) anything a
            // MouseUp handler shows. Populating at TrickleDown and stopping
            // propagation REPLACES the native items instead of racing them.
            // Locked (read-only) mode offers clipboard actions — copies are
            // the RENDERED text without formatting (no rich-text tags, no
            // markdown markers) so URLs and prose paste clean. Both modes
            // get the window's shared entries via onContextMenuExtend (Tabs,
            // file ops, Git submenu) — a rendered .md is still a file in the
            // repo. In unlocked mode a click inside an open block editor
            // keeps the TextField's native menu.
            RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                if (!Locked && InBlockEditor(evt.target as VisualElement)) return;
                // Link items lead the menu in BOTH modes when the pointer is
                // on a link — opening comes first, the copy beside it.
                string link = _hoverLink;
                if (!string.IsNullOrEmpty(link))
                {
                    // Local file links open as ATE tabs; the label says so.
                    evt.menu.AppendAction(System.IO.File.Exists(link)
                            ? L10n.Tr("Open in Text Editor") : L10n.Tr("Open Link in Browser"),
                        _ => OpenLinkTarget(link));
                    evt.menu.AppendAction(L10n.Tr("Copy Link URL"),
                        _ => EditorGUIUtility.systemCopyBuffer = link);
                    evt.menu.AppendSeparator();
                }
                if (Locked)
                {
                    if (HasDocSelection)
                        evt.menu.AppendAction(L10n.Tr("Copy Selection as Text"),
                            _ => EditorGUIUtility.systemCopyBuffer = SelectedPlainText());
                    var block = BlockFor(evt.target as VisualElement);
                    if (block != null)
                        evt.menu.AppendAction(L10n.Tr("Copy Block as Text"),
                            _ => EditorGUIUtility.systemCopyBuffer = PlainTextFor(block));
                    evt.menu.AppendAction(L10n.Tr("Copy All as Text"),
                        _ => EditorGUIUtility.systemCopyBuffer = PlainText());
                    evt.menu.AppendSeparator();
                    evt.menu.AppendAction(L10n.Tr("Unlock (Allow Editing)"),
                        _ => onUnlockRequest?.Invoke());
                }
                onContextMenuExtend?.Invoke(evt.menu);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            HookDocSelection();
        }

        static Block BlockFor(VisualElement v)
        {
            for (; v != null; v = v.parent)
                if (v.userData is Block b) return b;
            return null;
        }

        /// <summary>True when the element sits inside an open click-to-edit
        /// block editor (its TextField owns the mouse there).</summary>
        static bool InBlockEditor(VisualElement v)
        {
            for (; v != null; v = v.parent)
                if (v is TextField) return true;
            return false;
        }

        // ---------- Locked-mode DOCUMENT selection (spans blocks) ----------
        //
        // Native TextElement selection is per label, so it can never cross a
        // block boundary — and Ctrl+A used to select just the focused label.
        // This layer adds a view-level selection ABOVE the native one:
        // dragging past the block you started in selects whole blocks (shown
        // as a tint), Ctrl+A selects the entire document, and Ctrl+C copies
        // the covered blocks as plain rendered text. Within a single block,
        // the native character-precise selection still works as before.

        readonly List<Label> _selLabels = new List<Label>(); // visual == document order
        int _selAnchor = -1, _selFocus = -1;                 // indices into _selLabels
        bool _selAll, _selDragging;

        internal bool HasDocSelection =>
            _selAll || (_selAnchor >= 0 && _selFocus >= 0 &&
                        (_selAnchor != _selFocus || _selAnchorChar != _selFocusChar));

        void HookDocSelection()
        {
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (!Locked || e.button != 0) return;
                // Triple-click: select the whole BLOCK (the paragraph) via
                // the document-selection layer. The native label machinery
                // never delivered a triple-click line select here, so the
                // doc layer owns it — and the block is the natural unit in
                // a rendered view.
                if (e.clickCount >= 3 && !e.ctrlKey && !e.commandKey &&
                    IsInContent(e.target as VisualElement))
                {
                    int idx = LabelIndexAt(e.position);
                    if (idx >= 0)
                    {
                        ClearDocSelection();
                        _selAnchor = _selFocus = idx;
                        _selAnchorChar = 0;
                        _selFocusChar = (_selLabels[idx].text ?? string.Empty).Length;
                        ApplyDocSelection();
                        Focus(); // so Ctrl+C routes through this view
                        e.StopImmediatePropagation();
                    }
                    return;
                }
                // Pass through what the labels must handle natively:
                // Ctrl+Click link opening and double-click word selection —
                // those engage the label's own machinery.
                if (e.ctrlKey || e.commandKey || e.clickCount > 1) return;
                // And anything that is not the document itself — a scrollbar
                // click has to scroll, not clear the selection.
                if (!IsInContent(e.target as VisualElement)) return;

                ClearDocSelection();
                _selAnchor = _selFocus = LabelIndexAt(e.position);
                _selAnchorChar = _selFocusChar =
                    _selAnchor >= 0 ? CharIndexAt(_selLabels[_selAnchor], e.position) : 0;
                _selDragging = _selAnchor >= 0;
                if (!_selDragging) return;

                // Take the pointer for the view and keep the event from the
                // label: if the label's native selection captured it instead,
                // every mouse-move would regenerate that label's entire text
                // mesh (6–14 ms on this package's own docs) to display a
                // highlight the overlay draws for well under a millisecond.
                // View capture also keeps moves flowing when the pointer
                // leaves the window, which the edge auto-scroll feeds on.
                Focus();   // so Ctrl+A / Ctrl+C route through this view
                this.CapturePointer(e.pointerId);
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            // The VIEW holds the pointer capture during a selection drag (see
            // PointerDown above), so captured moves arrive here directly —
            // including moves outside the window. The per-label
            // OnLabelPointerMove/Up handlers (see Render) only matter for the
            // native paths that still capture on a label: double/triple-click
            // drags and Ctrl+Click.
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!Locked || !_selDragging || (e.pressedButtons & 1) == 0) return;
                UpdateDocSelectionAt(e.position);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
                _selDragging = false;
                StopEdgeScroll();
                PromoteSelectionToOverlay();
            }, TrickleDown.TrickleDown);
            // If anything else steals the capture mid-drag, end the drag
            // cleanly with whatever was selected so far.
            RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!_selDragging) return;
                _selDragging = false;
                StopEdgeScroll();
                PromoteSelectionToOverlay();
            });
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (!Locked) return;
                bool ctrl = e.ctrlKey || e.commandKey;
                if (ctrl && e.keyCode == KeyCode.A)
                {
                    SelectAllDoc();
                    e.StopImmediatePropagation(); // keep the label's own select-all out of it
                }
                else if (ctrl && e.keyCode == KeyCode.C && HasDocSelection)
                {
                    // Never clobber the clipboard with nothing: an empty
                    // result means the selection resolved to zero characters,
                    // and whatever the user copied before must survive that.
                    string copied = SelectedPlainText();
                    if (copied.Length > 0) EditorGUIUtility.systemCopyBuffer = copied;
                    e.StopImmediatePropagation();
                }
                else if (e.keyCode == KeyCode.Escape && HasDocSelection)
                {
                    ClearDocSelection();
                    e.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        void UpdateDocSelectionAt(Vector2 panelPos)
        {
            _lastDragPos = panelPos;
            EdgeScrollTick(panelPos);
            int idx = LabelIndexAt(panelPos);
            if (idx < 0) return;
            int ch = CharIndexAt(_selLabels[idx], panelPos);
            // Within one label the character end moves even when the label
            // does not, so both have to be compared.
            if (idx == _selFocus && ch == _selFocusChar) return;
            _selFocus = idx;
            _selFocusChar = ch;
            ApplyDocSelection();
        }

        // ---------- Auto-scroll while dragging past the viewport ----------
        //
        // Dragging a selection to the top or bottom edge has to keep going:
        // without this the selection stops at whatever was on screen when the
        // drag started, and a document taller than the window can never be
        // selected with the mouse at all. Pointer-move events stop arriving
        // once the pointer stops moving, so the scrolling runs on a scheduled
        // tick rather than off the events, and each tick re-extends the
        // selection using the last known pointer position.

        Vector2 _lastDragPos;
        IVisualElementScheduledItem _edgeScroll;
        const float EdgeScrollZone = 24f;    // px inside the edge where it arms
        const float EdgeScrollMaxStep = 28f; // px per tick at full overshoot

        void EdgeScrollTick(Vector2 panelPos)
        {
            float speed = EdgeScrollSpeed(panelPos);
            if (Mathf.Approximately(speed, 0f)) { StopEdgeScroll(); return; }
            if (_edgeScroll != null) return;                 // already running
            _edgeScroll = schedule.Execute(() =>
            {
                if (!_selDragging) { StopEdgeScroll(); return; }
                float step = EdgeScrollSpeed(_lastDragPos);
                if (Mathf.Approximately(step, 0f)) { StopEdgeScroll(); return; }

                var offset = _scroll.scrollOffset;
                float maxY = Mathf.Max(0f, _scroll.contentContainer.worldBound.height
                                          - _scroll.contentViewport.worldBound.height);
                float y = Mathf.Clamp(offset.y + step, 0f, maxY);
                if (Mathf.Approximately(y, offset.y)) return; // already at the end
                _scroll.scrollOffset = new Vector2(offset.x, y);

                // The content moved under a stationary pointer, so the label
                // beneath it changed: extend the selection to match.
                UpdateDocSelectionAt(_lastDragPos);
            }).Every(16);
        }

        /// <summary>Pixels to scroll per tick for a pointer at
        /// <paramref name="panelPos"/>: 0 inside the viewport, ramping up to
        /// <see cref="EdgeScrollMaxStep"/> the further past an edge it goes.
        /// Negative scrolls up.</summary>
        float EdgeScrollSpeed(Vector2 panelPos)
        {
            var view = _scroll.contentViewport.worldBound;
            if (view.height <= 0f) return 0f;
            float over = panelPos.y > view.yMax - EdgeScrollZone ? panelPos.y - (view.yMax - EdgeScrollZone)
                       : panelPos.y < view.yMin + EdgeScrollZone ? panelPos.y - (view.yMin + EdgeScrollZone)
                       : 0f;
            if (Mathf.Approximately(over, 0f)) return 0f;
            float ramp = Mathf.Clamp01(Mathf.Abs(over) / 120f);
            return Mathf.Sign(over) * Mathf.Lerp(4f, EdgeScrollMaxStep, ramp);
        }

        void StopEdgeScroll()
        {
            _edgeScroll?.Pause();
            _edgeScroll = null;
        }

        /// <summary>Per-label drag tracking: the label a selection drag
        /// started in holds the pointer capture, so IT gets every move —
        /// route them into the document selection.</summary>
        void OnLabelPointerMove(PointerMoveEvent e)
        {
            if (!Locked || !_selDragging || (e.pressedButtons & 1) == 0) return;
            UpdateDocSelectionAt(e.position);
        }

        void OnLabelPointerUp(PointerUpEvent e)
        {
            _selDragging = false;
            StopEdgeScroll();
            PromoteSelectionToOverlay();
        }

        /// <summary>Ctrl+A: the whole document is the selection.</summary>
        internal void SelectAllDoc()
        {
            if (_selLabels.Count == 0) return;
            _selAnchor = 0;
            _selFocus = _selLabels.Count - 1;
            _selAnchorChar = 0;
            _selFocusChar = (_selLabels[_selFocus].text ?? string.Empty).Length;
            _selAll = true;
            ApplyDocSelection();
        }

        // The label range currently showing a selection, so a drag only
        // touches labels that actually change. Re-applying all of them per
        // mouse-move re-ran the text generator over the whole document on
        // every pointer move.
        int _tintedA = -1, _tintedB = -1;

        // Character offsets of the two ends, inside their own labels. A
        // segment boundary is an implementation detail of how the document is
        // laid out — the user sees one continuous document, so a selection
        // that crosses one stays character-precise instead of degrading to
        // whole blocks.
        int _selAnchorChar, _selFocusChar;

        // Unity renders a native selection in at most ONE TextElement: setting
        // a range on a second one silently clears the first at the next
        // repaint. A selection spanning blocks is therefore drawn here instead
        // — the usual three-part shape (partial first line, full-width middle,
        // partial last line) painted into an overlay behind the text. Native
        // selection still handles the single-block case, so double-click word
        // select and triple-click line select behave exactly as before.
        readonly VisualElement _selOverlay;
        readonly List<Rect> _selRects = new List<Rect>();
        static readonly Color SelFill = new Color(0.25f, 0.45f, 0.85f, 0.45f);

        void ApplyDocSelection()
        {
            int a = Mathf.Min(_selAnchor, _selFocus), b = Mathf.Max(_selAnchor, _selFocus);
            bool forward = _selFocus >= _selAnchor;
            int firstChar = forward ? _selAnchorChar : _selFocusChar;
            int lastChar = forward ? _selFocusChar : _selAnchorChar;

            // Single block: drawn too. Handing this case to the native
            // selection cost a full text-mesh regeneration of the label per
            // mouse-move; the overlay repaint is geometry only.
            if (a == b)
            {
                _selRects.Clear();
                AddRangeRects(_selLabels[a], Mathf.Min(firstChar, lastChar), Mathf.Max(firstChar, lastChar));
                _selOverlay.MarkDirtyRepaint();
                _tintedA = _tintedB = a;
                return;
            }

            // Spanning blocks: the native selection can only ever show one of
            // them, so drop it entirely and draw the whole span ourselves.
            // SelectNone only where a selection actually exists — it dirties
            // the label (a text-mesh regeneration) even when it is a no-op.
            for (int i = _tintedA < 0 ? a : Mathf.Min(a, _tintedA); i <= Mathf.Max(b, _tintedB) && i < _selLabels.Count; i++)
            {
                var sel = i >= 0 ? _selLabels[i].selection : null;
                if (sel != null && sel.HasSelection()) sel.SelectNone();
            }

            _selRects.Clear();
            for (int i = a; i <= b; i++)
            {
                var label = _selLabels[i];
                if (i == a) AddPartialRects(label, firstChar, toEnd: true);
                else if (i == b) AddPartialRects(label, lastChar, toEnd: false);
                else _selRects.Add(BoxOf(label));

                // Bridge the margin to the next covered block: the gap reads
                // as a selected empty line, so the highlight is one continuous
                // range instead of stripes with seams. Table cells on the same
                // row overlap vertically and produce no bridge.
                if (i == b) continue;
                var lo = BoxOf(label);
                var hi = BoxOf(_selLabels[i + 1]);
                if (hi.yMin <= lo.yMax) continue;
                float x = Mathf.Min(lo.xMin, hi.xMin);
                _selRects.Add(new Rect(x, lo.yMax, Mathf.Max(lo.xMax, hi.xMax) - x, hi.yMin - lo.yMax));
            }
            _selOverlay.MarkDirtyRepaint();
            _tintedA = a;
            _tintedB = b;
        }

        /// <summary>The label's whole box, in overlay space.</summary>
        Rect BoxOf(VisualElement label)
        {
            var tl = label.ChangeCoordinatesTo(_selOverlay, Vector2.zero);
            return new Rect(tl, label.layout.size);
        }

        /// <summary>Unity wipes a native TextElement selection on ANY focus
        /// change — clicking a tab, the window title bar, or another
        /// application all clear it (measured: even moving focus to a
        /// ToolbarButton in the same window zeroes the range). The drawn
        /// overlay has no such dependency, so the moment a single-block drag
        /// ends, its native selection is converted into overlay rects and can
        /// no longer be lost to focus traffic. Multi-block selections are
        /// drawn from the start and were never affected.</summary>
        void PromoteSelectionToOverlay()
        {
            if (_selAnchor < 0 || _selAnchor != _selFocus || _selAnchorChar == _selFocusChar) return;
            if (_selAnchor >= _selLabels.Count) return;
            var label = _selLabels[_selAnchor];
            label.selection?.SelectNone();
            _selRects.Clear();
            AddRangeRects(label, Mathf.Min(_selAnchorChar, _selFocusChar), Mathf.Max(_selAnchorChar, _selFocusChar));
            _selOverlay.MarkDirtyRepaint();
        }

        /// <summary>Label-local TOP of the line containing
        /// <paramref name="charIndex"/>: the previous line's cursor bottom,
        /// or the content top for the first line. Cursor positions sit at
        /// each line's BOTTOM, and lines within one label can have DIFFERENT
        /// heights (rich-text &lt;size&gt; headings render inside 12px body
        /// labels), so per-line tops must be measured — any per-label line
        /// height clips the tall lines and pads the short ones.</summary>
        float LineTopOf(Label l, int charIndex)
        {
            float yBottom = CursorPos(l, charIndex).y;
            int lo = 0, hi = charIndex;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (CursorPos(l, mid).y < yBottom - 0.5f) lo = mid + 1; else hi = mid;
            }
            return lo == 0 ? l.contentRect.yMin : CursorPos(l, lo - 1).y;
        }

        /// <summary>Rects for a range with both ends inside one label — a
        /// single rect on one line, else the usual three-part shape. Each
        /// line's band runs from its measured top to its cursor bottom.</summary>
        void AddRangeRects(Label label, int fromChar, int toChar)
        {
            var sel = label.selection;
            var box = BoxOf(label);
            if (sel == null) { _selRects.Add(box); return; }
            Vector2 pa = label.ChangeCoordinatesTo(_selOverlay, CursorPos(label, fromChar));
            Vector2 pb = label.ChangeCoordinatesTo(_selOverlay, CursorPos(label, toChar));
            float yOff = label.ChangeCoordinatesTo(_selOverlay, Vector2.zero).y;
            float topA = LineTopOf(label, fromChar) + yOff;
            float topB = LineTopOf(label, toChar) + yOff;
            if (Mathf.Abs(pa.y - pb.y) < 0.5f) // same line = same cursor bottom
            {
                _selRects.Add(new Rect(pa.x, topA, Mathf.Max(0f, pb.x - pa.x), pa.y - topA));
                return;
            }
            _selRects.Add(new Rect(pa.x, topA, Mathf.Max(0f, box.xMax - pa.x), pa.y - topA));
            if (topB > pa.y) _selRects.Add(new Rect(box.x, pa.y, box.width, topB - pa.y));
            _selRects.Add(new Rect(box.x, topB, Mathf.Max(0f, pb.x - box.x), pb.y - topB));
        }

        /// <summary>Rects for a partially covered label: from
        /// <paramref name="ch"/> to the label's end (<paramref name="toEnd"/>),
        /// or from its start up to <paramref name="ch"/>.</summary>
        void AddPartialRects(Label label, int ch, bool toEnd)
        {
            var sel = label.selection;
            var box = BoxOf(label);
            if (sel == null) { _selRects.Add(box); return; }

            Vector2 at = label.ChangeCoordinatesTo(_selOverlay, CursorPos(label, ch));
            float lineTop = LineTopOf(label, ch) + label.ChangeCoordinatesTo(_selOverlay, Vector2.zero).y;

            if (toEnd)
            {
                // Rest of the anchor's line, then everything below it.
                _selRects.Add(new Rect(at.x, lineTop, Mathf.Max(0f, box.xMax - at.x), at.y - lineTop));
                if (box.yMax > at.y) _selRects.Add(new Rect(box.x, at.y, box.width, box.yMax - at.y));
            }
            else
            {
                // Everything above the focus line, then the start of its line.
                if (lineTop > box.y) _selRects.Add(new Rect(box.x, box.y, box.width, lineTop - box.y));
                _selRects.Add(new Rect(box.x, lineTop, Mathf.Max(0f, at.x - box.x), at.y - lineTop));
            }
        }

        /// <summary>ITextSelection.GetCursorPositionFromStringIndex re-shapes
        /// the label's text on EVERY call — measured 1.66 ms on a 4,660-char
        /// label — and the pointer-to-character binary search makes ~12 calls
        /// per mouse-move. A label's layout is fixed between Renders, so every
        /// consumer goes through this memo instead; consecutive moves share
        /// most of their search path, so a drag settles into cache hits.
        /// Cleared in Render together with the labels.</summary>
        readonly Dictionary<Label, Dictionary<int, Vector2>> _cursorPos =
            new Dictionary<Label, Dictionary<int, Vector2>>();

        /// <summary>URL → title/alt from the markdown source, feeding the
        /// hover tooltip (destination + description). Rebuilt every Render;
        /// kept OUTSIDE the link tag so the rich-text parser only ever sees
        /// a plain URL as the linkID.</summary>
        readonly Dictionary<string, string> _linkTitles = new Dictionary<string, string>();

        Label _linkTip; // cursor-following link tooltip (see BuildUI note)

        /// <summary>Places the link tip via the shared placement (same
        /// offsets and edge handling as every other ATE tooltip).</summary>
        void MoveLinkTip(Vector2 panelPos) => AteTooltip.Place(this, _linkTip, panelPos);

        static readonly System.Text.RegularExpressions.Regex TitleRx =
            new System.Text.RegularExpressions.Regex("\"([^\"]*)\"");

        void RegisterLinkTitle(string url, string inside, string fallback)
        {
            var tm = TitleRx.Match(inside ?? "");
            string title = tm.Success ? tm.Groups[1].Value : fallback;
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title)) _linkTitles[url] = title;
        }

        Vector2 CursorPos(Label label, int index)
        {
            if (!_cursorPos.TryGetValue(label, out var map))
                _cursorPos[label] = map = new Dictionary<int, Vector2>();
            if (!map.TryGetValue(index, out var p))
                map[index] = p = CursorPositionOf(label.selection, index);
            return p;
        }

        /// <summary>Index → cursor position across Unity versions.
        /// ITextSelection.GetCursorPositionFromStringIndex is 6000.3+; on
        /// 6000.0–6000.2 the same answer comes from a set/read/restore
        /// round-trip through the cursor — verified pixel-identical to the
        /// real API (worst delta 0.00 px) and side-effect free on 6000.3,
        /// where both paths exist. Same per-call cost (~1.6 ms), so the
        /// memoization above matters equally on every version.</summary>
        static Vector2 CursorPositionOf(ITextSelection sel, int index)
        {
#if UNITY_6000_3_OR_NEWER
            return sel.GetCursorPositionFromStringIndex(index);
#else
            int prevCursor = sel.cursorIndex, prevSelect = sel.selectIndex;
            sel.cursorIndex = index;
            Vector2 p = sel.cursorPosition;
            sel.cursorIndex = prevCursor;
            sel.selectIndex = prevSelect;
            return p;
#endif
        }

        /// <summary>Line height per label, measured once from the text itself
        /// (a segment can mix sizes, so the style's font size alone would be
        /// wrong): the first index whose cursor y differs from index 0's is
        /// the start of line two. Cleared in Render.</summary>
        /// <summary>True when <paramref name="v"/> sits inside the document
        /// content — as opposed to the scroll bars or the view chrome.</summary>
        bool IsInContent(VisualElement v)
        {
            for (; v != null; v = v.parent)
                if (v == _scroll.contentContainer) return true;
            return false;
        }

        void ClearDrawnSelection()
        {
            if (_selRects.Count == 0) return;
            _selRects.Clear();
            _selOverlay.MarkDirtyRepaint();
        }

        void PaintSelection(MeshGenerationContext ctx)
        {
            // Raw quads, not painter2D: the general path tessellator cost
            // ~9 ms for a full-document selection's ~40 rects — per repaint,
            // so per mouse-move while dragging. Axis-aligned rectangles are
            // two triangles each; allocating them directly is microseconds.
            int n = 0;
            for (int i = 0; i < _selRects.Count; i++)
                if (_selRects[i].width > 0f && _selRects[i].height > 0f) n++;
            if (n == 0) return;

            var mesh = ctx.Allocate(n * 4, n * 6);
            Color32 tint = SelFill;
            ushort v = 0;
            foreach (var r in _selRects)
            {
                if (r.width <= 0f || r.height <= 0f) continue;
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = tint });
                mesh.SetNextIndex(v);
                mesh.SetNextIndex((ushort)(v + 1));
                mesh.SetNextIndex((ushort)(v + 2));
                mesh.SetNextIndex((ushort)(v + 2));
                mesh.SetNextIndex((ushort)(v + 3));
                mesh.SetNextIndex(v);
                v += 4;
            }
        }

        void ClearDocSelection()
        {
            for (int i = 0; i < _selLabels.Count; i++)
                if (i >= _tintedA && i <= _tintedB) _selLabels[i].selection?.SelectNone();
            ClearDrawnSelection();
            _tintedA = _tintedB = -1;
            _selAnchor = _selFocus = -1;
            _selAnchorChar = _selFocusChar = 0;
            _selAll = false;
        }

        /// <summary>The character offset in <paramref name="l"/> nearest a
        /// panel-space point. UI Toolkit maps index → position but not the
        /// reverse; the mapping is monotonic in y, so the inverse is a binary
        /// search — about 12 probes for a 4,000-character label.</summary>
        int CharIndexAt(Label l, Vector2 panelPos)
        {
            var sel = l.selection;
            string t = l.text ?? string.Empty;
            if (sel == null || t.Length == 0) return 0;

            Vector2 local = l.WorldToLocal(panelPos);
            // Cursor positions sit at each line's BOTTOM; the line's band is
            // [measured top, bottom]. The old fontSize-based tolerance around
            // the anchor made only a slice of tall (heading) lines hittable.
            int lo = 0, hi = t.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                Vector2 p = CursorPos(l, mid);
                bool beforePoint = p.y < local.y
                                   || (LineTopOf(l, mid) <= local.y && p.x < local.x);
                if (beforePoint) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>The label under (or vertically nearest to) a panel-space
        /// point — block flow is vertical, so Y distance is what matters.</summary>
        int LabelIndexAt(Vector2 p)
        {
            float best = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < _selLabels.Count; i++)
            {
                var r = _selLabels[i].worldBound;
                if (r.Contains(p)) return i;
                float dy = p.y < r.yMin ? r.yMin - p.y : p.y > r.yMax ? p.y - r.yMax : 0f;
                if (dy < best) { best = dy; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>The selected span as plain rendered text — exactly what is
        /// highlighted, including the partial first and last blocks. Images
        /// and tables inside the span cannot hold a text selection, so they
        /// contribute their whole plain form (alt text, tab-separated cells)
        /// the way Copy All renders them.</summary>
        internal string SelectedPlainText()
        {
            int a = Mathf.Min(_selAnchor, _selFocus), b = Mathf.Max(_selAnchor, _selFocus);
            if (a < 0 || _selLabels.Count == 0) return string.Empty;
            b = Mathf.Min(b, _selLabels.Count - 1);
            bool forward = _selFocus >= _selAnchor;
            int firstChar = forward ? _selAnchorChar : _selFocusChar;
            int lastChar = forward ? _selFocusChar : _selAnchorChar;

            var sb = new StringBuilder();
            var emitted = new HashSet<int>();   // non-segment blocks, once each
            for (int i = a; i <= b; i++)
            {
                var label = _selLabels[i];
                string piece;
                if (label.userData is Segment)
                {
                    // Selection offsets are indices into the RENDERED text, so
                    // slice that rather than the rich source or the markdown.
                    string rendered = RenderedText(label.text ?? string.Empty);
                    int from = i == a ? Mathf.Clamp(firstChar, 0, rendered.Length) : 0;
                    int to = i == b ? Mathf.Clamp(lastChar, 0, rendered.Length) : rendered.Length;
                    // Both ends in one label: firstChar/lastChar are ordered by
                    // LABEL, so a backward drag (right-to-left) arrives
                    // reversed here — order them or the slice is empty and
                    // Ctrl+C would overwrite the clipboard with "".
                    if (a == b && from > to) { int t2 = from; from = to; to = t2; }
                    if (to <= from) continue;
                    piece = rendered.Substring(from, to - from);
                }
                else
                {
                    // A table cell: emit its whole block once, not cell by cell.
                    var range = BlockRangeOf(label);
                    if (range.first < 0 || range.first >= _blocks.Count || !emitted.Add(range.first)) continue;
                    piece = PlainTextFor(_blocks[range.first]);
                }
                if (piece.Length == 0) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(piece);
            }
            return sb.ToString();
        }

        /// <summary>The tags this view emits (see RichFor / RenderBlockText).
        /// Anything else is left alone, because Unity renders an unrecognized
        /// tag literally and the offsets have to keep matching what is drawn.</summary>
        static readonly HashSet<string> RichTags = new HashSet<string>
        { "b", "i", "s", "u", "size", "color", "mark", "alpha", "noparse", "sup", "sub", "link", "align", "indent" };

        /// <summary>What the user actually sees for a rich-text string: tags
        /// removed, but the body of a &lt;noparse&gt; run kept verbatim — code
        /// blocks are wrapped in one precisely so their angle brackets survive,
        /// and stripping "&lt;int&gt;" out of a generic would shift every
        /// offset after it.</summary>
        static string RenderedText(string rich)
        {
            if (string.IsNullOrEmpty(rich) || rich.IndexOf('<') < 0) return rich ?? string.Empty;
            const string noparseEnd = "</noparse>";
            var sb = new StringBuilder(rich.Length);
            int i = 0;
            while (i < rich.Length)
            {
                if (rich[i] != '<') { sb.Append(rich[i++]); continue; }
                int close = rich.IndexOf('>', i + 1);
                if (close < 0) { sb.Append(rich, i, rich.Length - i); break; }

                string inner = rich.Substring(i + 1, close - i - 1);
                bool closing = inner.StartsWith("/", StringComparison.Ordinal);
                string name = closing ? inner.Substring(1) : inner;
                int cut = name.IndexOfAny(new[] { '=', ' ' });
                if (cut >= 0) name = name.Substring(0, cut);
                if (!RichTags.Contains(name.ToLowerInvariant())) { sb.Append(rich[i++]); continue; }

                if (!closing && name.Equals("noparse", StringComparison.OrdinalIgnoreCase))
                {
                    int end = rich.IndexOf(noparseEnd, close + 1, StringComparison.Ordinal);
                    int bodyEnd = end < 0 ? rich.Length : end;
                    sb.Append(rich, close + 1, bodyEnd - close - 1);
                    i = end < 0 ? rich.Length : end + noparseEnd.Length;
                    continue;
                }
                i = close + 1;
            }
            return sb.ToString();
        }

        public void SetPalette(HighlightTheme.Palette palette)
        {
            _palette = palette;
            style.backgroundColor = palette.BackgroundColor;
            Render(_text);
        }

        public void Render(string text)
        {
            _text = text ?? string.Empty;
            _highlighted = null; // its element is discarded with the pool
            _hlSegment = null;
            ParseBlocks();
            var c = _scroll.contentContainer;
            c.Clear();
            _selRects.Clear();
            _cursorPos.Clear();         // the labels they were measured on are gone
            _linkTitles.Clear();        // re-registered as InlineToRich re-runs
            if (_linkTip != null) _linkTip.style.display = DisplayStyle.None;
            c.Add(_selOverlay);         // first child: paints behind the blocks
            _selDragging = false;
            StopEdgeScroll();           // the labels it was scrolling toward are gone
            _selLabels.Clear();
            _selAnchor = _selFocus = -1;
            _tintedA = _tintedB = -1;   // the labels that carried the tint are gone
            _selAll = false;
            _segments.Clear();
            if (Locked)
            {
                // Locked = READING mode: runs of text blocks render as ONE
                // selectable rich-text label each (a "segment"), so native
                // selection and copy span headings, paragraphs, quotes, code,
                // lists, and rules continuously — like a normal document.
                // Only images and tables (which cannot live inside a rich
                // text) interrupt a segment and keep their real elements;
                // drags across THEM fall back to the block-span layer.
                RenderLockedSegments(c);
            }
            else
            {
                // Unlocked keeps the per-block layout for click-to-edit.
                foreach (var block in _blocks)
                    c.Add(BuildBlockElement(block));
            }
        }

        // ---------- Locked layout: segments of continuous rich text ----------

        sealed class Segment
        {
            public int FirstBlock, LastBlock;   // indices into _blocks
            public Label Label;
            public readonly List<(Block block, int start, int length)> Ranges =
                new List<(Block, int, int)>();  // rich-string range per block
        }
        readonly List<Segment> _segments = new List<Segment>();

        /// <summary>Maximum rich-text length of one segment label.
        ///
        /// A segment is one TextElement, and Unity regenerates an element's
        /// WHOLE text mesh whenever its selection changes — so a drag inside a
        /// segment costs one full regeneration per pointer-move. That cost is
        /// linear in the segment's length; measured on 6000.3.19f1 with this
        /// package's RELEASE-NOTES.md (a document of nothing but text blocks,
        /// which merged into a single 40,768-character label):
        ///
        ///     500 chars 2.6 ms | 2,000 6.4 ms | 8,000 21.8 ms
        ///     16,000 45.3 ms | 32,000 97.2 ms | 40,768 124.7 ms
        ///
        /// At 125 ms per mouse-move, selecting text felt like dragging through
        /// treacle. Capping at 2,000 keeps a move near 6 ms.
        ///
        /// The cost of the cap is selection seams: NATIVE char-precise
        /// selection cannot cross a TextElement boundary. Splitting only
        /// BETWEEN blocks means selection inside any single block is unchanged,
        /// and multi-block drags fall through to the block-span layer, which
        /// already handles the seams that images and tables have always
        /// created.</summary>
        const int MaxSegmentChars = 2000;

        void RenderLockedSegments(VisualElement c)
        {
            Color text = _palette?.TextColor ?? Color.white;
            Segment seg = null;
            var sb = new StringBuilder();
            void Flush()
            {
                if (seg == null) return;
                var label = new Label(sb.ToString()) { enableRichText = true };
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.color = text;
                label.style.marginBottom = 8;
                label.userData = seg;
                seg.Label = label;
                c.Add(label);
                _segments.Add(seg);
                sb.Clear();
                seg = null;
            }
            for (int i = 0; i < _blocks.Count; i++)
            {
                var b = _blocks[i];
                if (b.Kind == "image" || b.Kind == "table")
                {
                    Flush();
                    var el = b.Kind == "image" ? BuildImageElement(b, text) : BuildTable(b, text);
                    el.userData = b;
                    c.Add(el);
                    continue;
                }
                string rich = RichFor(b, text);
                // Break BEFORE a block that would push this segment past the
                // cap, never inside one: splitting a block would cut its
                // rich-text tags in half. A single block over the cap stays
                // whole and simply costs what it costs.
                if (seg != null && sb.Length + rich.Length > MaxSegmentChars) Flush();
                if (seg == null) seg = new Segment { FirstBlock = i };
                if (sb.Length > 0) sb.Append("\n\n");
                int start = sb.Length;
                sb.Append(rich);
                seg.Ranges.Add((b, start, sb.Length - start));
                seg.LastBlock = i;
            }
            Flush();

            // Every label (segments + table cells) is selectable and feeds
            // the fallback block-span layer for drags across tables/images.
            c.Query<Label>().ForEach(l =>
            {
                l.focusable = true;
                l.selection.isSelectable = true;
                _selLabels.Add(l);
                l.RegisterCallback<PointerMoveEvent>(OnLabelPointerMove);
                l.RegisterCallback<PointerUpEvent>(OnLabelPointerUp);
            });
        }

        /// <summary>One block's rich-text form INSIDE a segment label —
        /// reproducing the per-block styling with rich-text tags.</summary>
        string RichFor(Block b, Color text)
        {
            switch (b.Kind)
            {
                case "h1": return "<size=24><b>" + RenderBlockText(b) + "</b></size>";
                case "h2": return "<size=20><b>" + RenderBlockText(b) + "</b></size>";
                case "h3": return "<size=17><b>" + RenderBlockText(b) + "</b></size>";
                case "h4": case "h5": case "h6":
                    return "<size=14><b>" + RenderBlockText(b) + "</b></size>";
                case "hr":
                    return "<color=#" + Hex(text, 0.35f) + ">" + new string('─', 40) + "</color>";
                case "code":
                {
                    // The box background becomes a <mark> run; <noparse>
                    // keeps code's angle brackets out of the tag parser.
                    string body = StripFences(b.Source).Replace("</noparse>", "<\u200B/noparse>");
                    Color codeCol = _palette != null ? ParseHex(_palette.String, text) : text;
                    return "<mark=#" + Hex(text, 0.07f) + "><color=#" + Hex(codeCol) + "><noparse>"
                        + body + "</noparse></color></mark>";
                }
                case "quote":
                {
                    // The left border rule becomes a ▎ glyph per line.
                    var q = new StringBuilder();
                    foreach (var line in RenderBlockText(b).Split('\n'))
                    {
                        if (q.Length > 0) q.Append('\n');
                        q.Append("<color=#").Append(Hex(text, 0.4f)).Append(">▎</color> ").Append(line);
                    }
                    return "<alpha=#D9>" + q + "<alpha=#FF>";
                }
                default:
                    return RenderBlockText(b);
            }
        }

        static string Hex(Color c, float alphaMul = 1f) =>
            ColorUtility.ToHtmlStringRGBA(new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * alphaMul)));

        /// <summary>The [first, last] block-index range a label stands for:
        /// a whole segment, or the single block of a table cell / image.</summary>
        (int first, int last) BlockRangeOf(Label l)
        {
            if (l.userData is Segment s) return (s.FirstBlock, s.LastBlock);
            var b = BlockFor(l);
            int i = b != null ? _blocks.IndexOf(b) : -1;
            return (i, i);
        }

        // ---------- Search highlight ----------

        VisualElement _highlighted;
        StyleColor _highlightedPrevBg;

        /// <summary>Scrolls to and highlights the block containing document
        /// span [start, end) — how Find shows its match while the rendered
        /// view is active (the code view carries the actual selection but is
        /// hidden). The highlight moves on the next call and clears on
        /// re-render.</summary>
        Segment _hlSegment;      // locked mode: segment carrying a <mark>
        string _hlOriginal;      // its pre-highlight rich text

        public void HighlightSpan(int start, int end)
        {
            ClearHighlight();
            Block target = null;
            foreach (var b in _blocks)
                if (end >= b.StartOffset && start <= b.EndOffset) { target = b; break; }
            if (target == null) return;
            // Locked segments: the block lives INSIDE a big label — wrap its
            // rich-text range in a temporary <mark> and scroll to it (the
            // range positions are known exactly, so no index guessing).
            foreach (var seg in _segments)
            {
                foreach (var r in seg.Ranges)
                {
                    if (!ReferenceEquals(r.block, target)) continue;
                    _hlSegment = seg;
                    _hlOriginal = seg.Label.text;
                    seg.Label.text = _hlOriginal
                        .Insert(r.start + r.length, "</mark>")
                        .Insert(r.start, "<mark=#F5C8424D>");
                    float y = seg.Label.layout.y;
                    try { y += CursorPositionOf(seg.Label.selection, r.start).y; }
                    catch (System.Exception) { }
                    _scroll.scrollOffset = new Vector2(0, Mathf.Max(0, y - 40));
                    return;
                }
            }
            // Per-block elements (unlocked mode, and tables/images in locked).
            foreach (var child in _scroll.contentContainer.Children())
            {
                if (!ReferenceEquals(child.userData, target)) continue;
                _highlighted = child;
                _highlightedPrevBg = child.style.backgroundColor;
                child.style.backgroundColor = new Color(1f, 0.8f, 0.2f, 0.18f);
                _scroll.ScrollTo(child);
                break;
            }
        }

        void ClearHighlight()
        {
            if (_hlSegment != null)
            {
                if (_hlSegment.Label != null) _hlSegment.Label.text = _hlOriginal;
                _hlSegment = null;
            }
            if (_highlighted == null) return;
            _highlighted.style.backgroundColor = _highlightedPrevBg;
            _highlighted = null;
        }

        // ---------- Block parsing ----------

        void ParseBlocks()
        {
            _blocks.Clear();
            string[] lines = _text.Split('\n');
            var starts = new int[lines.Length + 1];
            for (int i = 0; i < lines.Length; i++) starts[i + 1] = starts[i] + lines[i].Length + 1;

            int li = 0;
            while (li < lines.Length)
            {
                string t = lines[li].TrimStart();
                if (t.Length == 0) { li++; continue; }

                int first = li;
                string kind;
                if (t.StartsWith("```"))
                {
                    kind = "code";
                    li++;
                    while (li < lines.Length && !lines[li].TrimStart().StartsWith("```")) li++;
                    if (li < lines.Length) li++; // closing fence
                }
                else if (t.StartsWith("#"))
                {
                    int level = 0;
                    while (level < t.Length && t[level] == '#') level++;
                    kind = "h" + Mathf.Clamp(level, 1, 6);
                    li++;
                }
                else if (MarkdownClassifier.IsHorizontalRule(t))
                {
                    kind = "hr";
                    li++;
                }
                else if (t.StartsWith(">"))
                {
                    kind = "quote";
                    while (li < lines.Length && lines[li].TrimStart().StartsWith(">")) li++;
                }
                else if (t.StartsWith("|"))
                {
                    kind = "table";
                    while (li < lines.Length && lines[li].TrimStart().StartsWith("|")) li++;
                }
                else if (t.StartsWith("![") && t.Contains("]("))
                {
                    kind = "image";
                    li++;
                }
                else if (MarkdownClassifier.IsListMarker(t, out _))
                {
                    kind = "list";
                    while (li < lines.Length && lines[li].Trim().Length > 0 &&
                           (MarkdownClassifier.IsListMarker(lines[li].TrimStart(), out _) || lines[li].StartsWith("  ")))
                        li++;
                }
                else
                {
                    kind = "p";
                    while (li < lines.Length && lines[li].Trim().Length > 0 &&
                           !lines[li].TrimStart().StartsWith("#") && !lines[li].TrimStart().StartsWith("```") &&
                           !lines[li].TrimStart().StartsWith(">") &&
                           !lines[li].TrimStart().StartsWith("|") &&
                           !MarkdownClassifier.IsListMarker(lines[li].TrimStart(), out _) &&
                           !lines[li].TrimStart().StartsWith("![") &&
                           !MarkdownClassifier.IsHorizontalRule(lines[li].TrimStart()))
                        li++;
                }

                int endLine = Mathf.Max(first, li - 1);
                int endOffset = Mathf.Min(_text.Length, starts[endLine] + lines[endLine].Length);
                _blocks.Add(new Block
                {
                    StartOffset = starts[first],
                    EndOffset = endOffset,
                    Source = _text.Substring(starts[first], endOffset - starts[first]),
                    Kind = kind
                });
            }
        }

        // ---------- Rendering ----------

        /// <summary>Renders a standalone image line "![alt](path)". Local
        /// paths (absolute, or relative to <see cref="BaseDir"/>) load into a
        /// real Image; anything unresolvable falls back to an "alt — path"
        /// placeholder label. Remote URLs are not fetched.</summary>
        VisualElement BuildImageElement(Block block, Color text)
        {
            string src = block.Source.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(src, @"^!\[(?<alt>[^\]]*)\]\((?<path>[^)\s]+)[^)]*\)");
            string alt = m.Success ? m.Groups["alt"].Value : string.Empty;
            string path = m.Success ? m.Groups["path"].Value : null;
            Texture2D tex = null;
            if (path != null && !path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string full = System.IO.Path.IsPathRooted(path) ? path
                        : BaseDir != null ? System.IO.Path.Combine(BaseDir, path) : path;
                    if (System.IO.File.Exists(full))
                    {
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!tex.LoadImage(System.IO.File.ReadAllBytes(full))) tex = null;
                    }
                }
                catch (Exception) { tex = null; }
            }
            if (tex == null)
            {
                var ph = new Label("🖼 " + (alt.Length > 0 ? alt + " — " : "") + (path ?? src));
                ph.style.unityFontStyleAndWeight = FontStyle.Italic;
                ph.style.color = new Color(text.r, text.g, text.b, 0.6f);
                ph.style.marginBottom = 8;
                return ph;
            }
            var box = new VisualElement();
            box.style.marginBottom = 8;
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
            img.style.width = tex.width;
            img.style.height = tex.height;
            img.style.maxWidth = Length.Percent(100);
            img.style.alignSelf = Align.FlexStart;
            box.Add(img);
            if (alt.Length > 0)
            {
                var cap = new Label(alt);
                cap.style.color = new Color(text.r, text.g, text.b, 0.55f);
                cap.style.fontSize = 10;
                box.Add(cap);
            }
            return box;
        }

        VisualElement BuildBlockElement(Block block)
        {
            VisualElement el;
            Color text = _palette?.TextColor ?? Color.white;

            if (block.Kind == "hr")
            {
                el = new VisualElement();
                el.style.height = 1;
                el.style.backgroundColor = new Color(text.r, text.g, text.b, 0.35f);
                el.style.marginTop = 8;
                el.style.marginBottom = 8;
            }
            else if (block.Kind == "image")
            {
                el = BuildImageElement(block, text);
            }
            else if (block.Kind == "code")
            {
                var box = new VisualElement();
                box.style.backgroundColor = new Color(text.r, text.g, text.b, 0.07f);
                box.style.paddingLeft = 10;
                box.style.paddingTop = 6;
                box.style.paddingBottom = 6;
                box.style.marginBottom = 8;
                var body = StripFences(block.Source);
                var label = new Label(body) { enableRichText = false };
                label.AddToClassList("code-line");
                label.style.whiteSpace = WhiteSpace.Pre;
                label.style.color = _palette != null ? ParseHex(_palette.String, text) : text;
                box.Add(label);
                el = box;
            }
            else if (block.Kind == "table")
            {
                el = BuildTable(block, text);
            }
            else
            {
                var label = new Label { enableRichText = true };
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.color = text;
                label.style.marginBottom = 8;
                switch (block.Kind)
                {
                    case "h1": label.style.fontSize = 24; label.style.unityFontStyleAndWeight = FontStyle.Bold; break;
                    case "h2": label.style.fontSize = 20; label.style.unityFontStyleAndWeight = FontStyle.Bold; break;
                    case "h3": label.style.fontSize = 17; label.style.unityFontStyleAndWeight = FontStyle.Bold; break;
                    case "h4": case "h5": case "h6":
                        label.style.fontSize = 14; label.style.unityFontStyleAndWeight = FontStyle.Bold; break;
                    case "quote":
                        label.style.borderLeftWidth = 3;
                        label.style.borderLeftColor = new Color(text.r, text.g, text.b, 0.4f);
                        label.style.paddingLeft = 10;
                        label.style.opacity = 0.85f;
                        break;
                }
                label.text = RenderBlockText(block);
                el = label;
            }

            el.userData = block; // context menu maps the clicked element back

            // Click-to-edit: swap the block for an inline source editor.
            // Ctrl+Click is reserved for links (handled by the link-tag
            // events) and must not open the editor. Locked mode never edits —
            // the event must keep propagating so text selection works.
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (Locked || e.button != 0 || e.ctrlKey || e.commandKey) return;
                BeginBlockEdit(el, block);
                e.StopPropagation();
            });
            return el;
        }

        VisualElement BuildTable(Block block, Color text)
        {
            var table = new VisualElement();
            table.style.marginBottom = 8;
            var borderColor = new Color(text.r, text.g, text.b, 0.3f);
            bool headerDone = false;
            foreach (var raw in block.Source.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("|")) continue;
                string inner = line.Trim('|');
                // separator row (|---|:--:|) ends the header
                if (!headerDone && inner.Contains("-") &&
                    inner.Replace("-", "").Replace(":", "").Replace("|", "").Trim().Length == 0)
                {
                    headerDone = true;
                    continue;
                }
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                foreach (var cell in inner.Split('|'))
                {
                    var cl = new Label(InlineToRich(cell.Trim())) { enableRichText = true };
                    cl.style.flexGrow = 1;
                    cl.style.flexBasis = 0;
                    cl.style.color = text;
                    cl.style.paddingLeft = 6;
                    cl.style.paddingRight = 6;
                    cl.style.paddingTop = 2;
                    cl.style.paddingBottom = 2;
                    cl.style.borderBottomWidth = 1;
                    cl.style.borderBottomColor = borderColor;
                    if (!headerDone) cl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    row.Add(cl);
                }
                table.Add(row);
            }
            return table;
        }

        // ---------- Formatting actions ----------

        /// <summary>Applies a formatting action: into the open block editor
        /// when one exists (wrapping the selection or transforming lines),
        /// otherwise raises onInsertBlock with a template for a new block.</summary>
        public void ApplyFormat(string id)
        {
            if (Locked) return; // read-only: formatting is an edit
            if (_activeEditor == null)
            {
                string template = TemplateFor(id);
                if (template != null) onInsertBlock?.Invoke(template);
                return;
            }

            if (TryGetInlineWrap(id, out string pre, out string post, out string ph))
            {
                var ed = _activeEditor;
                string v = ed.value;
                int a = Mathf.Clamp(Mathf.Min(ed.cursorIndex, ed.selectIndex), 0, v.Length);
                int b = Mathf.Clamp(Mathf.Max(ed.cursorIndex, ed.selectIndex), a, v.Length);
                string innerText = b > a ? v.Substring(a, b - a) : ph;
                ed.value = v.Substring(0, a) + pre + innerText + post + v.Substring(b);
                ed.selectIndex = a + pre.Length;
                ed.cursorIndex = a + pre.Length + innerText.Length;
            }
            else if (id == "hr" || id == "table")
            {
                var ed = _activeEditor;
                string v = ed.value;
                int a = Mathf.Clamp(Mathf.Min(ed.cursorIndex, ed.selectIndex), 0, v.Length);
                string ins = "\n" + TemplateFor(id) + "\n";
                ed.value = v.Substring(0, a) + ins + v.Substring(a);
                ed.cursorIndex = ed.selectIndex = a + ins.Length;
            }
            else
            {
                string transformed = TransformLines(id, _activeEditor.value);
                if (transformed != null) _activeEditor.value = transformed;
            }
            var focusEd = _activeEditor;
            focusEd.schedule.Execute(() => focusEd.Focus()).ExecuteLater(0);
        }

        /// <summary>Template block source for a formatting id (new-block path).</summary>
        internal static string TemplateFor(string id) => id switch
        {
            "h1" => "# Heading",
            "h2" => "## Heading",
            "h3" => "### Heading",
            "bold" => "**bold text**",
            "italic" => "*italic text*",
            "strike" => "~~struck text~~",
            "code" => "`code`",
            "link" => "[link text](https://)",
            "image" => "![alt text](https://)",
            "ul" => "- item",
            "ol" => "1. item",
            "task" => "- [ ] task",
            "quote" => "> quote",
            "codeblock" => "```\ncode\n```",
            "table" => "| Column A | Column B |\n|---|---|\n| a | b |",
            "hr" => "---",
            _ => null
        };

        /// <summary>Inline styles wrap the selection (or a placeholder).</summary>
        internal static bool TryGetInlineWrap(string id, out string pre, out string post, out string placeholder)
        {
            (pre, post, placeholder) = id switch
            {
                "bold" => ("**", "**", "bold text"),
                "italic" => ("*", "*", "italic text"),
                "strike" => ("~~", "~~", "struck text"),
                "code" => ("`", "`", "code"),
                "link" => ("[", "](https://)", "link text"),
                "image" => ("![", "](https://)", "alt text"),
                _ => (null, null, null)
            };
            return pre != null;
        }

        /// <summary>Line-level transforms (headings, list/quote prefixes,
        /// fence wrap) applied to a block of text; null if id is not one.</summary>
        internal static string TransformLines(string id, string text)
        {
            var lines = text.Split('\n');
            switch (id)
            {
                case "h1": case "h2": case "h3":
                    int level = id[1] - '0';
                    lines[0] = new string('#', level) + " " + lines[0].TrimStart('#').TrimStart();
                    return string.Join("\n", lines);
                case "ul": return PrefixLines(lines, "- ");
                case "task": return PrefixLines(lines, "- [ ] ");
                case "quote": return PrefixLines(lines, "> ");
                case "ol":
                    int num = 1;
                    for (int i = 0; i < lines.Length; i++)
                        if (lines[i].Trim().Length > 0)
                            lines[i] = (num++) + ". " + lines[i].TrimStart();
                    return string.Join("\n", lines);
                case "codeblock": return "```\n" + text + "\n```";
                default: return null;
            }
        }

        static string PrefixLines(string[] lines, string prefix)
        {
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0 && !lines[i].TrimStart().StartsWith(prefix.TrimEnd()))
                    lines[i] = prefix + lines[i].TrimStart();
            return string.Join("\n", lines);
        }

        string RenderBlockText(Block block) => InlineToRich(StripBlockMarkers(block));

        /// <summary>Block source minus its block-level markers (#, &gt;, list
        /// bullets → glyphs) — the text part both the rich renderer and the
        /// plain-text clipboard path start from.</summary>
        static string StripBlockMarkers(Block block)
        {
            string src = block.Source;
            if (block.Kind.StartsWith("h"))
                src = src.TrimStart().TrimStart('#').TrimStart();
            else if (block.Kind == "quote")
            {
                var sb = new StringBuilder();
                foreach (var l in src.Split('\n'))
                {
                    var t = l.TrimStart();
                    sb.Append(t.StartsWith(">") ? t.Substring(1).TrimStart() : t).Append('\n');
                }
                src = sb.ToString().TrimEnd('\n');
            }
            else if (block.Kind == "list")
            {
                var sb = new StringBuilder();
                foreach (var l in src.Split('\n'))
                {
                    string t = l.TrimStart();
                    int indent = l.Length - t.Length;
                    if (MarkdownClassifier.IsListMarker(t, out int ml))
                    {
                        string rest = t.Substring(ml).TrimStart();
                        string marker = char.IsDigit(t[0]) ? t.Substring(0, ml) : "•";
                        if (rest.StartsWith("[ ] ")) { marker = "☐"; rest = rest.Substring(4); }
                        else if (rest.StartsWith("[x] ") || rest.StartsWith("[X] ")) { marker = "☑"; rest = rest.Substring(4); }
                        sb.Append(new string(' ', indent)).Append(marker).Append(' ').Append(rest);
                    }
                    else sb.Append(l);
                    sb.Append('\n');
                }
                src = sb.ToString().TrimEnd('\n');
            }
            return src;
        }

        // ---------- Plain-text copy (locked mode) ----------

        /// <summary>The whole rendered document as plain text: blocks in
        /// order, blank-line separated, no markers or rich-text tags.</summary>
        public string PlainText()
        {
            var sb = new StringBuilder(_text.Length);
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (i > 0) sb.Append("\n\n");
                sb.Append(PlainTextFor(_blocks[i]));
            }
            return sb.ToString();
        }

        /// <summary>Clipboard form of one block: the rendered text without
        /// formatting. Links keep their URL — "text (url)" — so addresses
        /// survive the copy; table cells are tab-separated.</summary>
        string PlainTextFor(Block block)
        {
            switch (block.Kind)
            {
                case "hr": return block.Source.Trim();
                case "code": return StripFences(block.Source);
                case "image":
                {
                    var m = System.Text.RegularExpressions.Regex.Match(block.Source.Trim(),
                        @"^!\[(?<alt>[^\]]*)\]\((?<path>[^)\s]+)[^)]*\)");
                    if (!m.Success) return block.Source.Trim();
                    string alt = m.Groups["alt"].Value, path = m.Groups["path"].Value;
                    return alt.Length > 0 ? alt + " (" + path + ")" : path;
                }
                case "table":
                {
                    var sb = new StringBuilder();
                    foreach (var raw in block.Source.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (!line.StartsWith("|")) continue;
                        string inner = line.Trim('|');
                        if (inner.Contains("-") && // separator row, not content
                            inner.Replace("-", "").Replace(":", "").Replace("|", "").Trim().Length == 0)
                            continue;
                        var cells = inner.Split('|');
                        for (int i = 0; i < cells.Length; i++)
                        {
                            if (i > 0) sb.Append('\t');
                            sb.Append(InlineToPlain(cells[i].Trim()));
                        }
                        sb.Append('\n');
                    }
                    return sb.ToString().TrimEnd('\n');
                }
                default: return InlineToPlain(StripBlockMarkers(block));
            }
        }

        /// <summary>Inline markdown → plain clipboard text: emphasis/code
        /// markers dropped, "[text](url)" becomes "text (url)", images become
        /// "alt (path)", bare URLs and everything else copy as displayed.</summary>
        internal static string InlineToPlain(string src)
        {
            var sb = new StringBuilder(src.Length);
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (c == '~' && i + 1 < n && src[i + 1] == '~')
                {
                    int send = src.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (send > i) { sb.Append(src, i + 2, send - i - 2); i = send + 2; continue; }
                }
                else if (c == '!' && i + 1 < n && src[i + 1] == '[')
                {
                    int close = src.IndexOf(']', i + 2);
                    if (close > i && close + 1 < n && src[close + 1] == '(')
                    {
                        int urlEnd = src.IndexOf(')', close + 2);
                        if (urlEnd > close)
                        {
                            string alt = src.Substring(i + 2, close - i - 2);
                            string path = src.Substring(close + 2, urlEnd - close - 2).Split(' ')[0];
                            sb.Append(alt.Length > 0 ? alt + " (" + path + ")" : path);
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
                if (c == '`')
                {
                    int end = src.IndexOf('`', i + 1);
                    if (end > i) { sb.Append(src, i + 1, end - i - 1); i = end + 1; continue; }
                }
                else if (c == '*' && i + 1 < n && src[i + 1] == '*')
                {
                    int end = src.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i) { sb.Append(src, i + 2, end - i - 2); i = end + 2; continue; }
                }
                else if ((c == '*' || c == '_') && i + 1 < n && src[i + 1] != ' ' && src[i + 1] != c)
                {
                    int end = src.IndexOf(c, i + 1);
                    if (end > i) { sb.Append(src, i + 1, end - i - 1); i = end + 1; continue; }
                }
                else if (c == '[')
                {
                    // Image-in-link ([![alt](badge)](target)): alt (target).
                    if (i + 1 < n && src[i + 1] == '!')
                    {
                        var nested = System.Text.RegularExpressions.Regex.Match(src.Substring(i),
                            @"^\[!\[(?<alt>[^\]]*)\]\([^)]*\)\]\((?<target>[^)\s]+)[^)]*\)");
                        if (nested.Success)
                        {
                            string alt2 = nested.Groups["alt"].Value;
                            string target2 = nested.Groups["target"].Value;
                            sb.Append(alt2.Length > 0 ? alt2 : target2);
                            if (target2.Length > 0 && target2 != alt2) sb.Append(" (").Append(target2).Append(')');
                            i += nested.Length;
                            continue;
                        }
                    }
                    int close = src.IndexOf(']', i + 1);
                    if (close > i && close + 1 < n && src[close + 1] == '(')
                    {
                        int urlEnd = src.IndexOf(')', close + 2);
                        if (urlEnd > close)
                        {
                            string label = src.Substring(i + 1, close - i - 1);
                            string url = src.Substring(close + 2, urlEnd - close - 2).Split(' ')[0];
                            sb.Append(label);
                            if (url.Length > 0 && url != label) sb.Append(" (").Append(url).Append(')');
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>Inline markdown → UITK rich text: **bold**, *italic*,
        /// `code`, [text](url). Literal '&lt;' is escaped via noparse.</summary>
        internal string InlineToRich(string src)
        {
            var sb = new StringBuilder(src.Length + 32);
            int i = 0, n = src.Length;
            string codeColor = _palette?.String ?? "#CE9178";
            string linkColor = _palette?.Method ?? "#DCDCAA";
            string imgColor = _palette?.Type ?? "#4EC9B0";
            while (i < n)
            {
                char c = src[i];
                if (c == '~' && i + 1 < n && src[i + 1] == '~')
                {
                    int send = src.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (send > i)
                    {
                        sb.Append("<s>");
                        AppendEscaped(sb, src.Substring(i + 2, send - i - 2));
                        sb.Append("</s>");
                        i = send + 2;
                        continue;
                    }
                }
                else if (c == '!' && i + 1 < n && src[i + 1] == '[')
                {
                    int close = src.IndexOf(']', i + 2);
                    if (close > i && close + 1 < n && src[close + 1] == '(')
                    {
                        int urlEnd = src.IndexOf(')', close + 2);
                        if (urlEnd > close)
                        {
                            // Clickable when the image URL is openable — the
                            // picture itself can't render inline.
                            string imgInside = src.Substring(close + 2, urlEnd - close - 2);
                            string imgUrl = imgInside.Split(' ')[0];
                            string imgAlt = src.Substring(i + 2, close - i - 2);
                            RegisterLinkTitle(imgUrl, imgInside, imgAlt);
                            bool imgOpen = IsOpenableUrl(imgUrl);
                            if (imgOpen) sb.Append("<link=\"").Append(imgUrl).Append("\">");
                            sb.Append("<color=").Append(imgColor).Append(">[img] <u>");
                            AppendEscaped(sb, imgAlt);
                            sb.Append("</u></color>");
                            if (imgOpen) sb.Append("</link>");
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
                if (c == '`')
                {
                    int end = src.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        sb.Append("<color=").Append(codeColor).Append('>');
                        AppendEscaped(sb, src.Substring(i + 1, end - i - 1));
                        sb.Append("</color>");
                        i = end + 1;
                        continue;
                    }
                }
                else if (c == '*' && i + 1 < n && src[i + 1] == '*')
                {
                    int end = src.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        sb.Append("<b>");
                        AppendEscaped(sb, src.Substring(i + 2, end - i - 2));
                        sb.Append("</b>");
                        i = end + 2;
                        continue;
                    }
                }
                else if ((c == '*' || c == '_') && i + 1 < n && src[i + 1] != ' ' && src[i + 1] != c)
                {
                    int end = src.IndexOf(c, i + 1);
                    if (end > i)
                    {
                        sb.Append("<i>");
                        AppendEscaped(sb, src.Substring(i + 1, end - i - 1));
                        sb.Append("</i>");
                        i = end + 1;
                        continue;
                    }
                }
                else if (c == '[')
                {
                    // Image-in-link — [![alt](badge)](target), the README
                    // badge idiom. The badge cannot render inline, so ALT is
                    // the link text and TARGET the destination. Without this
                    // the first ']' found belonged to the nested image and
                    // the line shredded into raw syntax.
                    if (i + 1 < n && src[i + 1] == '!')
                    {
                        var nested = System.Text.RegularExpressions.Regex.Match(src.Substring(i),
                            @"^\[!\[(?<alt>[^\]]*)\]\([^)]*\)\]\((?<target>[^)\s]+)(?<rest>[^)]*)\)");
                        if (nested.Success)
                        {
                            string target = nested.Groups["target"].Value;
                            string alt = nested.Groups["alt"].Value;
                            bool openable = IsOpenableUrl(target);
                            if (!openable)
                            {
                                string local = ResolveLocalLink(target);
                                if (local != null) { target = local; openable = true; }
                            }
                            if (openable)
                            {
                                RegisterLinkTitle(target, nested.Groups["rest"].Value, alt);
                                sb.Append("<link=\"").Append(target).Append("\">");
                                sb.Append("<color=").Append(linkColor).Append("><u>");
                                AppendEscaped(sb, alt.Length > 0 ? alt : target);
                                sb.Append("</u></color></link>");
                            }
                            else
                                AppendEscaped(sb, alt.Length > 0 ? alt : target);
                            i += nested.Length;
                            continue;
                        }
                    }
                    int close = src.IndexOf(']', i + 1);
                    if (close > i && close + 1 < n && src[close + 1] == '(')
                    {
                        int urlEnd = src.IndexOf(')', close + 2);
                        if (urlEnd > close)
                        {
                            string inside = src.Substring(close + 2, urlEnd - close - 2);
                            string target = inside.Split(' ')[0];
                            string text = src.Substring(i + 1, close - i - 1);
                            bool openable = IsOpenableUrl(target);
                            if (!openable)
                            {
                                // Relative file links open as ATE tabs.
                                string local = ResolveLocalLink(target);
                                if (local != null) { target = local; openable = true; }
                            }
                            if (openable)
                            {
                                RegisterLinkTitle(target, inside, text);
                                sb.Append("<link=\"").Append(target).Append("\">");
                                sb.Append("<color=").Append(linkColor).Append("><u>");
                                AppendEscaped(sb, text);
                                sb.Append("</u></color></link>");
                            }
                            else
                            {
                                // Nothing to open — don't dress it as a link.
                                AppendEscaped(sb, text);
                            }
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
                if ((c == 'h' || c == 'm') && (i == 0 || !char.IsLetterOrDigit(src[i - 1])))
                {
                    // Bare URL: linkify http(s)/mailto runs in plain text.
                    string rest = src.Substring(i);
                    if (rest.StartsWith("http://") || rest.StartsWith("https://") ||
                        rest.StartsWith("mailto:"))
                    {
                        int end = i;
                        while (end < n && !char.IsWhiteSpace(src[end]) &&
                               src[end] != ')' && src[end] != ']' && src[end] != '>' &&
                               src[end] != '"' && src[end] != '\'') end++;
                        while (end > i && ".,;:!?".IndexOf(src[end - 1]) >= 0) end--;
                        string url = src.Substring(i, end - i);
                        sb.Append("<link=\"").Append(url).Append("\">")
                          .Append("<color=").Append(linkColor).Append("><u>");
                        AppendEscaped(sb, url);
                        sb.Append("</u></color></link>");
                        i = end;
                        continue;
                    }
                }
                if (c == '<') sb.Append("<noparse><</noparse>");
                else sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        static bool IsOpenableUrl(string u) =>
            u.StartsWith("http://", StringComparison.Ordinal) ||
            u.StartsWith("https://", StringComparison.Ordinal) ||
            u.StartsWith("mailto:", StringComparison.Ordinal) ||
            u.StartsWith("ate://", StringComparison.Ordinal); // file:line links (security reports) open in ATE

        /// <summary>A relative link target ([text](CHANGELOG.md)) resolved
        /// against the document's directory — the absolute path when the
        /// file exists, else null (anchors and dead paths stay null).</summary>
        string ResolveLocalLink(string target)
        {
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(BaseDir)) return null;
            if (target.StartsWith("#", StringComparison.Ordinal)) return null;
            try
            {
                string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(BaseDir, target));
                return System.IO.File.Exists(full) ? full : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>One open path for every link activation (Ctrl+Click and
        /// the context menu): ate:// opens in ATE, an existing local file
        /// opens as an ATE tab, everything else goes to the system.</summary>
        static void OpenLinkTarget(string link)
        {
            if (TextEditorWindow.TryOpenAteLink(link)) return;
            if (System.IO.File.Exists(link)) { TextEditorWindow.OpenExternal(link, 1, 1); return; }
            Application.OpenURL(link);
        }

        static void AppendEscaped(StringBuilder sb, string s)
        {
            foreach (char c in s)
            {
                if (c == '<') sb.Append("<noparse><</noparse>");
                else sb.Append(c);
            }
        }

        static string StripFences(string src)
        {
            var lines = new List<string>(src.Split('\n'));
            if (lines.Count > 0 && lines[0].TrimStart().StartsWith("```")) lines.RemoveAt(0);
            if (lines.Count > 0 && lines[lines.Count - 1].TrimStart().StartsWith("```")) lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines);
        }

        static Color ParseHex(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

        // ---------- Block editing ----------

        void BeginBlockEdit(VisualElement rendered, Block block)
        {
            var editor = new TextField { multiline = true, value = block.Source };
            editor.AddToClassList("md-block-editor");
            editor.style.whiteSpace = WhiteSpace.Pre;
            editor.style.marginBottom = 8;

            int index = _scroll.contentContainer.IndexOf(rendered);
            _scroll.contentContainer.Insert(index, editor);
            rendered.style.display = DisplayStyle.None;
            _activeEditor = editor;

            bool done = false;
            void Commit()
            {
                if (done) return;
                done = true;
                _activeEditor = null;
                string newSource = editor.value.Replace("\r\n", "\n").Replace("\r", "\n");
                if (newSource != block.Source)
                    onEditBlock?.Invoke(block.StartOffset, block.EndOffset, newSource);
                else
                {
                    editor.RemoveFromHierarchy();
                    rendered.style.display = DisplayStyle.Flex;
                }
            }
            void Cancel()
            {
                if (done) return;
                done = true;
                _activeEditor = null;
                editor.RemoveFromHierarchy();
                rendered.style.display = DisplayStyle.Flex;
            }

            editor.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Cancel(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Return && (e.ctrlKey || e.commandKey)) { Commit(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            editor.RegisterCallback<FocusOutEvent>(_ => Commit());
            editor.schedule.Execute(() => editor.Focus()).ExecuteLater(0);
        }
    }
}
#endif
