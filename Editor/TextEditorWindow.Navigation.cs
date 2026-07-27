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

        void JumpTo(NavLoc loc)
        {
            int i = _docs.IndexOf(loc.Doc);
            if (i < 0) return;
            if (i != _active) SwitchTo(i); // same-doc jumps skip the tab churn
            _code.IndexToLineCol(Mathf.Clamp(loc.Index, 0, _code.value.Length), out int l, out int c);
            _code.GoToLine(l + 1, c + 1);
        }
    }
}
#endif
