#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace ADKOM.TextEditor
{
    // Replace-in-Files application: open buffers are edited through the
    // normal document machinery (the active tab gets a real undo step),
    // closed files are written to disk BOM-aware, and every operation lands
    // in the ReplaceJournal so ONE action can restore all touched files.
    public partial class TextEditorWindow
    {
        /// <summary>Normalized path → live buffer text for every open
        /// file-backed document (so searches see unsaved edits).</summary>
        internal Dictionary<string, string> OpenDocSnapshots()
        {
            var map = new Dictionary<string, string>();
            foreach (var d in _docs)
                if (!d.IsSettings && d.HasFile && d.Content != null)
                    map[FindInFiles.Norm(d.FilePath)] = d.Content;
            return map;
        }

        TextDocument DocByNormPath(string norm) =>
            _docs.FirstOrDefault(d => !d.IsSettings && d.HasFile &&
                FindInFiles.Norm(d.FilePath) == norm);

        /// <summary>Applies the selected matches. Returns (files touched,
        /// replacements applied, stale matches skipped).</summary>
        internal (int files, int replaced, int skipped) ApplyReplaceInFiles(List<FindInFiles.Match> selected)
        {
            var op = new ReplaceJournal.Operation();
            int replaced = 0, skippedTotal = 0;
            foreach (var group in selected.GroupBy(m => m.Path))
            {
                string norm = group.Key;
                var doc = DocByNormPath(norm);
                string before;
                try { before = doc != null ? doc.Content : File.ReadAllText(norm); }
                catch (System.Exception) { skippedTotal += group.Count(); continue; }

                var list = group.ToList();
                string after = FindInFiles.ApplyToContent(before, list, out int skipped);
                skippedTotal += skipped;
                if (after == before) continue;
                replaced += list.Count - skipped;

                if (!SetFileContent(norm, doc, after)) { skippedTotal += list.Count - skipped; continue; }
                op.Changes.Add(new ReplaceJournal.FileChange { Path = norm, Before = before, After = after });
            }
            if (op.Changes.Count > 0)
            {
                op.Label = string.Format(L10n.Tr("{0} replacement(s) in {1} file(s)"), replaced, op.Changes.Count);
                ReplaceJournal.Push(op);
            }
            RebuildTabs();
            UpdateTitle();
            return (op.Changes.Count, replaced, skippedTotal);
        }

        /// <summary>Writes new content to an open buffer (undoable when it is
        /// the active tab) or to disk (BOM-aware) when closed.</summary>
        bool SetFileContent(string norm, TextDocument doc, string content)
        {
            if (doc == null) doc = DocByNormPath(norm); // may have opened since
            if (doc != null)
            {
                ApiReplaceRange(doc, 0, (doc.Content ?? string.Empty).Length, content);
                return true;
            }
            try
            {
                bool bom = false;
                using (var fs = File.OpenRead(norm))
                {
                    var b = new byte[3];
                    bom = fs.Read(b, 0, 3) == 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
                }
                File.WriteAllText(norm, content, new System.Text.UTF8Encoding(bom));
                if (norm.StartsWith(FindInFiles.Norm(UnityEngine.Application.dataPath),
                        System.StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.Refresh();
                return true;
            }
            catch (System.Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Replace in Files could not write " + norm + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Restores every file of the newest journal operation to its
        /// BEFORE state (redo = AFTER again). Open-vs-closed is resolved at
        /// restore time, so files opened or closed since still restore.</summary>
        internal string UndoReplaceInFiles()
        {
            var op = ReplaceJournal.PopUndo();
            if (op == null) return null;
            foreach (var c in op.Changes) SetFileContent(c.Path, null, c.Before);
            RebuildTabs();
            UpdateTitle();
            return op.Label;
        }

        internal string RedoReplaceInFiles()
        {
            var op = ReplaceJournal.PopRedo();
            if (op == null) return null;
            foreach (var c in op.Changes) SetFileContent(c.Path, null, c.After);
            RebuildTabs();
            UpdateTitle();
            return op.Label;
        }
    }
}
#endif
