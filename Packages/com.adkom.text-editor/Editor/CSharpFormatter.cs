#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Rich-text C# syntax highlighter. Output is TMP-style markup rendered by
    /// the display overlay; literal '&lt;' runs are wrapped in &lt;noparse&gt;
    /// so source text can never be misread as markup.
    /// </summary>
    public sealed class CSharpFormatter : ITextFormatter
    {
        public string Name => "C#";

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
            // common contextual keywords
            "var","get","set","value","yield","async","await","when","where",
            "partial","record","init","nameof"
        };

        /// <summary>Theme supplying the token colors. Never null.</summary>
        public HighlightTheme Theme { get; set; } = HighlightTheme.VSCode;

        string KeywordColor => Theme.Current.Keyword;
        string StringColor  => Theme.Current.String;
        string CommentColor => Theme.Current.Comment;
        string NumberColor  => Theme.Current.Number;
        string PreprocColor => Theme.Current.Preprocessor;

        public string Format(string text)
        {
            var sb = new StringBuilder(text.Length * 2);
            int i = 0, n = text.Length;
            int plainStart = 0; // start of the pending uncolored run

            void FlushPlain(int end)
            {
                if (end > plainStart) Emit(sb, text, plainStart, end, null);
            }

            while (i < n)
            {
                char c = text[i];

                // Line comment
                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    FlushPlain(i);
                    int s = i;
                    while (i < n && text[i] != '\n') i++;
                    Emit(sb, text, s, i, CommentColor);
                    plainStart = i;
                }
                // Block comment
                else if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    FlushPlain(i);
                    int s = i;
                    i += 2;
                    while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    i = Math.Min(n, i + 2);
                    Emit(sb, text, s, i, CommentColor);
                    plainStart = i;
                }
                // Preprocessor directive (only whitespace may precede it on the line)
                else if (c == '#' && AtLineStart(text, i))
                {
                    FlushPlain(i);
                    int s = i;
                    while (i < n && text[i] != '\n') i++;
                    Emit(sb, text, s, i, PreprocColor);
                    plainStart = i;
                }
                // Verbatim string @"..." ("" escapes)
                else if (c == '@' && i + 1 < n && text[i + 1] == '"')
                {
                    FlushPlain(i);
                    int s = i;
                    i += 2;
                    while (i < n)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < n && text[i + 1] == '"') { i += 2; continue; }
                            i++; break;
                        }
                        i++;
                    }
                    Emit(sb, text, s, i, StringColor);
                    plainStart = i;
                }
                // String literal (also covers the quoted part of $"...")
                else if (c == '"')
                {
                    FlushPlain(i);
                    int s = i;
                    i++;
                    while (i < n && text[i] != '"' && text[i] != '\n')
                    {
                        if (text[i] == '\\') i++;
                        i++;
                    }
                    if (i < n && text[i] == '"') i++;
                    Emit(sb, text, s, i, StringColor);
                    plainStart = i;
                }
                // Char literal
                else if (c == '\'')
                {
                    FlushPlain(i);
                    int s = i;
                    i++;
                    while (i < n && text[i] != '\'' && text[i] != '\n')
                    {
                        if (text[i] == '\\') i++;
                        i++;
                    }
                    if (i < n && text[i] == '\'') i++;
                    Emit(sb, text, s, i, StringColor);
                    plainStart = i;
                }
                // Number
                else if (char.IsDigit(c) && (i == 0 || !IsIdentChar(text[i - 1])))
                {
                    FlushPlain(i);
                    int s = i;
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '.' || text[i] == '_'))
                        i++;
                    Emit(sb, text, s, i, NumberColor);
                    plainStart = i;
                }
                // Identifier / keyword
                else if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < n && IsIdentChar(text[i])) i++;
                    if (Keywords.Contains(text.Substring(s, i - s)))
                    {
                        FlushPlain(s);
                        Emit(sb, text, s, i, KeywordColor);
                        plainStart = i;
                    }
                    // non-keyword identifiers stay in the plain run
                }
                else
                {
                    i++;
                }
            }
            FlushPlain(n);
            return sb.ToString();
        }

        static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        static bool AtLineStart(string text, int i)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                char c = text[j];
                if (c == '\n') return true;
                if (c != ' ' && c != '\t') return false;
            }
            return true;
        }

        // Tags are emitted per line (never spanning '\n') so output can be
        // split into independently renderable lines by the virtualized view.
        static void Emit(StringBuilder sb, string text, int start, int end, string color)
        {
            if (end <= start) return;
            int segStart = start;
            for (int i = start; i <= end; i++)
            {
                if (i == end || text[i] == '\n')
                {
                    if (i > segStart)
                    {
                        string seg = text.Substring(segStart, i - segStart);
                        if (seg.IndexOf('<') >= 0)
                            seg = "<noparse>" + seg.Replace("</noparse>", "</ noparse>") + "</noparse>";
                        if (color == null) sb.Append(seg);
                        else sb.Append("<color=").Append(color).Append('>').Append(seg).Append("</color>");
                    }
                    if (i < end) sb.Append('\n');
                    segStart = i + 1;
                }
            }
        }
    }

    /// <summary>Maps a file path to the display formatter for its language.</summary>
    public static class TextFormatters
    {
        static readonly PlainTextFormatter Plain = new PlainTextFormatter();
        static readonly CSharpFormatter CSharp = new CSharpFormatter();

        /// <summary>Theme applied to all highlighting formatters.</summary>
        public static HighlightTheme Theme
        {
            get => CSharp.Theme;
            set => CSharp.Theme = value ?? HighlightTheme.VSCode;
        }

        public static ITextFormatter ForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return Plain;
            return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".cs" => CSharp,
                _ => Plain
            };
        }
    }
}
#endif
