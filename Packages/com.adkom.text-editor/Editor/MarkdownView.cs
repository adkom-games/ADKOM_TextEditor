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

        /// <summary>Raised by formatting actions when no block is being
        /// edited: the window appends the given source as a new block.</summary>
        public event Action<string> onInsertBlock;

        TextField _activeEditor;
        public bool HasActiveEditor => _activeEditor != null;

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
                else if (t.StartsWith("|"))
                {
                    kind = "table";
                    while (li < lines.Length && lines[li].TrimStart().StartsWith("|")) li++;
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

            // Click-to-edit: swap the block for an inline source editor.
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
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
            if (_activeEditor == null)
            {
                string template = id switch
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
                if (template != null) onInsertBlock?.Invoke(template);
                return;
            }

            switch (id)
            {
                case "bold": WrapSelection("**", "**", "bold text"); break;
                case "italic": WrapSelection("*", "*", "italic text"); break;
                case "strike": WrapSelection("~~", "~~", "struck text"); break;
                case "code": WrapSelection("`", "`", "code"); break;
                case "link": WrapSelection("[", "](https://)", "link text"); break;
                case "image": WrapSelection("![", "](https://)", "alt text"); break;
                case "h1": SetHeading(1); break;
                case "h2": SetHeading(2); break;
                case "h3": SetHeading(3); break;
                case "ul": PrefixLines("- "); break;
                case "ol": PrefixLinesNumbered(); break;
                case "task": PrefixLines("- [ ] "); break;
                case "quote": PrefixLines("> "); break;
                case "codeblock":
                    _activeEditor.value = "```\n" + _activeEditor.value + "\n```";
                    break;
                case "hr": InsertAtCaret("\n---\n"); break;
                case "table": InsertAtCaret("\n| Column A | Column B |\n|---|---|\n| a | b |\n"); break;
            }
            var ed = _activeEditor;
            ed.schedule.Execute(() => ed.Focus()).ExecuteLater(0);
        }

        void WrapSelection(string pre, string post, string placeholder)
        {
            var ed = _activeEditor;
            string v = ed.value;
            int a = Mathf.Clamp(Mathf.Min(ed.cursorIndex, ed.selectIndex), 0, v.Length);
            int b = Mathf.Clamp(Mathf.Max(ed.cursorIndex, ed.selectIndex), a, v.Length);
            string innerText = b > a ? v.Substring(a, b - a) : placeholder;
            ed.value = v.Substring(0, a) + pre + innerText + post + v.Substring(b);
            ed.selectIndex = a + pre.Length;
            ed.cursorIndex = a + pre.Length + innerText.Length;
        }

        void InsertAtCaret(string textToInsert)
        {
            var ed = _activeEditor;
            string v = ed.value;
            int a = Mathf.Clamp(Mathf.Min(ed.cursorIndex, ed.selectIndex), 0, v.Length);
            ed.value = v.Substring(0, a) + textToInsert + v.Substring(a);
            ed.cursorIndex = ed.selectIndex = a + textToInsert.Length;
        }

        void SetHeading(int level)
        {
            var ed = _activeEditor;
            var lines = ed.value.Split('\n');
            lines[0] = new string('#', level) + " " + lines[0].TrimStart('#').TrimStart();
            ed.value = string.Join("\n", lines);
        }

        void PrefixLines(string prefix)
        {
            var ed = _activeEditor;
            var lines = ed.value.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0 && !lines[i].TrimStart().StartsWith(prefix.TrimEnd()))
                    lines[i] = prefix + lines[i].TrimStart();
            ed.value = string.Join("\n", lines);
        }

        void PrefixLinesNumbered()
        {
            var ed = _activeEditor;
            var lines = ed.value.Split('\n');
            int num = 1;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0)
                    lines[i] = (num++) + ". " + lines[i].TrimStart();
            ed.value = string.Join("\n", lines);
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
