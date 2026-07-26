#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Model for a single text document: content, backing file, dirty state,
    /// and detected encoding / line-ending style so saves round-trip faithfully.
    /// </summary>
    [Serializable]
    public class TextDocument
    {
        public enum LineEnding { Unix, Windows, Mac }

        public string FilePath;
        public string Content = string.Empty;
        public bool IsDirty;
        // Special tab that shows the editor's settings pane instead of text.
        public bool IsSettings;
        // Virtual documents (e.g. "Foo [metadata]"): named content with no
        // backing file; VirtualCSharp requests C# highlighting.
        public string VirtualName;
        public bool VirtualCSharp;
        public LineEnding Eol = LineEnding.Windows;
        public bool HasBom;
        // True when the file on disk indents with tabs. In memory tabs are
        // always rendered as spaces; saving converts leading indentation back.
        public bool UsesTabs;

        // Ticks of File.GetLastWriteTimeUtc at load/save time, for external-change detection.
        public long LastKnownWriteTimeUtcTicks;

        public bool HasFile => !string.IsNullOrEmpty(FilePath);
        public string DisplayName =>
            IsSettings ? "Settings"
            : !string.IsNullOrEmpty(VirtualName) ? VirtualName
            : HasFile ? Path.GetFileName(FilePath) : "Untitled";

        public void LoadFrom(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            HasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(bytes, HasBom ? 3 : 0, bytes.Length - (HasBom ? 3 : 0));

            Eol = DetectEol(text);
            // Normalize to \n internally; restored on save.
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            UsesTabs = DetectTabs(text);
            Content = ExpandTabs(text, EditorConfig.TabSize);
            FilePath = path;
            IsDirty = false;
            LastKnownWriteTimeUtcTicks = File.GetLastWriteTimeUtc(path).Ticks;
        }

        public void SaveTo(string path)
        {
            string text = Content;
            if (UsesTabs) text = LeadingSpacesToTabs(text, EditorConfig.TabSize);
            if (Eol == LineEnding.Windows) text = text.Replace("\n", "\r\n");
            else if (Eol == LineEnding.Mac) text = text.Replace("\n", "\r");

            byte[] body = new UTF8Encoding(false).GetBytes(text);
            if (HasBom) body = Combine(new byte[] { 0xEF, 0xBB, 0xBF }, body);
            File.WriteAllBytes(path, body);

            FilePath = path;
            IsDirty = false;
            LastKnownWriteTimeUtcTicks = File.GetLastWriteTimeUtc(path).Ticks;
        }

        public bool FileChangedOnDisk()
        {
            if (!HasFile || !File.Exists(FilePath)) return false;
            return File.GetLastWriteTimeUtc(FilePath).Ticks != LastKnownWriteTimeUtcTicks;
        }

        static byte[] Combine(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        /// <summary>A file "uses tabs" when more lines indent with a tab than with spaces.</summary>
        static bool DetectTabs(string text)
        {
            int tabLines = 0, spaceLines = 0;
            bool atLineStart = true;
            for (int i = 0; i < text.Length; i++)
            {
                if (atLineStart)
                {
                    if (text[i] == '\t') tabLines++;
                    else if (text[i] == ' ') spaceLines++;
                    atLineStart = false;
                }
                if (text[i] == '\n') atLineStart = true;
            }
            return tabLines > spaceLines;
        }

        /// <summary>Expands each tab to spaces up to the next tab stop (column aware).</summary>
        public static string ExpandTabs(string text, int tabSize)
        {
            if (text.IndexOf('\t') < 0) return text;
            var sb = new StringBuilder(text.Length + 64);
            int col = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\t')
                {
                    int add = tabSize - (col % tabSize);
                    sb.Append(' ', add);
                    col += add;
                }
                else
                {
                    sb.Append(c);
                    col = c == '\n' ? 0 : col + 1;
                }
            }
            return sb.ToString();
        }

        /// <summary>Converts each full tab-stop of leading spaces back to a tab;
        /// interior alignment spaces are left untouched.</summary>
        public static string LeadingSpacesToTabs(string text, int tabSize)
        {
            var sb = new StringBuilder(text.Length);
            int i = 0, n = text.Length;
            while (i < n)
            {
                int lineStart = i;
                int spaces = 0;
                while (i < n && text[i] == ' ') { spaces++; i++; }
                sb.Append('\t', spaces / tabSize);
                sb.Append(' ', spaces % tabSize);
                while (i < n && text[i] != '\n') { sb.Append(text[i]); i++; }
                if (i < n) { sb.Append('\n'); i++; }
            }
            return sb.ToString();
        }

        static LineEnding DetectEol(string text)
        {
            int crlf = 0, lf = 0, cr = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                    else cr++;
                }
                else if (text[i] == '\n') lf++;
            }
            if (crlf >= lf && crlf >= cr && crlf > 0) return LineEnding.Windows;
            if (cr > lf) return LineEnding.Mac;
            if (lf > 0) return LineEnding.Unix;
            return Environment.NewLine == "\r\n" ? LineEnding.Windows : LineEnding.Unix;
        }

        public string EolLabel => Eol switch
        {
            LineEnding.Windows => "CRLF",
            LineEnding.Mac => "CR",
            _ => "LF"
        };
    }
}
#endif
