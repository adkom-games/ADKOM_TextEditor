#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace ADKOM.TextEditor
{
    // History navigation: read-only views over undo/redo stacks for the
    // History window — a summary row per undo step (the unit one Undo()
    // reverts) and full document reconstruction at any point, computed from
    // the stored deltas (never stored snapshots, so memory stays flat).
    // The static ...For variants work over ANY document's detached UndoWorld
    // (the History window's per-tab browsing); the instance methods serve the
    // live view's attached world.
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

        /// <summary>The ACTIVE document's world (attached to this view) — the
        /// History window reads inactive documents' worlds off TextDocument.</summary>
        internal UndoWorld LiveUndoWorld => _world;

        internal List<HistoryRow> HistoryRows() => HistoryRowsFor(_world, GetValueInternal());

        internal string HistoryStateAt(int undoSteps, int redoSteps, out int changeLine) =>
            HistoryStateFor(_world, GetValueInternal(), undoSteps, redoSteps, out changeLine);

        /// <summary>The timeline, newest-future first: redo entries (furthest
        /// future at the top), then the current state, then past entries, then
        /// the reachable original. Empty when nothing is recorded.</summary>
        internal static List<HistoryRow> HistoryRowsFor(UndoWorld world, string current)
        {
            var undo = world.Undo;
            var redo = world.Redo;
            var rows = new List<HistoryRow>(undo.Count + redo.Count + 1);
            if (undo.Count == 0 && redo.Count == 0) return rows;

            // Future: redo all the way lands after redo[0], so walk from the
            // far end. State evolves forward as we go, giving exact lines.
            var futures = new List<HistoryRow>();
            string t = current;
            for (int i = redo.Count - 1; i >= 0; i--)
            {
                t = ApplyForward(t, redo[i]);
                futures.Add(new HistoryRow
                {
                    Summary = Describe(redo[i]),
                    Line = LineOfOffset(t, redo[i].Start),
                    RedoSteps = redo.Count - i
                });
            }
            for (int i = futures.Count - 1; i >= 0; i--) rows.Add(futures[i]); // furthest first

            // Current: the newest undo entry's after-state is the buffer itself.
            if (undo.Count > 0)
            {
                rows.Add(new HistoryRow
                {
                    Summary = Describe(undo[undo.Count - 1]),
                    Line = LineOfOffset(current, undo[undo.Count - 1].Start),
                    IsCurrent = true
                });
            }
            else rows.Add(new HistoryRow { Summary = string.Empty, IsCurrent = true });

            // Past: walking back through the undo stack, reverting as we go so
            // each row's line is exact in ITS state.
            string p = current;
            for (int i = undo.Count - 1; i >= 1; i--)
            {
                p = ApplyRevert(p, undo[i]);
                rows.Add(new HistoryRow
                {
                    Summary = Describe(undo[i - 1]),
                    Line = LineOfOffset(p, undo[i - 1].Start),
                    UndoSteps = undo.Count - i
                });
            }
            if (undo.Count > 0)
                rows.Add(new HistoryRow { IsOriginal = true, UndoSteps = undo.Count });
            return rows;
        }

        /// <summary>The document text at a point on the timeline: undoSteps
        /// back, or redoSteps forward (one of them 0). changeLine is the line
        /// of the last change between here and that state, for scrolling.</summary>
        internal static string HistoryStateFor(UndoWorld world, string current,
            int undoSteps, int redoSteps, out int changeLine)
        {
            string t = current;
            changeLine = 0;
            for (int i = 0; i < undoSteps && i < world.Undo.Count; i++)
            {
                var op = world.Undo[world.Undo.Count - 1 - i];
                t = ApplyRevert(t, op);
                changeLine = LineOfOffset(t, op.Start);
            }
            for (int i = 0; i < redoSteps && i < world.Redo.Count; i++)
            {
                var op = world.Redo[world.Redo.Count - 1 - i];
                t = ApplyForward(t, op);
                changeLine = LineOfOffset(t, op.Start);
            }
            return t;
        }

        internal static string ApplyRevert(string text, UndoOp op)
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

        internal static string ApplyForward(string text, UndoOp op)
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
