#if UNITY_EDITOR
using System.Collections.Generic;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Markdown source-mode classifier. Maps constructs onto the existing
    /// TokenClass palette: headers→Keyword, code→String, bold→Type,
    /// italic→Variable, link text→Method, link URL→Comment, list markers and
    /// rules→Number, blockquote markers→Preprocessor.
    /// </summary>
    public sealed class MarkdownClassifier : ISyntaxClassifier
    {
        public string Name => "Markdown";

        public List<SyntaxSpan> Classify(IReadOnlyList<string> lines)
        {
            var spans = new List<SyntaxSpan>(lines.Count * 3);
            bool inFence = false;

            for (int li = 0; li < lines.Count; li++)
            {
                string line = lines[li];
                string trimmed = line.TrimStart();
                int indent = line.Length - trimmed.Length;

                if (trimmed.StartsWith("```"))
                {
                    spans.Add(new SyntaxSpan(li, 0, line.Length, TokenClass.String));
                    inFence = !inFence;
                    continue;
                }
                if (inFence)
                {
                    spans.Add(new SyntaxSpan(li, 0, line.Length, TokenClass.String));
                    continue;
                }
                if (trimmed.StartsWith("#"))
                {
                    spans.Add(new SyntaxSpan(li, 0, line.Length, TokenClass.Keyword));
                    continue;
                }
                if (IsHorizontalRule(trimmed))
                {
                    spans.Add(new SyntaxSpan(li, 0, line.Length, TokenClass.Number));
                    continue;
                }
                if (trimmed.StartsWith(">"))
                    spans.Add(new SyntaxSpan(li, indent, 1, TokenClass.Preprocessor));
                else if (trimmed.StartsWith("|"))
                {
                    for (int ci = 0; ci < line.Length; ci++)
                        if (line[ci] == '|')
                            spans.Add(new SyntaxSpan(li, ci, 1, TokenClass.Preprocessor));
                }
                else if (IsListMarker(trimmed, out int markerLen))
                {
                    spans.Add(new SyntaxSpan(li, indent, markerLen, TokenClass.Number));
                    string rest = trimmed.Substring(markerLen).TrimStart();
                    if (rest.StartsWith("[ ] ") || rest.StartsWith("[x] ") || rest.StartsWith("[X] "))
                        spans.Add(new SyntaxSpan(li, line.IndexOf('[', indent + markerLen), 3, TokenClass.Number));
                }

                ClassifyInline(li, line, spans);
            }
            return spans;
        }

        internal static bool IsHorizontalRule(string t) =>
            t.Length >= 3 && (t.Replace("-", "") == "" || t.Replace("*", "") == "" || t.Replace("_", "") == "");

        internal static bool IsListMarker(string trimmed, out int markerLen)
        {
            markerLen = 0;
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
            {
                markerLen = 1;
                return true;
            }
            int i = 0;
            while (i < trimmed.Length && char.IsDigit(trimmed[i])) i++;
            if (i > 0 && i < trimmed.Length && (trimmed[i] == '.' || trimmed[i] == ')') &&
                i + 1 < trimmed.Length && trimmed[i + 1] == ' ')
            {
                markerLen = i + 1;
                return true;
            }
            return false;
        }

        static void ClassifyInline(int li, string line, List<SyntaxSpan> spans)
        {
            int i = 0, n = line.Length;
            while (i < n)
            {
                char c = line[i];
                if (c == '`')
                {
                    int end = line.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        spans.Add(new SyntaxSpan(li, i, end - i + 1, TokenClass.String));
                        i = end + 1;
                        continue;
                    }
                }
                else if (c == '~' && i + 1 < n && line[i + 1] == '~')
                {
                    int end = line.IndexOf("~~", i + 2, System.StringComparison.Ordinal);
                    if (end > i)
                    {
                        spans.Add(new SyntaxSpan(li, i, end - i + 2, TokenClass.Type)); // strikethrough
                        i = end + 2;
                        continue;
                    }
                }
                else if (c == '!' && i + 1 < n && line[i + 1] == '[')
                {
                    int close = line.IndexOf(']', i + 2);
                    if (close > i && close + 1 < n && line[close + 1] == '(')
                    {
                        int urlEnd = line.IndexOf(')', close + 2);
                        if (urlEnd > close)
                        {
                            spans.Add(new SyntaxSpan(li, i, close - i + 1, TokenClass.Method));   // ![alt]
                            spans.Add(new SyntaxSpan(li, close + 1, urlEnd - close, TokenClass.Comment)); // (url)
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
                else if (c == '*' && i + 1 < n && line[i + 1] == '*')
                {
                    int end = line.IndexOf("**", i + 2, System.StringComparison.Ordinal);
                    if (end > i)
                    {
                        spans.Add(new SyntaxSpan(li, i, end - i + 2, TokenClass.Type)); // bold
                        i = end + 2;
                        continue;
                    }
                }
                else if ((c == '*' || c == '_') && i + 1 < n && line[i + 1] != ' ' && line[i + 1] != c)
                {
                    int end = line.IndexOf(c, i + 1);
                    if (end > i)
                    {
                        spans.Add(new SyntaxSpan(li, i, end - i + 1, TokenClass.Variable)); // italic
                        i = end + 1;
                        continue;
                    }
                }
                else if (c == '[')
                {
                    int close = line.IndexOf(']', i + 1);
                    if (close > i && close + 1 < n && line[close + 1] == '(')
                    {
                        int urlEnd = line.IndexOf(')', close + 2);
                        if (urlEnd > close)
                        {
                            spans.Add(new SyntaxSpan(li, i, close - i + 1, TokenClass.Method));   // [text]
                            spans.Add(new SyntaxSpan(li, close + 1, urlEnd - close, TokenClass.Comment)); // (url)
                            i = urlEnd + 1;
                            continue;
                        }
                    }
                }
                i++;
            }
        }
    }
}
#endif
