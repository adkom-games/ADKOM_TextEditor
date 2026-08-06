#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    // Git in the editor window: background-refreshed gutter diff markers,
    // Blame as a read-only virtual tab, and File History as a commit picker
    // opening point-in-time snapshots. All via the system git CLI.
    public partial class TextEditorWindow
    {
        // Reset with the domain — hot-serialization would keep a mid-flight
        // true and permanently block gutter mark refreshes.
        [System.NonSerialized] bool _gitMarksInFlight;

        /// <summary>Recomputes the active document's gutter diff markers on a
        /// background thread (no-op without git / a repo / a file).</summary>
        internal void RefreshGitMarksAsync()
        {
            if (_code == null || _gitMarksInFlight) return;
            if (!HasDocs || Active.IsSettings || !Active.HasFile)
            { _code.ApplyGitMarks(null); return; }
            string path = Active.FilePath;
            var doc = Active;
            var ctx = _mainCtx;
            _gitMarksInFlight = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                Dictionary<int, GitService.LineMark> marks = null;
                try { if (GitService.GitAvailable) marks = GitService.DiffMarks(path); }
                catch (System.Exception) { }
                ctx.Post(_ =>
                {
                    _gitMarksInFlight = false;
                    if (this == null || _code == null || _code.panel == null) return;
                    if (HasDocs && Active == doc) _code.ApplyGitMarks(marks);
                }, null);
            });
        }

        /// <summary>Blame for the active file as a read-only virtual tab:
        /// "hash date author │ line".</summary>
        internal void GitBlameCurrent()
        {
            if (!HasDocs || !Active.HasFile) return;
            GitBlameFor(Active.FilePath, Active.DisplayName);
        }

        /// <summary>Blame for any file (auxiliary views pass their own
        /// path), shown as a read-only virtual tab.</summary>
        internal void GitBlameFor(string path, string name)
        {
            var ctx = _mainCtx;
            PostStatus(L10n.Tr("Running git blame…"));
            System.Threading.Tasks.Task.Run(() =>
            {
                List<GitService.BlameLine> blame = null;
                string[] lines = null;
                try
                {
                    if (GitService.GitAvailable)
                    {
                        blame = GitService.Blame(path);
                        lines = System.IO.File.ReadAllLines(path);
                    }
                }
                catch (System.Exception) { }
                ctx.Post(_ =>
                {
                    if (this == null) return;
                    if (blame == null || blame.Count == 0 || lines == null)
                    {
                        PostStatus(L10n.Tr("No blame available (not in a git repository?)."));
                        return;
                    }
                    int authorW = System.Math.Min(18, blame.Max(b => (b.Author ?? "").Length));
                    var sb = new System.Text.StringBuilder(lines.Length * 100);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var b = i < blame.Count ? blame[i] : default;
                        string author = (b.Author ?? "").PadRight(authorW);
                        if (author.Length > authorW) author = author.Substring(0, authorW);
                        sb.Append(b.Hash ?? "????????").Append(' ')
                          .Append(b.Date ?? "????-??-??").Append(' ')
                          .Append(author).Append(" │ ").Append(lines[i]).Append('\n');
                    }
                    OpenVirtualDoc(string.Format(L10n.Tr("Blame {0}"), name), sb.ToString(), csharp: false);
                }, null);
            });
        }

        /// <summary>Opens the active file's commit history as a focused
        /// read-only virtual tab.</summary>
        internal void GitFileHistory()
        {
            if (!HasDocs || !Active.HasFile) return;
            GitFileHistoryFor(Active.FilePath, Active.DisplayName);
        }

        /// <summary>File history for any file, opened as a focused virtual
        /// tab titled "History <name>" — one "hash date author subject"
        /// line per commit, newest first. (This replaced a transient
        /// dropdown popup: a tab persists, scrolls, and can be searched or
        /// copied from.)</summary>
        internal void GitFileHistoryFor(string path, string name)
        {
            var ctx = _mainCtx;
            PostStatus(L10n.Tr("Reading history…"));
            System.Threading.Tasks.Task.Run(() =>
            {
                List<GitService.LogEntry> hist = null;
                try { if (GitService.GitAvailable) hist = GitService.FileHistory(path); }
                catch (System.Exception) { }
                ctx.Post(_ =>
                {
                    if (this == null) return;
                    if (hist == null || hist.Count == 0)
                    {
                        PostStatus(L10n.Tr("No history available (not in a git repository?)."));
                        return;
                    }
                    PostStatus("");
                    int authorW = System.Math.Min(18, hist.Max(h => (h.Author ?? "").Length));
                    var sb = new System.Text.StringBuilder(hist.Count * 96);
                    sb.Append(path).Append('\n').Append('\n');
                    foreach (var e in hist)
                    {
                        string author = (e.Author ?? "").PadRight(authorW);
                        if (author.Length > authorW) author = author.Substring(0, authorW);
                        sb.Append(e.Hash).Append("  ").Append(e.Date).Append("  ")
                          .Append(author).Append("  ").Append(e.Subject).Append('\n');
                    }
                    OpenVirtualDoc(string.Format(L10n.Tr("History {0}"), name), sb.ToString(), csharp: false);
                }, null);
            });
        }

        /// <summary>Opens a Git Time Lapse window on the active file, seeded
        /// with the tab buffer as the slider's right end. Multi-instance:
        /// every call opens its own window.</summary>
        internal void GitTimeLapseCurrent()
        {
            if (!HasDocs || !Active.HasFile || _code == null) return;
            GitTimeLapseWindow.Open(this, Active.FilePath, Active.DisplayName, _code.value);
        }

        /// <summary>Time Lapse for any file (auxiliary views pass their own
        /// path): the slider's right end is the open tab's buffer when the
        /// file has one, the on-disk content otherwise.</summary>
        internal void GitTimeLapseFor(string path, string name)
        {
            var d = FindDocByPath(path);
            string content;
            if (d != null)
                content = HasDocs && Active == d && _code != null ? _code.value : d.Content ?? "";
            else
            {
                try { content = System.IO.File.ReadAllText(path); }
                catch (System.Exception) { PostStatus(L10n.Tr("Could not read that file.")); return; }
            }
            GitTimeLapseWindow.Open(this, path, name, content);
        }

        /// <summary>The main editor's word-wrap setting, for auxiliary views
        /// that mirror the reading experience.</summary>
        internal bool WordWrapEnabled => _wordWrap;

        internal TextDocument FindDocByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return _docs.Find(d => d.HasFile && string.Equals(
                System.IO.Path.GetFullPath(d.FilePath), System.IO.Path.GetFullPath(path),
                System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Time Lapse's Copy to Tab: replaces the file's tab buffer
        /// with <paramref name="content"/> — one undoable Programmatic step
        /// when that tab is active, a direct model write otherwise.</summary>
        internal bool GitTimeLapseApply(string path, string content)
        {
            var d = FindDocByPath(path);
            if (d == null)
            {
                PostStatus(L10n.Tr("That file's tab is no longer open."));
                return false;
            }
            int end = HasDocs && Active == d && _code != null
                ? _code.value.Length : (d.Content ?? "").Length;
            ApiReplaceRange(d, 0, end, content);
            PostStatus(L10n.Tr("Copied this revision into the file's tab."));
            return true;
        }

    }
}
#endif
