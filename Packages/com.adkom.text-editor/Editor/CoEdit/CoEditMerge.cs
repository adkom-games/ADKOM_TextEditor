// =============================================================================
//  ADKOM Text Editor — shared-document co-editing: edit model and merge.
//  Implements the ATE half of ALS_ATE_CoEditing_Contract.md v1.0.
//
//  An edit is (at, del, ins) in UTF-16 code units — the same units C# string
//  indexing uses, which is why no conversion ever happens at the bridge.
//
//  Merge is operational transform against a TOTAL ORDER: the ALS relay
//  sequences every batch, so there are no vector clocks and no tie-break
//  ambiguity beyond concurrent inserts at the identical offset, which are
//  ordered by author id so every machine resolves them the same way.
//
//  This file is deliberately free of UnityEngine and of ALS: it is pure text
//  algebra and can be reasoned about (and unit-tested) on its own.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace ADKOM.TextEditor.CoEdit
{
    /// <summary>One primitive edit: delete `Del` code units at `At`, then insert
    /// `Ins` there. Insert-only has Del == 0; delete-only has empty Ins.</summary>
    internal readonly struct Edit
    {
        public readonly int At;
        public readonly int Del;
        public readonly string Ins;

        public Edit(int at, int del, string ins)
        {
            At = at < 0 ? 0 : at;
            Del = del < 0 ? 0 : del;
            Ins = ins ?? "";
        }

        public int End => At + Del;
        public int Delta => Ins.Length - Del;
        public bool IsNoOp => Del == 0 && Ins.Length == 0;

        public override string ToString() => $"({At},-{Del},+{Ins.Length})";
    }

    /// <summary>A batch of edits made against one base sequence number, applied
    /// in array order. Atomic on the wire (contract §5).</summary>
    internal sealed class EditBatch
    {
        public string Doc = "";
        public long Base = -1;
        public readonly List<Edit> Edits = new List<Edit>();

        public bool IsEmpty
        {
            get
            {
                foreach (var e in Edits)
                    if (!e.IsNoOp)
                        return false;
                return true;
            }
        }
    }

    internal static class CoEditMerge
    {
        // ------------------------------------------------------------- applying

        /// <summary>Apply a batch to text. Edits apply in array order, each
        /// against the result of the previous one — the same order the author's
        /// editor produced them in.</summary>
        public static string Apply(string text, EditBatch batch)
        {
            if (batch == null || batch.Edits.Count == 0)
                return text;
            var sb = new StringBuilder(text ?? "");
            foreach (var e in batch.Edits)
            {
                var at = Clamp(e.At, 0, sb.Length);
                var del = Clamp(e.Del, 0, sb.Length - at);
                if (del > 0)
                    sb.Remove(at, del);
                if (e.Ins.Length > 0)
                    sb.Insert(at, e.Ins);
            }
            return sb.ToString();
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // ---------------------------------------------------------- transforming

        /// <summary>Transform `a` so it can be applied AFTER `b`, where both were
        /// authored against the same text, appending the result to `outp`.
        /// `aFirst` breaks the one genuine tie — two inserts at the identical
        /// offset — and must be computed identically on every machine (author id
        /// ordering) or replicas diverge.
        ///
        /// The convention, which both directions must agree on or convergence
        /// fails: text typed inside a range someone else is concurrently
        /// deleting SURVIVES, landing immediately after whatever replaced that
        /// range. Keeping it costs a split — a delete straddling a concurrent
        /// insertion becomes two deletes, one either side — and that split is
        /// exactly why this returns a list rather than one edit. The cheaper
        /// alternative (let the delete swallow it) silently eats characters a
        /// human just typed, which is not a trade worth making.</summary>
        public static void TransformInto(Edit a, Edit b, bool aFirst, List<Edit> outp)
        {
            if (b.IsNoOp)
            {
                outp.Add(a);
                return;
            }

            // Disjoint: b entirely before a — shift a by b's net length change.
            // (Two inserts at the same offset look like this but are the tie case.)
            if (b.End <= a.At && !(b.End == a.At && b.Del == 0 && a.Del == 0))
            {
                outp.Add(new Edit(a.At + b.Delta, a.Del, a.Ins));
                return;
            }

            // Disjoint: a entirely before b — unaffected.
            if (a.End <= b.At && !(a.End == b.At && a.Del == 0 && b.Del == 0))
            {
                outp.Add(a);
                return;
            }

            // Concurrent inserts at the identical offset: a stable, agreed order.
            if (a.Del == 0 && b.Del == 0 && a.At == b.At)
            {
                outp.Add(aFirst ? a : new Edit(a.At + b.Ins.Length, 0, a.Ins));
                return;
            }

            // a deletes a range that strictly contains b's insertion point: keep
            // b's text by deleting either side of it.
            if (b.Del == 0 && a.At < b.At && b.At < a.End)
            {
                var head = new Edit(a.At, b.At - a.At, a.Ins);
                if (head.Del > 0 || head.Ins.Length > 0)
                    outp.Add(head);
                var tailAt = a.At + a.Ins.Length + b.Ins.Length;
                var tailDel = a.End - b.At;
                if (tailDel > 0)
                    outp.Add(new Edit(tailAt, tailDel, ""));
                return;
            }

            // General overlap: map both endpoints through b.
            var start = MapThrough(a.At, b);
            var end = MapThrough(a.End, b);
            var del = end - start;
            outp.Add(new Edit(start, del < 0 ? 0 : del, a.Ins));
        }

        /// <summary>Single-result transform, for the cases that cannot split
        /// (used when carrying an edit forward past another).</summary>
        public static Edit Transform(Edit a, Edit b, bool aFirst)
        {
            var tmp = new List<Edit>(2);
            TransformInto(a, b, aFirst, tmp);
            if (tmp.Count == 1)
                return tmp[0];
            if (tmp.Count == 0)
                return new Edit(a.At, 0, "");
            // A split collapsed into one edit spans both halves: only ever used
            // to carry a peer's edit past another edit of the SAME batch, and
            // ATE emits one edit per batch, so this path is unreachable in
            // practice and deliberately conservative rather than clever.
            var first = tmp[0];
            var last = tmp[tmp.Count - 1];
            return new Edit(first.At, last.End - first.At, first.Ins);
        }

        static int MapThrough(int pos, Edit b)
        {
            if (pos <= b.At)
                return pos;
            if (pos >= b.End)
                return pos + b.Delta;
            // Inside b's deleted range: land AFTER whatever replaced it, which is
            // the same place the split above puts surviving text.
            return b.At + b.Ins.Length;
        }

        /// <summary>Transform every edit of `a` past every edit of `b`.</summary>
        public static EditBatch Transform(EditBatch a, EditBatch b, bool aFirst)
        {
            var current = new List<Edit>(a.Edits);
            foreach (var be in b.Edits)
            {
                var next = new List<Edit>(current.Count + 1);
                var carried = be;
                foreach (var ae in current)
                {
                    TransformInto(ae, carried, aFirst, next);
                    carried = Transform(carried, ae, !aFirst);
                }
                current = next;
            }
            var result = new EditBatch { Doc = a.Doc, Base = a.Base };
            result.Edits.AddRange(current);
            return result;
        }

        /// <summary>Transform a whole batch past one edit.</summary>
        public static EditBatch Transform(EditBatch a, Edit b, bool aFirst)
        {
            var wrapper = new EditBatch();
            wrapper.Edits.Add(b);
            return Transform(a, wrapper, aFirst);
        }

        /// <summary>Stable, machine-independent tie-break for concurrent inserts
        /// at the same offset: ordinal author-id comparison.</summary>
        public static bool AuthorWins(string a, string b) =>
            string.CompareOrdinal(a ?? "", b ?? "") < 0;

        // ------------------------------------------------------------------ json

        /// <summary>Serialize to the contract §5 wire shape. Hand-rolled because
        /// ATE must not take a dependency on ALS's JSON (or on any package) for
        /// a fixed three-field object.</summary>
        public static string ToJson(EditBatch b)
        {
            var sb = new StringBuilder(64 + b.Edits.Count * 24);
            sb.Append("{\"doc\":");
            AppendString(sb, b.Doc);
            sb.Append(",\"base\":").Append(b.Base).Append(",\"edits\":[");
            for (var i = 0; i < b.Edits.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                var e = b.Edits[i];
                sb.Append("{\"at\":").Append(e.At)
                  .Append(",\"del\":").Append(e.Del)
                  .Append(",\"ins\":");
                AppendString(sb, e.Ins);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s ?? "")
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            sb.Append('"');
        }

        /// <summary>Parse the §5 wire shape. Returns null on anything malformed —
        /// callers drop the batch rather than corrupt a buffer.</summary>
        public static EditBatch FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            var i = 0;
            try
            {
                var batch = new EditBatch();
                Expect(json, ref i, '{');
                while (true)
                {
                    SkipWs(json, ref i);
                    if (i >= json.Length || json[i] == '}')
                        break;
                    var key = ReadString(json, ref i);
                    SkipWs(json, ref i);
                    Expect(json, ref i, ':');
                    SkipWs(json, ref i);
                    switch (key)
                    {
                        case "doc":
                            batch.Doc = ReadString(json, ref i);
                            break;
                        case "base":
                            batch.Base = ReadLong(json, ref i);
                            break;
                        case "edits":
                            ReadEdits(json, ref i, batch);
                            break;
                        default:
                            SkipValue(json, ref i);
                            break;
                    }
                    SkipWs(json, ref i);
                    if (i < json.Length && json[i] == ',')
                        i++;
                }
                return batch;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void ReadEdits(string s, ref int i, EditBatch batch)
        {
            Expect(s, ref i, '[');
            while (true)
            {
                SkipWs(s, ref i);
                if (s[i] == ']')
                {
                    i++;
                    return;
                }
                Expect(s, ref i, '{');
                int at = 0, del = 0;
                var ins = "";
                while (true)
                {
                    SkipWs(s, ref i);
                    if (s[i] == '}')
                    {
                        i++;
                        break;
                    }
                    var key = ReadString(s, ref i);
                    SkipWs(s, ref i);
                    Expect(s, ref i, ':');
                    SkipWs(s, ref i);
                    if (key == "at") at = (int)ReadLong(s, ref i);
                    else if (key == "del") del = (int)ReadLong(s, ref i);
                    else if (key == "ins") ins = ReadString(s, ref i);
                    else SkipValue(s, ref i);
                    SkipWs(s, ref i);
                    if (s[i] == ',')
                        i++;
                }
                batch.Edits.Add(new Edit(at, del, ins));
                SkipWs(s, ref i);
                if (s[i] == ',')
                    i++;
            }
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        static void Expect(string s, ref int i, char c)
        {
            SkipWs(s, ref i);
            if (s[i] != c)
                throw new FormatException($"expected '{c}'");
            i++;
        }

        static string ReadString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (s[i] != '"')
                throw new FormatException("expected string");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    switch (s[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                            i += 4;
                            break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(s[i]);
                }
                i++;
            }
            i++;
            return sb.ToString();
        }

        static long ReadLong(string s, ref int i)
        {
            SkipWs(s, ref i);
            var start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
                i++;
            while (i < s.Length && char.IsDigit(s[i]))
                i++;
            return long.Parse(s.Substring(start, i - start),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        static void SkipValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (s[i] == '"')
            {
                ReadString(s, ref i);
                return;
            }
            var depth = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    if (depth == 0) return;
                    depth--;
                }
                else if (c == ',' && depth == 0) return;
                i++;
            }
        }
    }
}
