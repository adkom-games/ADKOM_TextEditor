#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Code folding + indentation guides (must-haves #12, #19a).
    //
    // Folding rides the virtualized row model: a folded region's body lines
    // get ZERO visual rows in RecomputeWrap, and the existing row<->line
    // binary search skips zero-row lines naturally. Fold state is view-local
    // (cleared on document switch); edits before a fold shift it, edits
    // touching it unfold it, and moving the caret into a hidden line unfolds
    // its region. Guides draw one faint bar per indent level.
    public partial class CodeView
    {
        readonly List<(int start, int end)> _folds = new List<(int, int)>();
        readonly List<VisualElement> _guidePool = new List<VisualElement>();
        bool _indentGuides = true;

        public bool showIndentGuides
        {
            get => _indentGuides;
            set { _indentGuides = value; RefreshVisible(); }
        }

        internal bool HasFolds => _folds.Count > 0;

        internal bool IsLineHidden(int line)
        {
            foreach (var f in _folds)
                if (line > f.start && line <= f.end) return true;
            return false;
        }

        internal bool IsFoldedHeader(int line)
        {
            foreach (var f in _folds)
                if (f.start == line) return true;
            return false;
        }

        /// <summary>A line is foldable when it opens a brace whose match sits
        /// on a later line. Returns the region's end line, or -1.</summary>
        internal int FoldEndLine(int line)
        {
            if (line < 0 || line >= _lines.Count) return -1;
            string text = _lines[line];
            int col = text.LastIndexOf('{');
            if (col < 0) return -1;
            int m = FindMatchingBracket(LineColToIndex(line, col));
            if (m < 0) return -1;
            IndexToLineCol(m, out int endLine, out _);
            return endLine > line ? endLine : -1;
        }

        /// <summary>Folds or unfolds the region opened on (or containing)
        /// <paramref name="line"/>.</summary>
        internal void ToggleFoldAt(int line)
        {
            for (int i = 0; i < _folds.Count; i++)
                if (_folds[i].start == line)
                {
                    _folds.RemoveAt(i);
                    RecomputeWrap();
                    RefreshVisible();
                    return;
                }
            int end = FoldEndLine(line);
            if (end < 0) return;
            _folds.Add((line, end));
            // Never leave the caret inside a hidden body.
            if (_caretLine > line && _caretLine <= end)
            {
                _caretLine = line;
                _caretCol = _lines[line].Length;
                CollapseAnchor();
            }
            RecomputeWrap();
            RefreshVisible();
        }

        internal void FoldAtCaret()
        {
            // The caret line itself, or the nearest enclosing foldable line.
            if (FoldEndLine(_caretLine) >= 0) { ToggleFoldAt(_caretLine); return; }
            string v = GetValueInternal();
            int open = FindEnclosingOpen(v, cursorIndex, cursorIndex);
            if (open < 0) return;
            IndexToLineCol(open, out int line, out _);
            if (!IsFoldedHeader(line)) ToggleFoldAt(line);
        }

        internal void UnfoldAtCaret()
        {
            for (int i = 0; i < _folds.Count; i++)
                if (_folds[i].start == _caretLine ||
                    (_caretLine >= _folds[i].start && _caretLine <= _folds[i].end))
                {
                    _folds.RemoveAt(i);
                    RecomputeWrap();
                    RefreshVisible();
                    return;
                }
        }

        internal void UnfoldAll()
        {
            if (_folds.Count == 0) return;
            _folds.Clear();
            RecomputeWrap();
            RefreshVisible();
        }

        void UnfoldContaining(int line)
        {
            bool changed = false;
            for (int i = _folds.Count - 1; i >= 0; i--)
                if (line > _folds[i].start && line <= _folds[i].end)
                {
                    _folds.RemoveAt(i);
                    changed = true;
                }
            if (changed)
            {
                RecomputeWrap();
                RefreshVisible();
            }
        }

        /// <summary>Keeps folds consistent across an edit: folds after the
        /// edit shift by the line delta; folds the edit touches unfold.</summary>
        void AdjustFoldsForEdit(int start, int end, string replacement)
        {
            if (_folds.Count == 0) return;
            IndexToLineCol(start, out int editStartLine, out _);
            IndexToLineCol(end, out int editEndLine, out _);
            int removedLines = 0;
            string v = GetValueInternal();
            for (int i = start; i < end && i < v.Length; i++) if (v[i] == '\n') removedLines++;
            int addedLines = 0;
            foreach (char c in replacement) if (c == '\n') addedLines++;
            int delta = addedLines - removedLines;
            bool changed = false;
            for (int i = _folds.Count - 1; i >= 0; i--)
            {
                var f = _folds[i];
                if (editEndLine < f.start)
                {
                    if (delta != 0) { _folds[i] = (f.start + delta, f.end + delta); changed = true; }
                }
                else if (editStartLine == f.start && editEndLine == f.start)
                {
                    // Edit confined to the header line: same-line typing keeps
                    // the fold; inserted/removed newlines move the region.
                    if (delta != 0) { _folds[i] = (f.start + delta, f.end + delta); changed = true; }
                }
                else if (editStartLine <= f.end)
                {
                    _folds.RemoveAt(i); // edit touches the region — reveal it
                    changed = true;
                }
            }
            if (changed) RecomputeWrap();
        }

        /// <summary>Rows a line occupies: 0 when hidden inside a fold.</summary>
        int VisualRowsOfLine(int line, int wrapRows) =>
            IsLineHidden(line) ? 0 : wrapRows;

        // ---------- Indentation guides ----------

        void RefreshIndentGuides(int firstRow, int visible)
        {
            int quad = 0;
            if (_indentGuides)
            {
                float spaceW = CharWidth(' ');
                var guideColor = new Color(_textColor.r, _textColor.g, _textColor.b, 0.09f);
                for (int i = 0; i < visible; i++)
                {
                    int row = firstRow + i;
                    if (row >= _totalRows) break;
                    RowToLineSub(row, out int line, out _);
                    int indent = IndentOfLineForGuides(line);
                    for (int k = 1; k * TabSize < indent; k++)
                    {
                        if (quad >= _guidePool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Insert(0, q); // beneath everything
                            _guidePool.Add(q);
                        }
                        var g = _guidePool[quad++];
                        g.style.display = DisplayStyle.Flex;
                        g.style.backgroundColor = guideColor;
                        g.style.left = k * TabSize * spaceW;
                        g.style.top = row * _lineHeight;
                        g.style.width = 1;
                        g.style.height = _lineHeight;
                    }
                }
            }
            for (int i = quad; i < _guidePool.Count; i++)
                _guidePool[i].style.display = DisplayStyle.None;
        }

        /// <summary>Leading-space count; blank lines inherit the previous
        /// non-blank line's indent so guides span through gaps.</summary>
        int IndentOfLineForGuides(int line)
        {
            for (int l = line; l >= 0 && l > line - 50; l--)
            {
                string t = _lines[l];
                if (t.Trim().Length == 0 && l != line) continue;
                if (t.Trim().Length == 0) continue;
                int ind = 0;
                while (ind < t.Length && t[ind] == ' ') ind++;
                return ind;
            }
            return 0;
        }
    }
}
#endif
