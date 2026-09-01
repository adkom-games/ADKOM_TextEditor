// =============================================================================
//  ADKOM Text Editor — co-editing convergence self-test.
//
//  Merge bugs are silent: two people keep typing and their files quietly stop
//  matching. So convergence is asserted mechanically rather than eyeballed —
//  every case below drives TWO independent CoEditDocument replicas through the
//  same relay ordering from opposite sides and fails unless both land on
//  identical text, which is the only property that actually matters.
//
//  Runs from Tools ▸ ADKOM ▸ Text Editor ▸ Co-Editing Self-Test, and returns a
//  string so it can be driven headlessly.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor.CoEdit
{
    public static class CoEditSelfTest
    {
        [MenuItem("Tools/ADKOM/Text Editor/Co-Editing Self-Test")]
        public static void RunFromMenu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[ATE] co-editing self-test\n");
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; log.Append("  PASS  ").Append(name).Append('\n'); }
                else { fail++; log.Append("  FAIL  ").Append(name).Append("  ").Append(detail).Append('\n'); }
            }

            // ---- wire round-trip -------------------------------------------
            {
                var b = new EditBatch { Doc = "A/b.cs", Base = 7 };
                b.Edits.Add(new Edit(3, 2, "he\"llo\n\tx"));
                var round = CoEditMerge.FromJson(CoEditMerge.ToJson(b));
                Check("batch survives a JSON round trip",
                    round != null && round.Doc == b.Doc && round.Base == b.Base
                    && round.Edits.Count == 1 && round.Edits[0].At == 3
                    && round.Edits[0].Del == 2 && round.Edits[0].Ins == "he\"llo\n\tx");
                Check("malformed JSON is refused, not guessed",
                    CoEditMerge.FromJson("{\"doc\":") == null);
            }

            // ---- two-replica convergence ------------------------------------
            // Both sides start from `start`, each makes its own edit, and the
            // relay orders A's first. Convergence means identical final text.
            string Converge(string start, Edit ea, Edit eb, out string ta, out string tb)
            {
                var a = new CoEditDocument("d") { SelfId = "uA" };
                var b = new CoEditDocument("d") { SelfId = "uB" };
                a.Reset(0, start, false);
                b.Reset(0, start, false);

                var ba = new EditBatch(); ba.Edits.Add(ea);
                var bb = new EditBatch(); bb.Edits.Add(eb);

                // Each applies its own edit locally and sends it.
                var ja = a.RecordLocal(ba, 1);
                var jb = b.RecordLocal(bb, 1);
                ta = CoEditMerge.Apply(start, CoEditMerge.FromJson(ja));
                tb = CoEditMerge.Apply(start, CoEditMerge.FromJson(jb));

                // Relay order: A gets seq 1, B gets seq 2.
                // A: own ack, then B's op.
                a.Acknowledged(1, 1);
                var forA = a.IncorporateRemote(2, "uB", jb);
                foreach (var e in forA)
                    ta = CoEditMerge.Apply(ta, Single(e));

                // B: A's op arrives first, then B's own ack.
                var forB = b.IncorporateRemote(1, "uA", ja);
                foreach (var e in forB)
                    tb = CoEditMerge.Apply(tb, Single(e));
                b.Acknowledged(1, 2);
                return ta == tb ? null : $"A=<{ta}> B=<{tb}>";
            }

            string d;
            d = Converge("Hello World", new Edit(0, 0, "X"), new Edit(6, 0, "Y"), out var a1, out var b1);
            Check("disjoint inserts converge", d == null, d ?? a1);

            d = Converge("Hello World", new Edit(3, 0, "X"), new Edit(3, 0, "Y"), out var a2, out var b2);
            Check("concurrent inserts at the SAME offset converge", d == null, d ?? a2);

            d = Converge("Hello World", new Edit(0, 5, ""), new Edit(6, 5, ""), out _, out _);
            Check("disjoint deletes converge", d == null, d);

            d = Converge("Hello World", new Edit(0, 7, ""), new Edit(4, 5, ""), out var a4, out _);
            Check("overlapping deletes converge", d == null, d ?? a4);

            d = Converge("Hello World", new Edit(2, 6, "ZZ"), new Edit(4, 0, "Q"), out var a5, out _);
            Check("insert inside a concurrently deleted range converges", d == null, d ?? a5);

            d = Converge("abc", new Edit(3, 0, "d"), new Edit(0, 3, ""), out var a6, out _);
            Check("append against full-delete converges", d == null, d ?? a6);

            // ---- a late sender (stale base) ---------------------------------
            {
                var a = new CoEditDocument("d") { SelfId = "uA" };
                a.Reset(0, "Hello World", false);
                // Two ops land from B and C, sequenced 1 and 2.
                var b1j = Batch(new Edit(0, 0, "1"), 0);
                a.IncorporateRemote(1, "uB", b1j);
                var c1j = Batch(new Edit(0, 0, "2"), 1);
                a.IncorporateRemote(2, "uC", c1j);
                // D was still at base 0 when it typed — its op must be lifted
                // past BOTH, not applied at face value.
                var d1j = Batch(new Edit(11, 0, "!"), 0);
                var edits = a.IncorporateRemote(3, "uD", d1j);
                var text = "Hello World";
                text = CoEditMerge.Apply(text, CoEditMerge.FromJson(b1j));
                text = CoEditMerge.Apply(text, CoEditMerge.FromJson(c1j));
                foreach (var e in edits)
                    text = CoEditMerge.Apply(text, Single(e));
                Check("a stale-base batch is lifted past the ops it missed",
                    text == "21Hello World!", text);
            }

            // ---- offsets are code units, not codepoints ----------------------
            {
                var s = "a\U0001F600b"; // emoji is two UTF-16 code units
                var t = CoEditMerge.Apply(s, One(new Edit(3, 0, "!")));
                Check("UTF-16 code-unit offsets index surrogate pairs correctly",
                    t == "a\U0001F600!b", t);
            }

            log.Append($"  {pass} passed, {fail} failed\n");
            log.Append(fail == 0 ? "  CO-EDIT SELF-TEST PASS" : "  CO-EDIT SELF-TEST FAIL");
            return log.ToString();
        }

        static string Batch(Edit e, long baseSeq)
        {
            var b = new EditBatch { Doc = "d", Base = baseSeq };
            b.Edits.Add(e);
            return CoEditMerge.ToJson(b);
        }

        static EditBatch Single(Edit e)
        {
            var b = new EditBatch();
            b.Edits.Add(e);
            return b;
        }

        static EditBatch One(Edit e) => Single(e);
    }
}
