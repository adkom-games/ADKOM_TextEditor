#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

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

        public MarkdownView()
        {
            style.flexGrow = 1;
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.contentContainer.style.paddingLeft = 16;
            _scroll.contentContainer.style.paddingRight = 16;
            _scroll.contentContainer.style.paddingTop = 10;
            Add(_scroll);
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
            ParseBlocks();
            var c = _scroll.contentContainer;
            c.Clear();
            foreach (var block in _blocks)
                c.Add(BuildBlockElement(block));
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
                           !MarkdownClassifier.IsListMarker(lines[li].TrimStart(), out _) &&
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

            // Click-to-edit: swap the block for an inline source editor.
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                BeginBlockEdit(el, block);
                e.StopPropagation();
            });
            return el;
        }

        string RenderBlockText(Block block)
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
                        string marker = char.IsDigit(t[0]) ? t.Substring(0, ml) : "•";
                        sb.Append(new string(' ', indent)).Append(marker).Append(' ')
                          .Append(t.Substring(ml).TrimStart());
                    }
                    else sb.Append(l);
                    sb.Append('\n');
                }
                src = sb.ToString().TrimEnd('\n');
            }
            return InlineToRich(src);
        }

        /// <summary>Inline markdown → UITK rich text: **bold**, *italic*,
        /// `code`, [text](url). Literal '&lt;' is escaped via noparse.</summary>
        internal string InlineToRich(string src)
        {
            var sb = new StringBuilder(src.Length + 32);
            int i = 0, n = src.Length;
            string codeColor = _palette?.String ?? "#CE9178";
            string linkColor = _palette?.Method ?? "#DCDCAA";
            while (i < n)
            {
                char c = src[i];
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
                            sb.Append("<color=").Append(linkColor).Append("><u>");
                            AppendEscaped(sb, src.Substring(i + 1, close - i - 1));
                            sb.Append("</u></color>");
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
                if (c == '<') sb.Append("<noparse><</noparse>");
                else sb.Append(c);
                i++;
            }
            return sb.ToString();
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

            bool done = false;
            void Commit()
            {
                if (done) return;
                done = true;
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
