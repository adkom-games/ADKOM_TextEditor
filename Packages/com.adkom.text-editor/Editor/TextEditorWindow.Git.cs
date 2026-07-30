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
        bool _gitMarksInFlight;

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
            string path = Active.FilePath;
            string name = Active.DisplayName;
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

        /// <summary>Pops the active file's commit history; picking a commit
        /// opens that revision as a read-only virtual tab.</summary>
        internal void GitFileHistory()
        {
            if (!HasDocs || !Active.HasFile) return;
            string path = Active.FilePath;
            string name = Active.DisplayName;
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
                    var menu = new GenericMenu();
                    foreach (var e in hist)
                    {
                        var entry = e;
                        string subj = entry.Subject.Length > 60 ? entry.Subject.Substring(0, 57) + "..." : entry.Subject;
                        menu.AddItem(new GUIContent((entry.Date + "  " + entry.Hash + "  " + subj).Replace('/', '∕')),
                            false, () => OpenRevision(path, name, entry.Hash));
                    }
                    menu.ShowAsContext();
                }, null);
            });
        }

        void OpenRevision(string path, string name, string hash)
        {
            var ctx = _mainCtx;
            System.Threading.Tasks.Task.Run(() =>
            {
                string content = null;
                try { content = GitService.ShowFileAt(path, hash); }
                catch (System.Exception) { }
                ctx.Post(_ =>
                {
                    if (this == null) return;
                    if (content == null) { PostStatus(L10n.Tr("Could not read that revision.")); return; }
                    OpenVirtualDoc(name + " @ " + hash, content,
                        csharp: path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase));
                }, null);
            });
        }
    }
}
#endif
