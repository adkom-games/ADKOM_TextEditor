#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Read/write reference highlighting: every occurrence of the symbol under
    // the caret gets a background fill — reads in the (existing) subtle match
    // tint, writes in a warmer amber — driven by the window's occurrence poll.
    // Complements selection-match highlighting: that one runs only WITH a
    // selection, this one only with a bare caret, so they never fight.
    public partial class CodeView
    {
        struct OccLine { public int Line, Start, Length; public bool IsWrite; }

        readonly List<VisualElement> _occPool = new List<VisualElement>();
        List<OccLine> _occ; // null = none
        static readonly Color WriteCol = new Color(0.9f, 0.55f, 0.2f, 0.30f);

        /// <summary>Replaces the highlighted occurrences; ignored if the
        /// document changed since they were computed.</summary>
        internal void ApplyOccurrences(List<SymbolOccurrence> occurrences, int forVersion)
        {
            if (forVersion != _docVersion) return;
            if (occurrences == null || occurrences.Count == 0) { ClearOccurrences(); return; }
            var list = new List<OccLine>(occurrences.Count);
            int docLen = GetValueInternal().Length;
            foreach (var o in occurrences)
            {
                if (o.Start < 0 || o.Start + o.Length > docLen) continue;
                IndexToLineCol(o.Start, out int line, out int col);
                list.Add(new OccLine { Line = line, Start = col, Length = o.Length, IsWrite = o.IsWrite });
            }
            _occ = list;
            RefreshVisible();
        }

        internal void ClearOccurrences()
        {
            if (_occ == null) return;
            _occ = null;
            RefreshVisible();
        }

        void RefreshOccurrences(int firstRow, int visible)
        {
            int quad = 0;
            if (_occ != null && !HasSelection)
            {
                for (int i = 0; i < visible; i++)
                {
                    int row = firstRow + i;
                    if (row >= _totalRows) break;
                    RowToLineSub(row, out int line, out int sub);
                    RowBounds(line, sub, out int rs, out int re);
                    foreach (var o in _occ)
                    {
                        if (o.Line != line) continue;
                        int cs = Mathf.Max(o.Start, rs), ce = Mathf.Min(o.Start + o.Length, re);
                        if (ce <= cs || cs > _lines[line].Length) continue;
                        ce = Mathf.Min(ce, _lines[line].Length);
                        if (quad >= _occPool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Insert(0, q); // beneath text
                            _occPool.Add(q);
                        }
                        var v = _occPool[quad++];
                        v.style.display = DisplayStyle.Flex;
                        v.style.backgroundColor = o.IsWrite ? WriteCol : _matchColor;
                        float x0 = MeasureRange(line, rs, cs);
                        v.style.left = x0;
                        v.style.top = row * _lineHeight;
                        v.style.width = Mathf.Max(2, MeasureRange(line, rs, ce) - x0);
                        v.style.height = _lineHeight;
                    }
                }
            }
            for (int i = quad; i < _occPool.Count; i++)
                _occPool[i].style.display = DisplayStyle.None;
        }
    }
}
#endif
