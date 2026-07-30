#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Find/Replace in Files engine: walks the project's text files (Assets +
    /// Packages by default, any root on request), matches plain/regex queries,
    /// and precomputes each match's replacement so a later apply is a pure
    /// splice. Open-document buffers override disk contents so results match
    /// what the user sees. Pure logic — UI and application live in the window.
    /// </summary>
    internal static class FindInFiles
    {
        internal sealed class Match
        {
            public string Path;        // normalized full path
            public int Offset, Length; // in the searched content
            public int Line, Col;      // 0-based
            public string LineText;    // trimmed preview
            public string MatchText;   // exact matched text (verify before apply)
            public string Replacement; // resolved replacement (regex groups expanded)
        }

        internal struct Options
        {
            public string Query, Replace;
            public bool Regex, MatchCase, WholeWord;
            public string Root;   // empty = Assets + Packages
            public string Glob;   // file-name wildcard, empty = all
        }

        internal const int MaxMatches = 5000;
        const int MaxFileBytes = 4 * 1024 * 1024;

        static readonly HashSet<string> BinaryExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",".jpg",".jpeg",".gif",".tga",".psd",".tif",".tiff",".exr",".hdr",
            ".fbx",".obj",".blend",".dae",".3ds",".max",
            ".dll",".so",".dylib",".a",".lib",".pdb",".mdb",".exe",
            ".wav",".mp3",".ogg",".aif",".aiff",".flac",".mp4",".mov",".avi",".webm",
            ".ttf",".otf",".ttc",".woff",".woff2",
            ".zip",".7z",".rar",".gz",".tar",".unitypackage",
            ".bytes",".bin",".pdf",".aar",".jar",".apk",".nupkg",".svgz"
        };

        /// <summary>The default search roots (call on the main thread).</summary>
        public static string[] DefaultRoots() => new[]
        {
            UnityEngine.Application.dataPath,
            Path.GetFullPath("Packages")
        };

        public static string Norm(string p) => Path.GetFullPath(p).Replace('\\', '/');

        /// <summary>Runs the search. <paramref name="openDocs"/> maps
        /// normalized paths of open documents to their live buffer text.
        /// Thread-safe (no Unity API calls).</summary>
        public static List<Match> Search(Options o, string[] roots,
            IReadOnlyDictionary<string, string> openDocs, out int filesScanned, out bool truncated)
        {
            filesScanned = 0;
            truncated = false;
            var result = new List<Match>();
            if (string.IsNullOrEmpty(o.Query)) return result;

            Regex rx = null;
            if (o.Regex)
                rx = new Regex(o.Query, o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            Regex glob = string.IsNullOrEmpty(o.Glob) || o.Glob.Trim() == "*" ? null
                : new Regex("^" + Regex.Escape(o.Glob.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                    RegexOptions.IgnoreCase);

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in EnumerateFiles(root))
                {
                    if (glob != null && !glob.IsMatch(Path.GetFileName(file))) continue;
                    string norm = Norm(file);
                    string content;
                    if (openDocs != null && openDocs.TryGetValue(norm, out var buffer))
                        content = buffer;
                    else
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.Length > MaxFileBytes) continue;
                            content = File.ReadAllText(file);
                        }
                        catch (Exception) { continue; }
                        if (content.IndexOf('\0') >= 0) continue; // binary sniff
                    }
                    filesScanned++;
                    SearchContent(o, rx, norm, content, result);
                    if (result.Count >= MaxMatches) { truncated = true; return result; }
                }
            }
            return result;
        }

        static IEnumerable<string> EnumerateFiles(string root)
        {
            var dirs = new Stack<string>();
            dirs.Push(root);
            while (dirs.Count > 0)
            {
                string dir = dirs.Pop();
                string name = Path.GetFileName(dir);
                if (name.StartsWith(".", StringComparison.Ordinal) ||
                    name == "Library" || name == "Temp" || name == "Logs" || name == "obj")
                    continue;
                string[] files;
                string[] subs;
                try
                {
                    files = Directory.GetFiles(dir);
                    subs = Directory.GetDirectories(dir);
                }
                catch (Exception) { continue; }
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f);
                    if (ext == ".meta" || BinaryExt.Contains(ext)) continue;
                    yield return f;
                }
                foreach (var s in subs) dirs.Push(s);
            }
        }

        static void SearchContent(Options o, Regex rx, string path, string content, List<Match> into)
        {
            int line = 0, lineStart = 0, scanned = 0;
            void Advance(int to)
            {
                for (; scanned < to; scanned++)
                    if (content[scanned] == '\n') { line++; lineStart = scanned + 1; }
            }
            string LineTextAt(int offset)
            {
                int end = content.IndexOf('\n', offset);
                if (end < 0) end = content.Length;
                string t = content.Substring(lineStart, end - lineStart).TrimEnd('\r');
                return t.Length > 200 ? t.Substring(0, 200) : t;
            }

            if (rx != null)
            {
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(content))
                {
                    if (m.Length == 0) continue; // zero-width would loop the splice
                    Advance(m.Index);
                    into.Add(new Match
                    {
                        Path = path,
                        Offset = m.Index,
                        Length = m.Length,
                        Line = line,
                        Col = m.Index - lineStart,
                        LineText = LineTextAt(m.Index),
                        MatchText = m.Value,
                        Replacement = o.Replace == null ? null : m.Result(o.Replace)
                    });
                    if (into.Count >= MaxMatches) return;
                }
                return;
            }

            var cmp = o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int i = content.IndexOf(o.Query, cmp);
            while (i >= 0)
            {
                bool ok = !o.WholeWord ||
                    ((i == 0 || !IsWordChar(content[i - 1])) &&
                     (i + o.Query.Length >= content.Length || !IsWordChar(content[i + o.Query.Length])));
                if (ok)
                {
                    Advance(i);
                    into.Add(new Match
                    {
                        Path = path,
                        Offset = i,
                        Length = o.Query.Length,
                        Line = line,
                        Col = i - lineStart,
                        LineText = LineTextAt(i),
                        MatchText = content.Substring(i, o.Query.Length),
                        Replacement = o.Replace
                    });
                    if (into.Count >= MaxMatches) return;
                }
                i = content.IndexOf(o.Query, i + 1, cmp);
            }
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Applies replacements to <paramref name="content"/> —
        /// descending by offset so earlier offsets stay valid — verifying each
        /// match still sees its original text. Returns the new content;
        /// <paramref name="skipped"/> counts stale matches left untouched.</summary>
        public static string ApplyToContent(string content, List<Match> matches, out int skipped)
        {
            skipped = 0;
            matches.Sort((a, b) => b.Offset.CompareTo(a.Offset));
            var sb = new System.Text.StringBuilder(content);
            foreach (var m in matches)
            {
                if (m.Offset < 0 || m.Offset + m.Length > sb.Length ||
                    sb.ToString(m.Offset, m.Length) != m.MatchText)
                { skipped++; continue; }
                sb.Remove(m.Offset, m.Length).Insert(m.Offset, m.Replacement ?? "");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The global undo journal for Replace in Files: each operation records
    /// every touched file's full before/after text, so one click restores (or
    /// re-applies) ALL of them — open buffers and closed files alike.
    /// Session-scoped (documents' own undo stacks handle in-buffer history).
    /// </summary>
    internal static class ReplaceJournal
    {
        internal sealed class FileChange
        {
            public string Path;
            public string Before, After;
        }

        internal sealed class Operation
        {
            public string Label;               // "3 files, 12 replacements"
            public List<FileChange> Changes = new List<FileChange>();
        }

        static readonly List<Operation> _undo = new List<Operation>();
        static readonly List<Operation> _redo = new List<Operation>();

        public static bool CanUndo => _undo.Count > 0;
        public static bool CanRedo => _redo.Count > 0;
        public static string UndoLabel => CanUndo ? _undo[_undo.Count - 1].Label : null;
        public static string RedoLabel => CanRedo ? _redo[_redo.Count - 1].Label : null;

        public static void Push(Operation op)
        {
            _undo.Add(op);
            if (_undo.Count > 20) _undo.RemoveAt(0); // full-text entries: keep bounded
            _redo.Clear();
        }

        public static Operation PopUndo()
        {
            if (!CanUndo) return null;
            var op = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(op);
            return op;
        }

        public static Operation PopRedo()
        {
            if (!CanRedo) return null;
            var op = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(op);
            return op;
        }
    }
}
#endif
