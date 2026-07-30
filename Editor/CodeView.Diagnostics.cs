#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Error highlighting: compiler diagnostics rendered as 2px underlines
    // (red = error, amber = warning) beneath the offending span, refreshed by
    // the debounced semantic pass. Hovering a marked span shows the message
    // in the shared hover tip; the diagnostics are version-gated exactly like
    // semantic classification, so stale results never land on edited text.
    public partial class CodeView
    {
        readonly List<VisualElement> _diagPool = new List<VisualElement>();
        Dictionary<int, List<DiagnosticItem>> _diagLines; // by 0-based line
        static readonly Color ErrCol = new Color(0.94f, 0.33f, 0.31f, 0.9f);
        static readonly Color WarnCol = new Color(0.9f, 0.74f, 0.23f, 0.8f);

        /// <summary>Total error/warning counts of the last applied pass, for
        /// the window's status display.</summary>
        internal int DiagErrorCount { get; private set; }
        internal int DiagWarningCount { get; private set; }

        /// <summary>Replaces the document's diagnostics; ignored if the
        /// document changed since they were computed.</summary>
        internal void ApplyDiagnostics(List<DiagnosticItem> items, int forVersion)
        {
            if (forVersion != _docVersion) return;
            int errs = 0, warns = 0;
            if (items == null || items.Count == 0) _diagLines = null;
            else
            {
                _diagLines = new Dictionary<int, List<DiagnosticItem>>();
                foreach (var d in items)
                {
                    if (d.IsError) errs++; else warns++;
                    (_diagLines.TryGetValue(d.Line, out var l)
                        ? l : _diagLines[d.Line] = new List<DiagnosticItem>(2)).Add(d);
                }
            }
            DiagErrorCount = errs;
            DiagWarningCount = warns;
            RefreshVisible();
        }

        /// <summary>Clears diagnostics (document switch / semantics off).</summary>
        internal void ClearDiagnostics()
        {
            _diagLines = null;
            DiagErrorCount = DiagWarningCount = 0;
            RefreshVisible();
        }

        void RefreshDiagnostics(int firstRow, int visible)
        {
            int quad = 0;
            if (_diagLines != null)
            {
                for (int i = 0; i < visible; i++)
                {
                    int row = firstRow + i;
                    if (row >= _totalRows) break;
                    RowToLineSub(row, out int line, out int sub);
                    if (!_diagLines.TryGetValue(line, out var diags)) continue;
                    RowBounds(line, sub, out int rs, out int re);
                    int lineLen = _lines[line].Length;
                    foreach (var d in diags)
                    {
                        // Clamp to this sub-row (and to the line's real text —
                        // edits since the pass may have shortened it).
                        int ds = Mathf.Min(d.Start, lineLen);
                        int de = Mathf.Min(d.Start + d.Length, lineLen);
                        int cs = Mathf.Max(ds, rs), ce = Mathf.Min(de, re);
                        if (ce < cs || (ce == cs && ds != de)) continue;
                        if (quad >= _diagPool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Add(q); // above text: a 2px strip under the glyphs
                            _diagPool.Add(q);
                        }
                        var v = _diagPool[quad++];
                        v.style.display = DisplayStyle.Flex;
                        v.style.backgroundColor = d.IsError ? ErrCol : WarnCol;
                        float x0 = MeasureRange(line, rs, cs);
                        float x1 = MeasureRange(line, rs, ce);
                        v.style.left = x0;
                        v.style.top = (row + 1) * _lineHeight - 2;
                        v.style.width = Mathf.Max(6, x1 - x0);
                        v.style.height = 2;
                    }
                }
            }
            for (int i = quad; i < _diagPool.Count; i++)
                _diagPool[i].style.display = DisplayStyle.None;
        }

        /// <summary>The diagnostic message covering (line, col), or null.</summary>
        internal string DiagnosticAt(int line, int col)
        {
            if (_diagLines == null || !_diagLines.TryGetValue(line, out var diags)) return null;
            string best = null;
            foreach (var d in diags)
                if (col >= d.Start && col <= d.Start + d.Length)
                    best = best == null ? d.Message : best + "\n" + d.Message;
            return best;
        }
    }
}
#endif
