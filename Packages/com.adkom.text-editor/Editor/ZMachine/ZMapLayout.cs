#if UNITY_EDITOR
// ATE Z-Machine — shared geometry for drawing room connections (used by both
// the interactive map pane and the SVG export). A connection attaches to each
// room on the side/corner of the exit that leads to the other room, so a link
// leaves from the direction you actually travel (e.g. a SOUTHWEST exit leaves
// the room's bottom-left corner). Bidirectional links get an arrowhead at each
// end; one-way links only at the destination.
using System;
using System.Collections.Generic;

namespace AteZMachine
{
    /// <summary>One directed/undirected connection between two rooms, resolved
    /// to box-relative attach points and control offsets for a spline.</summary>
    internal struct MapEdge
    {
        public int A, B;            // room ids (A &lt; B)
        public Dir SideA, SideB;    // box side each end attaches to
        public bool ArrowA, ArrowB; // arrowhead pointing INTO that room (travel arrives there)
    }

    internal static class ZMapLayout
    {
        public static bool IsCompass(Dir d) => d <= Dir.SW;

        /// <summary>Attach point on a box for a compass direction, as fractions
        /// of the box (0,0 = top-left … 1,1 = bottom-right).</summary>
        public static void Edge(Dir d, out float fx, out float fy)
        {
            switch (d)
            {
                case Dir.N: fx = 0.5f; fy = 0f; break;
                case Dir.S: fx = 0.5f; fy = 1f; break;
                case Dir.E: fx = 1f; fy = 0.5f; break;
                case Dir.W: fx = 0f; fy = 0.5f; break;
                case Dir.NE: fx = 1f; fy = 0f; break;
                case Dir.NW: fx = 0f; fy = 0f; break;
                case Dir.SE: fx = 1f; fy = 1f; break;
                case Dir.SW: fx = 0f; fy = 1f; break;
                default: fx = 0.5f; fy = 0.5f; break;
            }
        }

        /// <summary>Outward normal at a box side (unit-ish), for spline control
        /// points so the curve leaves the box perpendicular to its edge.</summary>
        public static void Normal(Dir d, out float nx, out float ny)
        {
            const float q = 0.70711f;
            switch (d)
            {
                case Dir.N: nx = 0; ny = -1; break;
                case Dir.S: nx = 0; ny = 1; break;
                case Dir.E: nx = 1; ny = 0; break;
                case Dir.W: nx = -1; ny = 0; break;
                case Dir.NE: nx = q; ny = -q; break;
                case Dir.NW: nx = -q; ny = -q; break;
                case Dir.SE: nx = q; ny = q; break;
                case Dir.SW: nx = -q; ny = q; break;
                default: nx = 0; ny = 0; break;
            }
        }

        /// <summary>The compass direction of the grid cell of <paramref
        /// name="to"/> relative to <paramref name="from"/> — the fallback attach
        /// side when a room has no explicit exit back toward its neighbour.</summary>
        public static Dir GeometricDir(MapRoom from, MapRoom to)
        {
            int sx = Math.Sign(to.X - from.X), sy = Math.Sign(to.Y - from.Y);
            if (sx == 0 && sy < 0) return Dir.N;
            if (sx == 0 && sy > 0) return Dir.S;
            if (sy == 0 && sx > 0) return Dir.E;
            if (sy == 0 && sx < 0) return Dir.W;
            if (sx > 0 && sy < 0) return Dir.NE;
            if (sx < 0 && sy < 0) return Dir.NW;
            if (sx > 0 && sy > 0) return Dir.SE;
            if (sx < 0 && sy > 0) return Dir.SW;
            return Dir.N;
        }

        /// <summary>Collects the connections among the placed rooms on one level.
        /// Every compass exit whose destination is a placed room on the same
        /// level contributes; opposite exits between the same pair merge into a
        /// single two-headed edge.</summary>
        public static List<MapEdge> EdgesForLevel(ZMap map, int level)
        {
            // key = (min,max) room id → resolved edge under construction
            var acc = new Dictionary<long, MapEdge>();
            var hasA = new Dictionary<long, bool>();
            var hasB = new Dictionary<long, bool>();

            foreach (var r in map.Rooms.Values)
            {
                if (!r.Placed || r.Level != level) continue;
                foreach (var kv in r.Exits)
                {
                    if (!IsCompass(kv.Key)) continue;
                    if (!map.Rooms.TryGetValue(kv.Value, out var dest) || !dest.Placed || dest.Level != level) continue;
                    if (dest.Id == r.Id) continue;

                    int a = Math.Min(r.Id, dest.Id), b = Math.Max(r.Id, dest.Id);
                    long key = ((long)a << 32) | (uint)b;
                    acc.TryGetValue(key, out var e);
                    e.A = a; e.B = b;
                    if (r.Id == a) { e.SideA = kv.Key; e.ArrowB = true; hasA[key] = true; }
                    else { e.SideB = kv.Key; e.ArrowA = true; hasB[key] = true; }
                    acc[key] = e;
                }
            }

            var list = new List<MapEdge>(acc.Count);
            foreach (var kvp in acc)
            {
                var e = kvp.Value;
                var A = map.Rooms[e.A];
                var B = map.Rooms[e.B];
                if (!hasA.ContainsKey(kvp.Key)) e.SideA = GeometricDir(A, B); // no A→B exit: face B geometrically
                if (!hasB.ContainsKey(kvp.Key)) e.SideB = GeometricDir(B, A);
                list.Add(e);
            }
            return list;
        }
    }
}
#endif
