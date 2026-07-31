#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    // Caret navigation history: jump commands (Goto Line, Go to Definition,
    // external opens, metadata views) record where you were; Navigate
    // Back/Forward walk the trail across tabs, VS-style.
    public partial class TextEditorWindow
    {
        struct NavLoc
        {
            public TextDocument Doc;
            public int Index;
        }

        readonly List<NavLoc> _navBack = new List<NavLoc>();
        readonly List<NavLoc> _navForward = new List<NavLoc>();
        const int NavCap = 100;

        /// <summary>Records the current caret location before a jump.</summary>
        internal void PushNavLocation()
        {
            if (!CanEditDoc || _code == null) return;
            var loc = new NavLoc { Doc = Active, Index = _code.cursorIndex };
            if (_navBack.Count > 0 && ReferenceEquals(_navBack[_navBack.Count - 1].Doc, loc.Doc) &&
                Mathf.Abs(_navBack[_navBack.Count - 1].Index - loc.Index) < 2)
                return; // same spot — don't stutter the trail
            _navBack.Add(loc);
            if (_navBack.Count > NavCap) _navBack.RemoveAt(0);
            _navForward.Clear();
        }

        void NavigateBack()
        {
            while (_navBack.Count > 0)
            {
                var loc = _navBack[_navBack.Count - 1];
                _navBack.RemoveAt(_navBack.Count - 1);
                if (!_docs.Contains(loc.Doc)) continue; // tab closed since
                if (CanEditDoc && _code != null)
                    _navForward.Add(new NavLoc { Doc = Active, Index = _code.cursorIndex });
                JumpTo(loc);
                return;
            }
        }

        void NavigateForward()
        {
            while (_navForward.Count > 0)
            {
                var loc = _navForward[_navForward.Count - 1];
                _navForward.RemoveAt(_navForward.Count - 1);
                if (!_docs.Contains(loc.Doc)) continue;
                if (CanEditDoc && _code != null)
                    _navBack.Add(new NavLoc { Doc = Active, Index = _code.cursorIndex });
                JumpTo(loc);
                return;
            }
        }

        // ---------- Bookmarks (must-have #19b) ----------

        internal void ToggleBookmark()
        {
            if (!CanEditDoc) return;
            _code.IndexToLineCol(_code.cursorIndex, out int line, out _);
            if (!Active.Bookmarks.Remove(line)) Active.Bookmarks.Add(line);
            _code.RefreshVisiblePublic();
        }

        internal void JumpBookmark(int dir)
        {
            if (!CanEditDoc || Active.Bookmarks.Count == 0)
            {
                PostStatus(L10n.Tr("No bookmarks in this document."));
                return;
            }
            var lines = new List<int>(Active.Bookmarks);
            lines.Sort();
            _code.IndexToLineCol(_code.cursorIndex, out int cur, out _);
            int target = -1;
            if (dir > 0)
            {
                foreach (int l in lines) if (l > cur) { target = l; break; }
                if (target < 0) target = lines[0]; // wrap around
            }
            else
            {
                for (int i = lines.Count - 1; i >= 0; i--)
                    if (lines[i] < cur) { target = lines[i]; break; }
                if (target < 0) target = lines[lines.Count - 1];
            }
            PushNavLocation();
            _code.GoToLine(target + 1, 1);
        }

        internal void ClearBookmarks()
        {
            if (!CanEditDoc) return;
            Active.Bookmarks.Clear();
            _code.RefreshVisiblePublic();
        }

        /// <summary>Shifts bookmarks below an edit that added/removed lines.</summary>
        void OnCodeLineDelta(int afterLine, int delta)
        {
            if (!CanEditDoc || Active.Bookmarks.Count == 0) return;
            var shifted = new HashSet<int>();
            foreach (int l in Active.Bookmarks)
                shifted.Add(l > afterLine ? Mathf.Max(0, l + delta) : l);
            Active.Bookmarks = shifted;
        }

        // ---- History window plumbing ----
        // The History window carries its OWN document selection (an unsynced
        // tab bar), so everything here is parameterized by TextDocument. The
        // ACTIVE doc's undo world lives attached to _code; every other doc's
        // world sits detached on doc.UndoWorld with the text in doc.Content.

        /// <summary>Documents eligible for history browsing (settings tabs
        /// keep their pseudo-doc out of it).</summary>
        internal List<TextDocument> HistoryDocs()
        {
            var list = new List<TextDocument>(_docs.Count);
            foreach (var d in _docs) if (!d.IsSettings) list.Add(d);
            return list;
        }

        internal TextDocument HistoryActiveDoc =>
            CanEditDoc ? Active : null;

        bool HistoryIsActive(TextDocument d) =>
            d != null && HasDocs && !Active.IsSettings && ReferenceEquals(Active, d);

        CodeView.UndoWorld HistoryWorldOf(TextDocument d) =>
            HistoryIsActive(d) ? _code.LiveUndoWorld
                               : d.UndoWorld as CodeView.UndoWorld;

        string HistoryTextOf(TextDocument d) =>
            HistoryIsActive(d) ? _code.value : (d.Content ?? string.Empty);

        /// <summary>Cheap change signature for the window's poll: stack depths
        /// plus a content stamp (live doc version, or background text hash).</summary>
        internal (int undo, int redo, int stamp) HistoryCountsFor(TextDocument d)
        {
            var w = HistoryWorldOf(d);
            int u = w?.Undo.Count ?? 0, r = w?.Redo.Count ?? 0;
            int stamp = HistoryIsActive(d) ? _code.DocVersion
                : (d.Content != null ? d.Content.GetHashCode() : 0);
            return (u, r, stamp);
        }

        internal List<CodeView.HistoryRow> HistoryRowsFor(TextDocument d)
        {
            var w = HistoryWorldOf(d);
            if (w == null) return new List<CodeView.HistoryRow>();
            return CodeView.HistoryRowsFor(w, HistoryTextOf(d));
        }

        internal string HistoryStateFor(TextDocument d, int undoSteps, int redoSteps,
            out int changeLine)
        {
            var w = HistoryWorldOf(d);
            if (w == null) { changeLine = 0; return HistoryTextOf(d); }
            return CodeView.HistoryStateFor(w, HistoryTextOf(d), undoSteps, redoSteps,
                out changeLine);
        }

        /// <summary>Dresses an auxiliary CodeView (the History preview) like
        /// the main editor: same theme palette, font, line numbers, and the
        /// given document's syntax classifier.</summary>
        internal void StyleAuxView(CodeView view, TextDocument doc = null)
        {
            view.SetPalette(CurrentTheme.Current);
            view.ApplyFontConfig();
            view.showLineNumbers = true;
            if (doc == null && HasDocs && !Active.IsSettings) doc = Active;
            string classifierPath = null;
            if (doc != null)
            {
                if (doc.HasFile) classifierPath = doc.FilePath;
                else if (doc.VirtualCSharp) classifierPath = "virtual.cs";
                else if (doc.VirtualMarkdown) classifierPath = "virtual.md";
            }
            view.SetClassifier(SyntaxClassifiers.ForPath(classifierPath));
        }

        /// <summary>Walks a document's undo/redo stacks to a point on the
        /// timeline. The active doc goes through the normal Undo()/Redo() so
        /// the move itself stays reversible; a background doc gets the same
        /// bookkeeping applied to its detached world and stored Content.</summary>
        internal void HistoryStepFor(TextDocument d, int undoSteps, int redoSteps)
        {
            if (d == null) return;
            if (HistoryIsActive(d))
            {
                for (int i = 0; i < undoSteps; i++) _code.Undo();
                for (int i = 0; i < redoSteps; i++) _code.Redo();
                return;
            }
            var w = d.UndoWorld as CodeView.UndoWorld;
            if (w == null) return;
            string text = d.Content ?? string.Empty;
            bool moved = false;
            for (int i = 0; i < undoSteps && w.Undo.Count > 0; i++)
            {
                var op = w.Undo[w.Undo.Count - 1];
                w.Undo.RemoveAt(w.Undo.Count - 1);
                text = CodeView.ApplyRevert(text, op);
                w.Redo.Add(op);
                moved = true;
            }
            for (int i = 0; i < redoSteps && w.Redo.Count > 0; i++)
            {
                var op = w.Redo[w.Redo.Count - 1];
                w.Redo.RemoveAt(w.Redo.Count - 1);
                text = CodeView.ApplyForward(text, op);
                w.Undo.Add(op);
                moved = true;
            }
            if (!moved) return;
            d.Content = text;
            d.IsDirty = true;
            RebuildTabs();
            SaveSessionNow();
        }

        /// <summary>Opens a history snapshot as a read-only-ish virtual tab
        /// (same C# highlighting as the source document when applicable).</summary>
        internal void HistoryOpenSnapshot(string title, string content, TextDocument like)
        {
            bool csharp = like != null && like.HasFile &&
                like.FilePath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase);
            csharp |= like != null && like.VirtualCSharp;
            OpenVirtualDoc(title, content, csharp);
        }

        void JumpTo(NavLoc loc)
        {
            int i = _docs.IndexOf(loc.Doc);
            if (i < 0) return;
            if (i != _active) SwitchTo(i); // same-doc jumps skip the tab churn
            _code.IndexToLineCol(Mathf.Clamp(loc.Index, 0, _code.value.Length), out int l, out int c);
            _code.GoToLine(l + 1, c + 1);
        }

        /// <summary>Pops a menu of the file's #regions (nested, with line
        /// numbers); picking one jumps to it and centres it.</summary>
        internal void GoToRegionCommand()
        {
            if (!CanEditDoc) return;
            var regions = _code.RegionList();
            if (regions.Count == 0)
            {
                AteConsole.Info("[ADKOM Text Editor] " + L10n.Tr("No #regions in this file."));
                return;
            }
            var menu = new GenericMenu();
            foreach (var r in regions)
            {
                int line = r.line;
                // GenericMenu treats '/' as a submenu separator — swap it out; em
                // spaces show nesting depth.
                string label = new string(' ', r.depth) + r.name.Replace('/', '∕')
                             + "   (" + (line + 1) + ")";
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    PushNavLocation();
                    _code.GoToLine(line + 1, 1);
                    _code.CenterOnLine(line);
                });
            }
            menu.ShowAsContext();
        }
    }
}
#endif
