#if UNITY_EDITOR
using System.Collections.Generic;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// JSON / JSONC classifier (.json, .asmdef, .jslib manifests): object keys
    /// (a string followed by ':') color as identifiers, string values as
    /// strings, numbers, true/false/null as keywords, plus // and /* */
    /// comments (JSONC — Unity's own JSON assets use them).
    /// </summary>
    public sealed class JsonClassifier : ISyntaxClassifier, ICompletionKeywords
    {
        public string Name => "JSON";

        static readonly string[] Kw = { "true", "false", "null" };
        public IEnumerable<string> CompletionKeywords => Kw;

        public List<SyntaxSpan> Classify(IReadOnlyList<string> lines)
        {
            var spans = new List<SyntaxSpan>(lines.Count * 4);
            bool inBlockComment = false;

            for (int li = 0; li < lines.Count; li++)
            {
                string line = lines[li];
                int i = 0, n = line.Length;

                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", System.StringComparison.Ordinal);
                    if (end < 0) { spans.Add(new SyntaxSpan(li, 0, n, TokenClass.Comment)); continue; }
                    spans.Add(new SyntaxSpan(li, 0, end + 2, TokenClass.Comment));
                    inBlockComment = false;
                    i = end + 2;
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
                            break;
                        }
                        spans.Add(new SyntaxSpan(li, i, end + 2 - i, TokenClass.Comment));
                        i = end + 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        int s = i;
                        i++;
                        while (i < n)
                        {
                            if (line[i] == '\\') { i += 2; continue; }
                            if (line[i] == '"') { i++; break; }
                            i++;
                        }
                        // A string followed by ':' is an object key.
                        int j = i;
                        while (j < n && (line[j] == ' ' || line[j] == '\t')) j++;
                        bool isKey = j < n && line[j] == ':';
                        spans.Add(new SyntaxSpan(li, s, System.Math.Min(i, n) - s,
                            isKey ? TokenClass.Variable : TokenClass.String));
                        continue;
                    }
                    if (char.IsDigit(c) || (c == '-' && i + 1 < n && char.IsDigit(line[i + 1])))
                    {
                        int s = i;
                        i++;
                        while (i < n && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == 'e' ||
                               line[i] == 'E' || line[i] == '+' || line[i] == '-')) i++;
                        spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Number));
                        continue;
                    }
                    if (char.IsLetter(c))
                    {
                        int s = i;
                        while (i < n && char.IsLetter(line[i])) i++;
                        string word = line.Substring(s, i - s);
                        if (word == "true" || word == "false" || word == "null")
                            spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Keyword));
                        continue;
                    }
                    i++;
                }
            }
            return spans;
        }
    }
}
#endif
