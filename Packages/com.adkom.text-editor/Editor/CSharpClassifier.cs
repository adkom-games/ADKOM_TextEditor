#if UNITY_EDITOR
using System.Collections.Generic;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Heuristic C# classifier: lexes keywords/strings/comments/numbers/
    /// preprocessor (block comments tracked across lines) and classifies
    /// identifiers from local context — type positions, call sites, member
    /// access, declarations. Fast enough for every keystroke; the semantic
    /// module replaces these spans with compiler truth when installed.
    /// </summary>
    public sealed class CSharpClassifier : ISyntaxClassifier, ICompletionKeywords
    {
        public string Name => "C#";

        public System.Collections.Generic.IEnumerable<string> CompletionKeywords => Keywords;

        static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract","as","base","bool","break","byte","case","catch","char",
            "checked","class","const","continue","decimal","default","delegate",
            "do","double","else","enum","event","explicit","extern","false",
            "finally","fixed","float","for","foreach","goto","if","implicit",
            "in","int","interface","internal","is","lock","long","namespace",
            "new","null","object","operator","out","override","params","private",
            "protected","public","readonly","ref","return","sbyte","sealed",
            "short","sizeof","stackalloc","static","string","struct","switch",
            "this","throw","true","try","typeof","uint","ulong","unchecked",
            "unsafe","ushort","using","virtual","void","volatile","while",
            "var","get","set","value","yield","async","await","when","where",
            "partial","record","init","nameof"
        };

        static readonly HashSet<string> TypeLeadKeywords = new HashSet<string>
        {
            "new", "class", "struct", "interface", "enum", "as", "is", "typeof"
        };

        struct Tok
        {
            public int Start, Length;
            public char Kind; // 'i' identifier, 'k' keyword, 'p' punctuation
            public string Text;
        }

        public List<SyntaxSpan> Classify(IReadOnlyList<string> lines)
        {
            var spans = new List<SyntaxSpan>(lines.Count * 6);
            bool inBlockComment = false;
            var toks = new List<Tok>(64);

            for (int li = 0; li < lines.Count; li++)
            {
                string line = lines[li];
                toks.Clear();
                int i = 0, n = line.Length;

                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", System.StringComparison.Ordinal);
                    if (end < 0) { spans.Add(new SyntaxSpan(li, 0, n, TokenClass.Comment)); continue; }
                    spans.Add(new SyntaxSpan(li, 0, end + 2, TokenClass.Comment));
                    inBlockComment = false;
                    i = end + 2;
                }

                // Preprocessor line
                int ns = i;
                while (ns < n && (line[ns] == ' ' || line[ns] == '\t')) ns++;
                if (ns < n && line[ns] == '#')
                {
                    spans.Add(new SyntaxSpan(li, ns, n - ns, TokenClass.Preprocessor));
                    continue;
                }

                while (i < n)
                {
                    char c = line[i];
                    if (c == '/' && i + 1 < n && line[i + 1] == '/')
                    {
                        spans.Add(new SyntaxSpan(li, i, n - i, TokenClass.Comment));
                        break;
                    }
                    if (c == '/' && i + 1 < n && line[i + 1] == '*')
                    {
                        int end = line.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                        if (end < 0)
                        {
                            spans.Add(new SyntaxSpan(li, i, n - i, TokenClass.Comment));
                            inBlockComment = true;
                            i = n;
                            break;
                        }
                        spans.Add(new SyntaxSpan(li, i, end + 2 - i, TokenClass.Comment));
                        i = end + 2;
                        continue;
                    }
                    if (c == '"' || (c == '@' && i + 1 < n && line[i + 1] == '"') ||
                        (c == '$' && i + 1 < n && line[i + 1] == '"'))
                    {
                        int s = i;
                        bool verbatim = c == '@';
                        i += c == '"' ? 1 : 2;
                        while (i < n)
                        {
                            if (line[i] == '\\' && !verbatim) { i += 2; continue; }
                            if (line[i] == '"')
                            {
                                if (verbatim && i + 1 < n && line[i + 1] == '"') { i += 2; continue; }
                                i++;
                                break;
                            }
                            i++;
                        }
                        spans.Add(new SyntaxSpan(li, s, System.Math.Min(i, n) - s, TokenClass.String));
                        continue;
                    }
                    if (c == '\'')
                    {
                        int s = i;
                        i++;
                        while (i < n && line[i] != '\'')
                        {
                            if (line[i] == '\\') i++;
                            i++;
                        }
                        if (i < n) i++;
                        spans.Add(new SyntaxSpan(li, s, System.Math.Min(i, n) - s, TokenClass.String));
                        continue;
                    }
                    if (char.IsDigit(c) && (i == 0 || !IsIdentChar(line[i - 1])))
                    {
                        int s = i;
                        while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '.' || line[i] == '_')) i++;
                        spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Number));
                        continue;
                    }
                    if (char.IsLetter(c) || c == '_')
                    {
                        int s = i;
                        while (i < n && IsIdentChar(line[i])) i++;
                        string word = line.Substring(s, i - s);
                        bool kw = Keywords.Contains(word);
                        if (kw) spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Keyword));
                        toks.Add(new Tok { Start = s, Length = i - s, Kind = kw ? 'k' : 'i', Text = word });
                        continue;
                    }
                    if (!char.IsWhiteSpace(c))
                        toks.Add(new Tok { Start = i, Length = 1, Kind = 'p', Text = null });
                    i++;
                }

                ClassifyIdentifiers(li, line, toks, spans);
            }
            return spans;
        }

        void ClassifyIdentifiers(int li, string line, List<Tok> toks, List<SyntaxSpan> spans)
        {
            for (int t = 0; t < toks.Count; t++)
            {
                if (toks[t].Kind != 'i') continue;
                var tok = toks[t];
                Tok? prev = t > 0 ? toks[t - 1] : (Tok?)null;
                Tok? next = t + 1 < toks.Count ? toks[t + 1] : (Tok?)null;
                char nc = next.HasValue && next.Value.Kind == 'p' ? line[next.Value.Start] : '\0';
                char pc = prev.HasValue && prev.Value.Kind == 'p' ? line[prev.Value.Start] : '\0';
                string pw = prev.HasValue && prev.Value.Kind == 'k' ? prev.Value.Text : null;

                TokenClass cls;
                if (pw != null && TypeLeadKeywords.Contains(pw)) cls = TokenClass.Type;
                else if (nc == '(') cls = TokenClass.Method;
                else if (pc == '.')
                    cls = nc == '.' ? TokenClass.Type
                        : TokenClass.Variable; // member access
                else if (nc == '.')
                    cls = char.IsUpper(tok.Text[0]) ? TokenClass.Type : TokenClass.Variable;
                else if (next.HasValue && next.Value.Kind == 'i')
                    cls = TokenClass.Type;   // "Foo bar" — type position
                else if (prev.HasValue && prev.Value.Kind == 'i')
                    cls = TokenClass.Variable; // the declared name
                else if (nc == '<' && char.IsUpper(tok.Text[0]))
                    cls = TokenClass.Type;   // Foo<...>
                else if (pc == ':' )
                    cls = TokenClass.Type;   // base list
                else if (char.IsUpper(tok.Text[0]))
                    cls = TokenClass.Default; // unknown Pascal identifier
                else
                    cls = TokenClass.Variable;

                if (cls != TokenClass.Default)
                    spans.Add(new SyntaxSpan(li, tok.Start, tok.Length, cls));
            }
        }

        static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
#endif
