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
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.contentContainer.style.paddingLeft = 16;
            _scroll.contentContainer.style.paddingRight = 16;
            _scroll.contentContainer.style.paddingTop = 10;
            Add(_scroll);

            // Rendered-mode links: Ctrl+Click on a <link>-tagged span opens
            // it; hovering shows the instruction tooltip on the label.
            RegisterCallback<PointerDownLinkTagEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey) || string.IsNullOrEmpty(e.linkID)) return;
                if (!TextEditorWindow.TryOpenAteLink(e.linkID)) // ate:// opens in ATE
                    Application.OpenURL(e.linkID);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerOverLinkTagEvent>(e =>
            {
                _hoverLink = e.linkID;
                if (e.target is TextElement te && !string.IsNullOrEmpty(e.linkID))
                    te.tooltip = string.Format(L10n.Tr("Ctrl+Click to open {0}"), e.linkID);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerOutLinkTagEvent>(e =>
            {
                _hoverLink = null;
                if (e.target is TextElement te) te.tooltip = null;
            }, TrickleDown.TrickleDown);

            // Locked (read-only) mode: right-click offers clipboard actions.
            // Copies are the RENDERED text without any formatting — no rich-
            // text tags, no markdown markers — so URLs and prose paste clean.
            RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button != 1 || !Locked) return;
                var menu = new GenericMenu();
                string link = _hoverLink;
                if (!string.IsNullOrEmpty(link))
                    menu.AddItem(new GUIContent(L10n.Tr("Copy Link URL")), false,
                        () => EditorGUIUtility.systemCopyBuffer = link);
                if (HasDocSelection)
                    menu.AddItem(new GUIContent(L10n.Tr("Copy Selection as Text")), false,
                        () => EditorGUIUtility.systemCopyBuffer = SelectedPlainText());
                var block = BlockFor(e.target as VisualElement);
                if (block != null)
                    menu.AddItem(new GUIContent(L10n.Tr("Copy Block as Text")), false,
                        () => EditorGUIUtility.systemCopyBuffer = PlainTextFor(block));
                menu.AddItem(new GUIContent(L10n.Tr("Copy All as Text")), false,
                    () => EditorGUIUtility.systemCopyBuffer = PlainText());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(L10n.Tr("Unlock (Allow Editing)")), false,
                    () => onUnlockRequest?.Invoke());
                menu.ShowAsContext();
                e.StopPropagation();
            });

            HookDocSelection();
        }

        static Block BlockFor(VisualElement v)
        {
            for (; v != null; v = v.parent)
                if (v.userData is Block b) return b;
            return null;
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
        static readonly Color SelTint = new Color(0.25f, 0.45f, 0.85f, 0.30f);

        // STICKY native selection: clicking a menu moves focus off the label
        // and the engine wipes its in-label selection before the menu command
        // runs — making "copy the selection" from a menu impossible. The
        // FocusOut handler fires while the range is still readable (verified),
        // so it is snapshotted here (display-space indices) and commands fall
        // back to it when no live/doc selection exists.
        Label _stickyLabel;
        int _stickyStart, _stickyEnd;

        internal bool HasDocSelection =>
            _selAll || (_selAnchor >= 0 && _selFocus >= 0 && _selAnchor != _selFocus);

        /// <summary>Anything a copy-the-selection command can act on: the
        /// multi-block document selection, or the sticky snapshot of the last
        /// in-label native selection (surviving the menu-click blur).</summary>
        internal bool HasCopyableSelection => HasDocSelection || _stickyLabel != null;

        void HookDocSelection()
        {
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (!Locked || e.button != 0) return;
                ClearDocSelection();
                _stickyLabel = null; // a new click in the view starts over
                _selAnchor = _selFocus = LabelIndexAt(e.position);
                _selDragging = _selAnchor >= 0;
                // No StopPropagation: the label under the pointer still does
                // its native char-precise selection until the drag leaves it.
            }, TrickleDown.TrickleDown);
            // NOTE: these view-level handlers only see moves while NO label
            // has pointer capture. The native text selection CAPTURES the
            // pointer on the label the drag started in, and captured pointer
            // events go to that element alone — which is why every label also
            // registers OnLabelPointerMove/Up (see Render): the capturing
            // label keeps receiving moves even far outside its own bounds.
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!Locked || !_selDragging || (e.pressedButtons & 1) == 0) return;
                UpdateDocSelectionAt(e.position);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => _selDragging = false, TrickleDown.TrickleDown);
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
                    EditorGUIUtility.systemCopyBuffer = SelectedPlainText();
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
            int idx = LabelIndexAt(panelPos);
            if (idx < 0 || idx == _selFocus) return;
            _selFocus = idx;
            ApplyDocSelection();
        }

        /// <summary>Per-label drag tracking: the label a selection drag
        /// started in holds the pointer capture, so IT gets every move —
        /// route them into the document selection.</summary>
        void OnLabelPointerMove(PointerMoveEvent e)
        {
            if (!Locked || !_selDragging || (e.pressedButtons & 1) == 0) return;
            UpdateDocSelectionAt(e.position);
        }

        void OnLabelPointerUp(PointerUpEvent e) => _selDragging = false;

        /// <summary>Snapshot (or invalidate) the native in-label selection
        /// the moment the label loses focus — the engine resets the range
        /// right after this event. A focus-out WITHOUT a range clears the
        /// snapshot: the user clicked away, deselecting.</summary>
        void OnLabelFocusOut(FocusOutEvent e)
        {
            if (!(e.currentTarget is Label l)) return;
            // Focus moving because of a click INSIDE the view (the pointer-
            // down handler set _selDragging) is a new selection starting —
            // never snapshot the old label's about-to-die range for that.
            if (_selDragging) { _stickyLabel = null; return; }
            int a = Mathf.Min(l.selection.cursorIndex, l.selection.selectIndex);
            int b = Mathf.Max(l.selection.cursorIndex, l.selection.selectIndex);
            if (b > a) { _stickyLabel = l; _stickyStart = a; _stickyEnd = b; }
            else if (_stickyLabel == l) _stickyLabel = null;
        }

        /// <summary>The display string of a rich-text label — tags stripped,
        /// noparse bodies kept literal. Selection indices live in THIS space
        /// (verified: SelectAll's extent equals this string's length).</summary>
        static string ParsedText(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            bool np = false;
            for (int i = 0; i < raw.Length; i++)
            {
                if (!np && i + 9 <= raw.Length && string.CompareOrdinal(raw, i, "<noparse>", 0, 9) == 0) { np = true; i += 8; continue; }
                if (np && i + 10 <= raw.Length && string.CompareOrdinal(raw, i, "</noparse>", 0, 10) == 0) { np = false; i += 9; continue; }
                if (!np && raw[i] == '<')
                {
                    int close = raw.IndexOf('>', i);
                    if (close > i) { i = close; continue; }
                }
                sb.Append(raw[i]);
            }
            return sb.ToString();
        }

        string StickyText()
        {
            if (_stickyLabel == null) return string.Empty;
            string parsed = ParsedText(_stickyLabel.text);
            int a = Mathf.Clamp(_stickyStart, 0, parsed.Length);
            int b = Mathf.Clamp(_stickyEnd, 0, parsed.Length);
            return b > a ? parsed.Substring(a, b - a) : string.Empty;
        }

        /// <summary>Ctrl+A: the whole document is the selection.</summary>
        internal void SelectAllDoc()
        {
            if (_selLabels.Count == 0) return;
            _selAnchor = 0;
            _selFocus = _selLabels.Count - 1;
            _selAll = true;
            ApplyDocSelection();
        }

        void ApplyDocSelection()
        {
            int a = Mathf.Min(_selAnchor, _selFocus), b = Mathf.Max(_selAnchor, _selFocus);
            for (int i = 0; i < _selLabels.Count; i++)
            {
                if (i >= a && i <= b) _selLabels[i].style.backgroundColor = SelTint;
                else _selLabels[i].style.backgroundColor = StyleKeyword.Null;
            }
        }

        void ClearDocSelection()
        {
            foreach (var l in _selLabels) l.style.backgroundColor = StyleKeyword.Null;
            _stickyLabel = null;
            _selAnchor = _selFocus = -1;
            _selAll = false;
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

        /// <summary>The selected span as plain rendered text: EVERY block
        /// from the first selected one to the last (rules and images between
        /// them included), joined like Copy All.</summary>
        internal string SelectedPlainText()
        {
            // No multi-block selection: fall back to the sticky snapshot of
            // the last in-label selection (kept across the menu-click blur).
            if (!HasDocSelection && _stickyLabel != null) return StickyText();
            int a = Mathf.Min(_selAnchor, _selFocus), b = Mathf.Max(_selAnchor, _selFocus);
            if (a < 0 || _selLabels.Count == 0) return string.Empty;
            var ra = BlockRangeOf(_selLabels[a]);
            var rb = BlockRangeOf(_selLabels[Mathf.Min(b, _selLabels.Count - 1)]);
            int bi = Mathf.Min(ra.first, rb.first), be = Mathf.Max(ra.last, rb.last);
            if (bi < 0 || be < 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = bi; i <= be && i < _blocks.Count; i++)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(PlainTextFor(_blocks[i]));
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
            _selLabels.Clear();
            _selAnchor = _selFocus = -1;
            _selAll = false;
            _stickyLabel = null; // its label is discarded with the rebuild
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
                if (seg == null) seg = new Segment { FirstBlock = i };
                if (sb.Length > 0) sb.Append("\n\n");
                int start = sb.Length;
                sb.Append(RichFor(b, text));
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
                l.RegisterCallback<FocusOutEvent>(OnLabelFocusOut);
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
                    try { y += seg.Label.selection.GetCursorPositionFromStringIndex(r.start).y; }
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
                            sb.Append("<color=").Append(imgColor).Append(">[img] <u>");
                            AppendEscaped(sb, src.Substring(i + 2, close - i - 2));
                            sb.Append("</u></color>");
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
                    int close = src.IndexOf(']', i + 1);
                    if (close > i && close + 1 < n && src[close + 1] == '(')
                    {
                        int urlEnd = src.IndexOf(')', close + 2);
                        if (urlEnd > close)
                        {
                            string target = src.Substring(close + 2, urlEnd - close - 2).Split(' ')[0];
                            bool openable = IsOpenableUrl(target);
                            if (openable) sb.Append("<link=\"").Append(target).Append("\">");
                            sb.Append("<color=").Append(linkColor).Append("><u>");
                            AppendEscaped(sb, src.Substring(i + 1, close - i - 1));
                            sb.Append("</u></color>");
                            if (openable) sb.Append("</link>");
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
