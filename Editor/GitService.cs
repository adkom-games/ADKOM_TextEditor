#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Git CLI backend (no bundled library — the system `git`, same trust
    /// model as the `gh`/CI tooling). Synchronous runners safe for background
    /// threads, a per-directory repo-root cache, and parsers for the porcelain
    /// formats the features consume (unified-0 diffs for gutter markers,
    /// pretty-format logs for history and the branch graph).
    /// </summary>
    internal static class GitService
    {
        static readonly Dictionary<string, string> _rootCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool? _gitAvailable;

        public static bool GitAvailable
        {
            get
            {
                if (_gitAvailable == null)
                    _gitAvailable = Run(".", "--version", out var o, out _) == 0 &&
                        o.Contains("git version");
                return _gitAvailable.Value;
            }
        }

        /// <summary>Runs git with the given args. Returns the exit code
        /// (-1 = could not start). Never throws; never opens a window.</summary>
        public static int Run(string workDir, string args, out string stdout, out string stderr)
        {
            stdout = stderr = string.Empty;
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using var p = Process.Start(psi);
                var so = new StringBuilder();
                var se = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) se.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return -1; }
                p.WaitForExit(); // flush async readers
                stdout = so.ToString();
                stderr = se.ToString();
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return -1;
            }
        }

        /// <summary>The repository root containing <paramref name="path"/>,
        /// or null. Cached per directory.</summary>
        public static string RepoRoot(string path)
        {
            try
            {
                string dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return null;
                lock (_rootCache)
                    if (_rootCache.TryGetValue(dir, out var hit)) return hit;
                string root = null;
                if (Run(dir, "rev-parse --show-toplevel", out var o, out _) == 0)
                {
                    root = o.Trim().Replace('/', Path.DirectorySeparatorChar);
                    if (root.Length == 0) root = null;
                }
                lock (_rootCache) _rootCache[dir] = root;
                return root;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Splits process output into lines, dropping the '\r' that
        /// Windows git appends (subjects/paths must never carry it).</summary>
        static string[] Lines(string s)
        {
            var parts = s.Split('\n');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].TrimEnd('\r');
            return parts;
        }

        public static string RelPath(string root, string fullPath)
        {
            string full = Path.GetFullPath(fullPath).Replace('\\', '/');
            string r = Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/');
            return full.StartsWith(r, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(r.Length + 1) : full;
        }

        // ---- Gutter diff markers ----

        public enum LineMark { None = 0, Added = 1, Modified = 2, DeletedBelow = 3 }

        /// <summary>Working-tree-vs-HEAD line marks for a file (0-based lines).
        /// DeletedBelow marks the line ABOVE a removed run.</summary>
        public static Dictionary<int, LineMark> DiffMarks(string filePath)
        {
            var marks = new Dictionary<int, LineMark>();
            string root = RepoRoot(filePath);
            if (root == null) return marks;
            string rel = RelPath(root, filePath);
            if (Run(root, "diff HEAD --no-color --unified=0 -- \"" + rel + "\"",
                    out var o, out _) != 0)
                return marks;
            foreach (var line in Lines(o))
            {
                // @@ -oldStart[,oldCount] +newStart[,newCount] @@
                if (!line.StartsWith("@@", StringComparison.Ordinal)) continue;
                int plus = line.IndexOf('+');
                int at2 = line.IndexOf("@@", 2, StringComparison.Ordinal);
                if (plus < 0 || at2 < 0 || plus > at2) continue;
                string newPart = line.Substring(plus + 1, at2 - plus - 1).Trim();
                string oldPart = line.Substring(3, plus - 4).Trim();
                ParseRange(newPart, out int ns, out int nc);
                ParseRange(oldPart, out _, out int oc);
                if (nc == 0)
                {
                    // Pure deletion: mark the line the removal sits under.
                    marks[Math.Max(0, ns - 1)] = LineMark.DeletedBelow;
                }
                else
                {
                    var kind = oc == 0 ? LineMark.Added : LineMark.Modified;
                    for (int i = 0; i < nc; i++)
                    {
                        int l = ns - 1 + i;
                        if (l >= 0) marks[l] = kind;
                    }
                }
            }
            return marks;
        }

        static void ParseRange(string s, out int start, out int count)
        {
            start = 0; count = 1;
            int comma = s.IndexOf(',');
            if (comma < 0) int.TryParse(s, out start);
            else
            {
                int.TryParse(s.Substring(0, comma), out start);
                int.TryParse(s.Substring(comma + 1), out count);
            }
        }

        // ---- Blame / history ----

        public struct BlameLine { public string Hash, Author, Date; }

        /// <summary>Per-line blame (0-based index into the result list).</summary>
        public static List<BlameLine> Blame(string filePath)
        {
            var result = new List<BlameLine>();
            string root = RepoRoot(filePath);
            if (root == null) return result;
            string rel = RelPath(root, filePath);
            if (Run(root, "blame --line-porcelain -- \"" + rel + "\"", out var o, out _) != 0)
                return result;
            string hash = null, author = null, date = null;
            foreach (var line in Lines(o))
            {
                if (line.StartsWith("author ", StringComparison.Ordinal)) author = line.Substring(7);
                else if (line.StartsWith("author-time ", StringComparison.Ordinal))
                {
                    if (long.TryParse(line.Substring(12), out long t))
                        date = DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime().ToString("yyyy-MM-dd");
                }
                else if (line.StartsWith("\t", StringComparison.Ordinal))
                {
                    result.Add(new BlameLine { Hash = hash, Author = author, Date = date });
                }
                else
                {
                    int sp = line.IndexOf(' ');
                    if (sp >= 8 && sp <= 64 && IsHex(line, sp))
                        hash = line.Substring(0, Math.Min(8, sp));
                }
            }
            return result;
        }

        static bool IsHex(string s, int len)
        {
            for (int i = 0; i < len && i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return len >= 8;
        }

        public struct LogEntry { public string Hash, Date, Author, Subject; }

        public static List<LogEntry> FileHistory(string filePath, int max = 100)
        {
            var result = new List<LogEntry>();
            string root = RepoRoot(filePath);
            if (root == null) return result;
            string rel = RelPath(root, filePath);
            if (Run(root, "log --follow --date=short --pretty=format:%h%x01%ad%x01%an%x01%s -n "
                    + max + " -- \"" + rel + "\"", out var o, out _) != 0)
                return result;
            foreach (var line in Lines(o))
            {
                var parts = line.Split('\u0001');
                if (parts.Length == 4)
                    result.Add(new LogEntry { Hash = parts[0], Date = parts[1], Author = parts[2], Subject = parts[3] });
            }
            return result;
        }

        /// <summary>A commit's full message — subject plus body (null on
        /// failure).</summary>
        public static string CommitMessage(string filePath, string hash)
        {
            string root = RepoRoot(filePath);
            if (root == null) return null;
            return Run(root, "show -s --format=%B " + hash, out var o, out _) == 0
                ? o.TrimEnd('\r', '\n') : null;
        }

        /// <summary>A file's content at a commit (null on failure).</summary>
        public static string ShowFileAt(string filePath, string hash)
        {
            string root = RepoRoot(filePath);
            if (root == null) return null;
            string rel = RelPath(root, filePath).Replace('\\', '/');
            return Run(root, "show " + hash + ":\"" + rel + "\"", out var o, out _) == 0 ? o : null;
        }

        // ---- Status / stage / commit / push ----

        public struct StatusEntry { public string Path; public char Index, Work; }

        public static List<StatusEntry> Status(string root)
        {
            var result = new List<StatusEntry>();
            if (Run(root, "status --porcelain", out var o, out _) != 0) return result;
            foreach (var line in Lines(o))
            {
                if (line.Length < 4) continue;
                result.Add(new StatusEntry
                { Index = line[0], Work = line[1], Path = line.Substring(3).Trim().Trim('"') });
            }
            return result;
        }

        // ---- Branch graph ----

        public sealed class GraphNode
        {
            public string Hash, Date, Author, Subject;
            public string[] Parents;
            public List<string> Refs = new List<string>(); // branch/tag names at this commit
            public bool IsHead;
            public int Lane;    // assigned by the layout
            public int Row;     // topological order, newest first
            public int Segment; // contiguous chain id — the COLOR key. Lanes
                                // are reused by later chains for compactness;
                                // coloring by lane made an unmerged stale
                                // branch read as one line with the newer
                                // merged branches sharing its lane.
        }

        public static List<GraphNode> Graph(string root, int max = 200)
        {
            var nodes = new List<GraphNode>();
            if (Run(root, "log --all --topo-order --date=short " +
                    "--pretty=format:%h%x01%p%x01%D%x01%an%x01%ad%x01%s -n " + max,
                    out var o, out _) != 0)
                return nodes;
            foreach (var line in Lines(o))
            {
                var parts = line.Split('\u0001');
                if (parts.Length != 6) continue;
                var n = new GraphNode
                {
                    Hash = parts[0],
                    Parents = parts[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                    Author = parts[3],
                    Date = parts[4],
                    Subject = parts[5],
                    Row = nodes.Count
                };
                foreach (var r in parts[2].Split(','))
                {
                    string t = r.Trim();
                    if (t.Length == 0) continue;
                    if (t.StartsWith("HEAD -> ", StringComparison.Ordinal))
                    { n.IsHead = true; n.Refs.Add(t.Substring(8)); }
                    else if (t == "HEAD") n.IsHead = true;
                    else n.Refs.Add(t);
                }
                nodes.Add(n);
            }
            AssignLanes(nodes);
            return nodes;
        }

        /// <summary>Simple lane assignment: an open lane per unresolved parent
        /// chain; a node takes the lane of the first chain expecting it.</summary>
        static void AssignLanes(List<GraphNode> nodes)
        {
            var lanes = new List<string>();  // lane index -> next expected hash
            var laneSeg = new List<int>();   // lane index -> segment id of that chain
            int nextSeg = 1;                 // 0 = the primary (main) line

            // Lane 0 (and its color) is the PRIMARY line: the local
            // main/master branch when its tip is in the window, else HEAD's
            // branch. Without this seed, lane 0 simply went to whatever ref
            // had the NEWEST commit — mid-work that is the feature branch,
            // whose first-parent chain runs straight down main's shared
            // trunk, so the whole trunk took the feature branch's lane and
            // read as that branch instead of main.
            string primary = null;
            foreach (var n in nodes)
                if (n.Refs.Contains("main") || n.Refs.Contains("master")) { primary = n.Hash; break; }
            if (primary == null)
                foreach (var n in nodes)
                    if (n.IsHead) { primary = n.Hash; break; }
            if (primary != null) { lanes.Add(primary); laneSeg.Add(0); }

            foreach (var n in nodes)
            {
                int lane = lanes.IndexOf(n.Hash);
                if (lane < 0)
                {
                    lane = lanes.IndexOf(null);
                    if (lane < 0) { lanes.Add(null); laneSeg.Add(0); lane = lanes.Count - 1; }
                    laneSeg[lane] = nextSeg++; // a chain nobody expected: new segment
                }
                n.Lane = lane;
                n.Segment = laneSeg[lane];
                // This lane continues to the first parent; extra parents open lanes.
                lanes[lane] = n.Parents.Length > 0 ? n.Parents[0] : null;
                for (int p = 1; p < n.Parents.Length; p++)
                    if (!lanes.Contains(n.Parents[p]))
                    {
                        int free = lanes.IndexOf(null);
                        if (free < 0) { lanes.Add(n.Parents[p]); laneSeg.Add(nextSeg++); }
                        else { lanes[free] = n.Parents[p]; laneSeg[free] = nextSeg++; }
                    }
                // Collapse duplicate expectations (two lanes waiting on the
                // same parent = a merge point): keep the leftmost.
                for (int a = 0; a < lanes.Count; a++)
                    for (int b = a + 1; b < lanes.Count; b++)
                        if (lanes[a] != null && lanes[a] == lanes[b]) lanes[b] = null;
            }
        }
    }
}
#endif
