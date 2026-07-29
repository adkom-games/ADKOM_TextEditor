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
    /// <summary>One end of a connection: the box side/corner it attaches to.</summary>
    internal struct MapEndpoint
    {
        public int Room;
        public Dir Side;
    }

    /// <summary>One drawn connection between two attach endpoints. Two exits
    /// merge into a single edge ONLY when they share the same pair of endpoints
    /// (a genuine two-way corridor); different corridors between the same rooms
    /// stay separate. An arrow flag points INTO that endpoint's room (travel
    /// arrives there).</summary>
    internal struct MapEdge
    {
        public MapEndpoint E0, E1;
        public bool Arrow0, Arrow1;
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
        /// Each compass exit leaves its room on that direction's side and arrives
        /// at the destination on the side facing back (its geometric direction),
        /// so distinct corridors between the same rooms (e.g. a straight EAST
        /// link plus a curving SOUTH one) are kept as separate paths. Only exits
        /// resolving to the identical pair of endpoints — a real two-way corridor
        /// — merge into one edge with an arrowhead at both ends.</summary>
        public static List<MapEdge> EdgesForPage(ZMap map, int area, int level)
        {
            var acc = new Dictionary<string, MapEdge>();
            foreach (var r in map.Rooms.Values)
            {
                if (!r.Placed || r.Area != area || r.Level != level) continue;
                foreach (var kv in r.Exits)
                {
                    if (!IsCompass(kv.Key)) continue;
                    if (!map.Rooms.TryGetValue(kv.Value, out var dest) || !dest.Placed
                        || dest.Area != area || dest.Level != level) continue;
                    if (dest.Id == r.Id) continue;

                    var s0 = new MapEndpoint { Room = r.Id, Side = kv.Key };                 // leaves here
                    var s1 = new MapEndpoint { Room = dest.Id, Side = GeometricDir(dest, r) }; // arrives here

                    // Canonical endpoint order so the reverse exit maps to the
                    // same key; the arrow always points into the destination (s1).
                    bool s0First = Cmp(s0, s1) <= 0;
                    var ea = s0First ? s0 : s1;
                    var eb = s0First ? s1 : s0;
                    string key = ea.Room + ":" + (int)ea.Side + "-" + eb.Room + ":" + (int)eb.Side;

                    acc.TryGetValue(key, out var e);
                    e.E0 = ea; e.E1 = eb;
                    if (s0First) e.Arrow1 = true; else e.Arrow0 = true; // arrow into s1
                    acc[key] = e;
                }
            }
            return new List<MapEdge>(acc.Values);
        }

        static int Cmp(MapEndpoint a, MapEndpoint b) =>
            a.Room != b.Room ? a.Room - b.Room : (int)a.Side - (int)b.Side;

        /// <summary>A room's box for obstacle-aware routing.</summary>
        internal struct BoxRect { public int Id; public float X, Y, W, H; }

        /// <summary>Cubic-Bezier control points for a connection from p0 (leaving
        /// side <paramref name="sideA"/>) to p1 (entering side <paramref
        /// name="sideB"/>). Each control leaves along that side's outward normal;
        /// the curve is then bowed laterally to whichever side keeps it clear of
        /// the other room boxes (the two endpoint rooms are ignored). A plain
        /// opposite-facing corridor with a clear straight path stays straight.</summary>
        public static void RouteControls(float p0x, float p0y, Dir sideA, float p1x, float p1y, Dir sideB,
            IList<BoxRect> obstacles, int skipA, int skipB,
            out float c1x, out float c1y, out float c2x, out float c2y)
        {
            Normal(sideA, out float ax, out float ay);
            Normal(sideB, out float bx, out float by);
            float dx = p1x - p0x, dy = p1y - p0y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            float reach = Clamp(dist * 0.35f, 24f, 90f);

            float b1x = p0x + ax * reach, b1y = p0y + ay * reach;
            float b2x = p1x + bx * reach, b2y = p1y + by * reach;

            // Unit perpendicular to the straight run — the lateral bow axis.
            float len = dist > 0.001f ? dist : 1f;
            float px = -dy / len, py = dx / len;

            // Try increasing lateral offsets on both sides; pick the least the
            // curve overlaps any box, preferring the smallest bow.
            float[] ks = { 0f, 45f, -45f, 90f, -90f, 140f, -140f, 200f, -200f };
            float bestK = 0f; int bestHits = int.MaxValue;
            foreach (float k in ks)
            {
                float cc1x = b1x + px * k, cc1y = b1y + py * k;
                float cc2x = b2x + px * k, cc2y = b2y + py * k;
                int hits = CurveHits(p0x, p0y, cc1x, cc1y, cc2x, cc2y, p1x, p1y, obstacles, skipA, skipB);
                if (hits < bestHits || (hits == bestHits && Math.Abs(k) < Math.Abs(bestK)))
                { bestHits = hits; bestK = k; }
                if (bestHits == 0) break; // this offset is clear; no need to try wider ones
            }
            c1x = b1x + px * bestK; c1y = b1y + py * bestK;
            c2x = b2x + px * bestK; c2y = b2y + py * bestK;
        }

        // Samples the cubic between the endpoints and counts how many samples
        // land inside an obstacle box (endpoints' own rooms excluded, boxes
        // slightly inflated so the curve is nudged fully clear).
        static int CurveHits(float p0x, float p0y, float c1x, float c1y, float c2x, float c2y,
            float p1x, float p1y, IList<BoxRect> obstacles, int skipA, int skipB)
        {
            if (obstacles == null || obstacles.Count == 0) return 0;
            const float inflate = 6f;
            int hits = 0;
            for (int s = 1; s <= 8; s++)
            {
                float t = s / 9f, it = 1f - t;
                float x = it * it * it * p0x + 3 * it * it * t * c1x + 3 * it * t * t * c2x + t * t * t * p1x;
                float y = it * it * it * p0y + 3 * it * it * t * c1y + 3 * it * t * t * c2y + t * t * t * p1y;
                foreach (var b in obstacles)
                {
                    if (b.Id == skipA || b.Id == skipB) continue;
                    if (x >= b.X - inflate && x <= b.X + b.W + inflate &&
                        y >= b.Y - inflate && y <= b.Y + b.H + inflate) { hits++; break; }
                }
            }
            return hits;
        }

        /// <summary>The room a connection's travel starts FROM — the non-arrow
        /// end (arrows point INTO the destination). Two-way links use E0.</summary>
        public static int FromRoom(MapEdge e) =>
            (e.Arrow1 && !e.Arrow0) ? e.E0.Room : (e.Arrow0 && !e.Arrow1) ? e.E1.Room : e.E0.Room;

        /// <summary>Deterministic per-room colour (same every game — a hash of
        /// the room id, not randomness): a hue spread by the golden ratio, at a
        /// fixed dark saturation/value so light text stays readable.</summary>
        public static void NodeColor(int id, out float r, out float g, out float b)
        {
            float hue = Frac(id * 0.61803399f + 0.13f);
            HsvToRgb(hue, 0.55f, 0.34f, out r, out g, out b);
        }

        static float Frac(float v) { v -= (float)Math.Floor(v); return v < 0 ? v + 1f : v; }

        static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            float i = (float)Math.Floor(h * 6f);
            float f = h * 6f - i;
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            switch (((int)i) % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
#endif
