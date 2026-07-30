#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace ADKOM.TextEditor
{
    // History navigation: read-only views over the undo/redo stacks for the
    // History window — a summary row per undo step (the unit one Undo()
    // reverts) and full document reconstruction at any point, computed from
    // the stored deltas (never stored snapshots, so memory stays flat).
    public partial class CodeView
    {
        internal struct HistoryRow
        {
            public string Summary;  // "+ \"abc\"", "− \"xyz\"", "\"a\" → \"b\"", "N-caret edit"
            public int Line;        // 0-based line of the change, in the state AFTER the edit
            public int UndoSteps;   // Undo() calls from CURRENT to reach the state after this edit
            public int RedoSteps;   // Redo() calls from CURRENT (future rows)
            public bool IsCurrent;  // the newest edit — its after-state IS the buffer
            public bool IsOriginal; // synthetic "before the first recorded edit" row
        }

        internal int UndoDepth => _undo.Count;
        internal int RedoDepth => _redo.Count;

        /// <summary>The timeline, newest-future first: redo entries (furthest
        /// future at the top), then the current state, then past entries, then
        /// the reachable original. Empty when nothing is recorded.</summary>
        internal List<HistoryRow> HistoryRows()
        {
            var rows = new List<HistoryRow>(_undo.Count + _redo.Count + 1);
            if (_undo.Count == 0 && _redo.Count == 0) return rows;

            // Future: redo all the way lands after _redo[0], so walk from the
            // far end. State evolves forward as we go, giving exact lines.
            var futures = new List<HistoryRow>();
            string t = GetValueInternal();
            for (int i = _redo.Count - 1; i >= 0; i--)
            {
                t = ApplyForward(t, _redo[i]);
                futures.Add(new HistoryRow
                {
                    Summary = Describe(_redo[i]),
                    Line = LineOfOffset(t, _redo[i].Start),
                    RedoSteps = _redo.Count - i
                });
            }
            for (int i = futures.Count - 1; i >= 0; i--) rows.Add(futures[i]); // furthest first

            // Current: the newest undo entry's after-state is the buffer itself.
            string cur = GetValueInternal();
            if (_undo.Count > 0)
            {
                rows.Add(new HistoryRow
                {
                    Summary = Describe(_undo[_undo.Count - 1]),
                    Line = LineOfOffset(cur, _undo[_undo.Count - 1].Start),
                    IsCurrent = true
                });
            }
            else rows.Add(new HistoryRow { Summary = string.Empty, IsCurrent = true });

            // Past: walking back through the undo stack, reverting as we go so
            // each row's line is exact in ITS state.
            string p = cur;
            for (int i = _undo.Count - 1; i >= 1; i--)
            {
                p = ApplyRevert(p, _undo[i]);
                rows.Add(new HistoryRow
                {
                    Summary = Describe(_undo[i - 1]),
                    Line = LineOfOffset(p, _undo[i - 1].Start),
                    UndoSteps = _undo.Count - i
                });
            }
            if (_undo.Count > 0)
                rows.Add(new HistoryRow { IsOriginal = true, UndoSteps = _undo.Count });
            return rows;
        }

        /// <summary>The document text at a point on the timeline: undoSteps
        /// back, or redoSteps forward (one of them 0). changeLine is the line
        /// of the last change between here and that state, for scrolling.</summary>
        internal string HistoryStateAt(int undoSteps, int redoSteps, out int changeLine)
        {
            string t = GetValueInternal();
            changeLine = 0;
            for (int i = 0; i < undoSteps && i < _undo.Count; i++)
            {
                var op = _undo[_undo.Count - 1 - i];
                t = ApplyRevert(t, op);
                changeLine = LineOfOffset(t, op.Start);
            }
            for (int i = 0; i < redoSteps && i < _redo.Count; i++)
            {
                var op = _redo[_redo.Count - 1 - i];
                t = ApplyForward(t, op);
                changeLine = LineOfOffset(t, op.Start);
            }
            return t;
        }

        static string ApplyRevert(string text, UndoOp op)
        {
            var sb = new StringBuilder(text);
            if (op.Segments != null)
            {
                foreach (var seg in op.Segments) // ascending: inverses land at Start
                    sb.Remove(seg.Start, seg.Inserted.Length).Insert(seg.Start, seg.Removed);
            }
            else
            {
                int start = System.Math.Min(op.Start, sb.Length);
                int len = System.Math.Min(op.Inserted.Length, sb.Length - start);
                sb.Remove(start, len).Insert(start, op.Removed);
            }
            return sb.ToString();
        }

        static string ApplyForward(string text, UndoOp op)
        {
            var sb = new StringBuilder(text);
            if (op.Segments != null)
            {
                for (int i = op.Segments.Count - 1; i >= 0; i--) // descending keeps starts valid
                {
                    var seg = op.Segments[i];
                    sb.Remove(seg.Start, seg.Removed.Length).Insert(seg.Start, seg.Inserted);
                }
            }
            else
            {
                int start = System.Math.Min(op.Start, sb.Length);
                int len = System.Math.Min(op.Removed.Length, sb.Length - start);
                sb.Remove(start, len).Insert(start, op.Inserted);
            }
            return sb.ToString();
        }

        static int LineOfOffset(string text, int offset)
        {
            int line = 0, n = System.Math.Min(offset, text.Length);
            for (int i = 0; i < n; i++) if (text[i] == '\n') line++;
            return line;
        }

        static string Describe(UndoOp op)
        {
            if (op.Segments != null)
                return string.Format(L10n.Tr("{0}-caret edit"), op.Segments.Count);
            string ins = Shorten(op.Inserted), rem = Shorten(op.Removed);
            if (op.Removed.Length == 0) return "+ \"" + ins + "\"";
            if (op.Inserted.Length == 0) return "− \"" + rem + "\"";
            return "\"" + rem + "\" → \"" + ins + "\"";
        }

        static string Shorten(string s)
        {
            s = s.Replace("\r", "").Replace("\n", "↵").Replace("\t", "→");
            return s.Length <= 24 ? s : s.Substring(0, 23) + "…";
        }
    }
}
#endif
