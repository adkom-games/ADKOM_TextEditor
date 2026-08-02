#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Text comparison core for the Diff / Merge tool: line-level two-way
    /// diff (Myers O(ND)), intra-line change ranges for changed line pairs,
    /// and a diff3-style three-way merge that classifies each region as
    /// clean (one side changed, or both made the identical change) or a
    /// conflict carrying all three variants.
    /// </summary>
    internal static class DiffEngine
    {
        public enum Op { Equal, Delete, Insert, Replace }

        /// <summary>One aligned run: A[AStart..+ACount) vs B[BStart..+BCount).
        /// Equal: both sides identical. Delete: only in A. Insert: only in
        /// B. Replace: differing runs paired up.</summary>
        public sealed class Block
        {
            public Op Op;
            public int AStart, ACount, BStart, BCount;
        }

        /// <summary>Splits text into lines for diffing. "\n" only — ATE
        /// documents are normalized; foreign files are normalized here.</summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        /// <summary>Line-level diff of a vs b as a list of blocks covering
        /// both sequences in order. Adjacent Delete+Insert runs are fused
        /// into Replace blocks so the UI can pair changed lines.</summary>
        public static List<Block> DiffLines(string[] a, string[] b)
        {
            // Hash lines once so comparisons are int comparisons.
            var ha = HashAll(a);
            var hb = HashAll(b);
            var raw = new List<Block>();
            Myers(ha, 0, ha.Length, hb, 0, hb.Length, a, b, raw);
            return Fuse(raw);
        }

        static int[] HashAll(string[] lines)
        {
            var h = new int[lines.Length];
            for (int i = 0; i < lines.Length; i++) h[i] = lines[i].GetHashCode();
            return h;
        }

        // Classic Myers greedy O(ND) with linear-space divide and conquer
        // (find middle snake, recurse). Hash equality is verified against
        // the real strings to be collision-safe.
        static void Myers(int[] ha, int a0, int a1, int[] hb, int b0, int b1,
            string[] a, string[] b, List<Block> outBlocks)
        {
            // Strip common prefix / suffix — cheap and keeps recursion small.
            int p0 = 0;
            while (a0 + p0 < a1 && b0 + p0 < b1 && LineEq(ha, a, a0 + p0, hb, b, b0 + p0)) p0++;
            if (p0 > 0) { AddRun(outBlocks, Op.Equal, a0, p0, b0, p0); a0 += p0; b0 += p0; }
            int s0 = 0;
            while (a1 - s0 > a0 && b1 - s0 > b0 && LineEq(ha, a, a1 - 1 - s0, hb, b, b1 - 1 - s0)) s0++;
            int sa = a1 - s0, sb = b1 - s0;

            int n = sa - a0, m = sb - b0;
            if (n == 0 && m == 0) { }
            else if (n == 0) AddRun(outBlocks, Op.Insert, a0, 0, b0, m);
            else if (m == 0) AddRun(outBlocks, Op.Delete, a0, n, b0, 0);
            else
            {
                // Middle snake via the linear-space refinement.
                FindMiddle(ha, a0, sa, hb, b0, sb, a, b, out int mx, out int my);
                if ((mx == a0 && my == b0) || (mx == sa && my == sb))
                {
                    // Degenerate split (no progress) — emit the safe,
                    // possibly suboptimal answer instead of recursing.
                    AddRun(outBlocks, Op.Delete, a0, n, b0, 0);
                    AddRun(outBlocks, Op.Insert, sa, 0, b0, m);
                }
                else
                {
                    Myers(ha, a0, mx, hb, b0, my, a, b, outBlocks);
                    Myers(ha, mx, sa, hb, my, sb, a, b, outBlocks);
                }
            }

            if (s0 > 0) AddRun(outBlocks, Op.Equal, sa, s0, sb, s0);
        }

        static bool LineEq(int[] ha, string[] a, int i, int[] hb, string[] b, int j)
            => ha[i] == hb[j] && string.Equals(a[i], b[j], StringComparison.Ordinal);

        // Finds a split point (mx,my) on an optimal path using forward and
        // backward D-paths (Myers' linear-space middle snake, simplified to
        // return just the meeting point).
        static void FindMiddle(int[] ha, int a0, int a1, int[] hb, int b0, int b1,
            string[] a, string[] b, out int mx, out int my)
        {
            int n = a1 - a0, m = b1 - b0;
            int max = (n + m + 1) / 2 + 1;
            int delta = n - m;
            bool odd = (delta & 1) != 0;
            var vf = new int[2 * max + 2];
            var vb = new int[2 * max + 2];
            int off = max;
            vf[off + 1] = 0;
            vb[off + 1] = 0;
            for (int d = 0; d <= max; d++)
            {
                for (int k = -d; k <= d; k += 2)
                {
                    int x = (k == -d || (k != d && vf[off + k - 1] < vf[off + k + 1]))
                        ? vf[off + k + 1] : vf[off + k - 1] + 1;
                    int y = x - k;
                    while (x < n && y < m && LineEq(ha, a, a0 + x, hb, b, b0 + y)) { x++; y++; }
                    vf[off + k] = x;
                    if (odd && k - delta >= -(d - 1) && k - delta <= d - 1
                        && x + vb[off + (delta - k)] >= n)
                    { mx = a0 + x; my = b0 + y; return; }
                }
                for (int k = -d; k <= d; k += 2)
                {
                    int x = (k == -d || (k != d && vb[off + k - 1] < vb[off + k + 1]))
                        ? vb[off + k + 1] : vb[off + k - 1] + 1;
                    int y = x - k;
                    while (x < n && y < m && LineEq(ha, a, a1 - 1 - x, hb, b, b1 - 1 - y)) { x++; y++; }
                    vb[off + k] = x;
                    if (!odd && k - delta >= -d && k - delta <= d
                        && x + vf[off + (delta - k)] >= n)
                    { mx = a1 - x; my = b1 - y; return; }
                }
            }
            // Unreachable for well-formed input; fall back to a midpoint cut.
            mx = a0 + n / 2; my = b0 + m / 2;
        }

        static void AddRun(List<Block> list, Op op, int aStart, int aCount, int bStart, int bCount)
        {
            var last = list.Count > 0 ? list[list.Count - 1] : null;
            if (last != null && last.Op == op
                && last.AStart + last.ACount == aStart && last.BStart + last.BCount == bStart)
            { last.ACount += aCount; last.BCount += bCount; return; }
            list.Add(new Block { Op = op, AStart = aStart, ACount = aCount, BStart = bStart, BCount = bCount });
        }

        /// <summary>Fuses adjacent Delete+Insert (either order) into Replace
        /// blocks so the view can pair changed lines side by side.</summary>
        static List<Block> Fuse(List<Block> raw)
        {
            var res = new List<Block>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var cur = raw[i];
                if (i + 1 < raw.Count && cur.Op != Op.Equal && raw[i + 1].Op != Op.Equal
                    && cur.Op != raw[i + 1].Op)
                {
                    var del = cur.Op == Op.Delete ? cur : raw[i + 1];
                    var ins = cur.Op == Op.Insert ? cur : raw[i + 1];
                    res.Add(new Block
                    {
                        Op = Op.Replace,
                        AStart = del.AStart, ACount = del.ACount,
                        BStart = ins.BStart, BCount = ins.BCount
                    });
                    i++;
                    continue;
                }
                res.Add(cur);
            }
            return res;
        }

        /// <summary>Intra-line changed span for a paired line: common prefix
        /// and suffix are trimmed; what remains differs. Returns start/length
        /// per side (lenA/lenB may be 0 for pure insertion/deletion points).</summary>
        public static void IntraLine(string a, string b, out int start, out int lenA, out int lenB)
        {
            int p = 0, minLen = Math.Min(a.Length, b.Length);
            while (p < minLen && a[p] == b[p]) p++;
            int s = 0;
            while (s < minLen - p && a[a.Length - 1 - s] == b[b.Length - 1 - s]) s++;
            start = p;
            lenA = a.Length - p - s;
            lenB = b.Length - p - s;
        }

        // ---------- Three-way merge (diff3) ----------

        public enum ChunkKind { Clean, Conflict }

        /// <summary>One merge region. Clean chunks carry the resolved lines.
        /// Conflict chunks carry all three variants for the UI to resolve.</summary>
        public sealed class MergeChunk
        {
            public ChunkKind Kind;
            public string[] Lines;                    // Clean: the output lines
            public string[] Base, Left, Right;        // Conflict: the variants
            public int BaseStart;                     // base line where the region begins (for context labels)
        }

        /// <summary>Three-way merge of left and right against their common
        /// base. Regions changed on one side only merge cleanly; identical
        /// changes on both sides merge cleanly; diverging changes become
        /// Conflict chunks. Adjacent (touching, non-overlapping) edits stay
        /// separate; two insertions at the same point conflict.</summary>
        public static List<MergeChunk> Merge3(string[] baseLines, string[] left, string[] right)
        {
            var lBlocks = DiffLines(baseLines, left);
            var rBlocks = DiffLines(baseLines, right);
            var lRegions = ChangedRegions(lBlocks);
            var rRegions = ChangedRegions(rBlocks);
            var chunks = new List<MergeChunk>();
            int bi = 0;                        // base line cursor
            int lr = 0, rr = 0;                // region indices per side
            var clean = new List<string>();
            void FlushClean()
            {
                if (clean.Count == 0) return;
                chunks.Add(new MergeChunk { Kind = ChunkKind.Clean, Lines = clean.ToArray(), BaseStart = -1 });
                clean = new List<string>();
            }
            while (true)
            {
                var nl = lr < lRegions.Count ? lRegions[lr] : null;
                var nr = rr < rRegions.Count ? rRegions[rr] : null;
                if (nl == null && nr == null)
                {
                    while (bi < baseLines.Length) { clean.Add(baseLines[bi]); bi++; }
                    FlushClean();
                    break;
                }
                int nextStart = Math.Min(nl?.BaseStart ?? int.MaxValue, nr?.BaseStart ?? int.MaxValue);
                while (bi < nextStart) { clean.Add(baseLines[bi]); bi++; }
                // Cluster overlapping regions from both sides into one area.
                // Absorb on true overlap, or at the boundary only while the
                // cluster is still zero-width (two insertions at one point).
                int bStart = nextStart, bEnd = nextStart;
                bool useL = false, useR = false;
                bool Absorbs(Region reg) =>
                    reg.BaseStart < bEnd || (reg.BaseStart == bEnd && bStart == bEnd);
                while (true)
                {
                    bool grew = false;
                    if (lr < lRegions.Count && Absorbs(lRegions[lr]))
                    { bEnd = Math.Max(bEnd, lRegions[lr].BaseEnd); useL = true; lr++; grew = true; }
                    if (rr < rRegions.Count && Absorbs(rRegions[rr]))
                    { bEnd = Math.Max(bEnd, rRegions[rr].BaseEnd); useR = true; rr++; grew = true; }
                    if (!grew) break;
                }
                // Map the base region bounds into left/right coordinates.
                // Starts exclude an insertion sitting exactly at the bound;
                // ends include it — so inserted lines land inside the region.
                int lStart = Map(lBlocks, bStart, end: false), lEnd = Map(lBlocks, bEnd, end: true);
                int rStart = Map(rBlocks, bStart, end: false), rEnd = Map(rBlocks, bEnd, end: true);
                var bSeg = Slice(baseLines, bStart, bEnd);
                var lSeg = Slice(left, lStart, lEnd);
                var rSeg = Slice(right, rStart, rEnd);
                if (!useR || SameLines(lSeg, rSeg))
                {
                    AppendLines(clean, lSeg);             // left-only change, or identical change
                }
                else if (!useL)
                {
                    AppendLines(clean, rSeg);             // right-only change
                }
                else
                {
                    FlushClean();
                    chunks.Add(new MergeChunk
                    {
                        Kind = ChunkKind.Conflict,
                        Base = bSeg, Left = lSeg, Right = rSeg, BaseStart = bStart
                    });
                }
                bi = bEnd;
            }
            return chunks;
        }

        sealed class Region { public int BaseStart, BaseEnd; }

        static List<Region> ChangedRegions(List<Block> blocks)
        {
            var res = new List<Region>();
            foreach (var blk in blocks)
            {
                if (blk.Op == Op.Equal) continue;
                var last = res.Count > 0 ? res[res.Count - 1] : null;
                if (last != null && last.BaseEnd >= blk.AStart)
                    last.BaseEnd = Math.Max(last.BaseEnd, blk.AStart + blk.ACount);
                else
                    res.Add(new Region { BaseStart = blk.AStart, BaseEnd = blk.AStart + blk.ACount });
            }
            return res;
        }

        /// <summary>Maps a base line index to the corresponding line index in
        /// the other sequence using the diff blocks (base is the A side).
        /// An insertion block sitting exactly at the index counts only when
        /// mapping a region END, so inserted lines fall inside the region.</summary>
        static int Map(List<Block> blocks, int baseLine, bool end)
        {
            int shift = 0;
            foreach (var blk in blocks)
            {
                int blkEnd = blk.AStart + blk.ACount;
                if (blk.ACount == 0)
                {
                    // Pure insertion at blk.AStart.
                    if (blk.AStart < baseLine || (end && blk.AStart == baseLine))
                    { shift += blk.BCount; continue; }
                    break;
                }
                if (blkEnd <= baseLine) { shift += blk.BCount - blk.ACount; continue; }
                if (blk.AStart >= baseLine) break;
                // Inside a changed block: clamp to its B-side extent (regions
                // are built on block boundaries, so this is an edge case).
                return blk.BStart + Math.Min(baseLine - blk.AStart, blk.BCount);
            }
            return baseLine + shift;
        }

        static string[] Slice(string[] src, int start, int end)
        {
            start = Math.Max(0, Math.Min(start, src.Length));
            end = Math.Max(start, Math.Min(end, src.Length));
            var res = new string[end - start];
            Array.Copy(src, start, res, 0, end - start);
            return res;
        }

        static bool SameLines(string[] x, string[] y)
        {
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (!string.Equals(x[i], y[i], StringComparison.Ordinal)) return false;
            return true;
        }

        static void AppendLines(List<string> clean, string[] lines)
        { foreach (var l in lines) clean.Add(l); }
    }
}
#endif
