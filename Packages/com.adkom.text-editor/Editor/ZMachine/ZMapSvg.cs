#if UNITY_EDITOR
// ATE Z-Machine — export the auto-map to a standalone SVG. Levels are stacked
// (highest at the top, like floors of a building). Room connections are drawn
// as splines with arrowheads showing travel direction: an arrowhead points INTO
// the room you arrive at, and a bidirectional link gets a head at both ends.
// Each end attaches to the side/corner of the exit that leads to the other room
// (so a SOUTHWEST exit leaves the bottom-left corner). UP/DOWN exits are drawn
// as dashed cross-level connectors so level changes are obvious. A LEGEND of
// every discovered object is drawn at the bottom — alphabetical, multi-column,
// with each item's name and current location. Pure rendering of the ZMap model.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AteZMachine
{
    internal static class ZMapSvg
    {
        const int CellW = 150, CellH = 92, BoxW = 128, BoxH = 74;
        const int Margin = 20, HeadingH = 26, SectionGap = 40;
        const int LegendColW = 260, LegendRowH = 18;

        const string PageBg = "#1e1e20";
        const string Border = "#808088";
        const string CurBorder = "#8cf299"; // "you are here" outline
        const string ItemCol = "#8cccff";
        const string TextCol = "#d9d9d9";

        static string Hx(float v)
        {
            int n = (int)(v * 255 + 0.5f);
            n = n < 0 ? 0 : (n > 255 ? 255 : n);
            return n.ToString("x2");
        }

        struct PageBox { public int Area, Level; public float HeadingY, Top; public int MinX, MinY, MaxX, MaxY; }

        public static string ToSvg(ZMap map)
        {
            // One page per (area, level). Exterior (area 0) first, then each
            // interior area; within an area the highest level is on top.
            var pages = new List<(int area, int level)>();
            foreach (var r in map.Rooms.Values)
                if (r.Placed && !pages.Contains((r.Area, r.Level))) pages.Add((r.Area, r.Level));
            pages.Sort((a, b) => a.area != b.area ? a.area - b.area : b.level - a.level);

            // ---- Pass 1: absolute room positions + page layout ----
            var pos = new Dictionary<int, (float x, float y)>();
            var layout = new List<PageBox>();
            float contentW = 200f, y = Margin;
            foreach (var (area, level) in pages)
            {
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var r in map.Rooms.Values)
                    if (r.Placed && r.Area == area && r.Level == level)
                    {
                        if (r.X < minX) minX = r.X; if (r.Y < minY) minY = r.Y;
                        if (r.X > maxX) maxX = r.X; if (r.Y > maxY) maxY = r.Y;
                    }
                if (minX == int.MaxValue) continue;

                float headingY = y;
                float top = y + HeadingH;
                foreach (var r in map.Rooms.Values)
                    if (r.Placed && r.Area == area && r.Level == level)
                        pos[r.Id] = (Margin + (r.X - minX) * CellW, top + (r.Y - minY) * CellH);

                layout.Add(new PageBox { Area = area, Level = level, HeadingY = headingY, Top = top,
                    MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY });
                contentW = Math.Max(contentW, Margin + (maxX - minX + 1) * CellW);
                y = top + (maxY - minY + 1) * CellH + SectionGap;
            }

            var body = new StringBuilder();

            // Obstacle rectangles (all placed rooms) for spline routing.
            var obstacles = new List<ZMapLayout.BoxRect>();
            foreach (var kv in pos)
                obstacles.Add(new ZMapLayout.BoxRect { Id = kv.Key, X = kv.Value.x, Y = kv.Value.y, W = BoxW, H = BoxH });

            // ---- Connections (under the boxes) ----
            // Intra-page: routed around boxes, stroked in the FROM room's colour.
            foreach (var lb in layout)
                foreach (var e in ZMapLayout.EdgesForPage(map, lb.Area, lb.Level))
                {
                    var pA = Attach(pos, e.E0.Room, e.E0.Side);
                    var pB = Attach(pos, e.E1.Room, e.E1.Side);
                    ZMapLayout.RouteControls(pA.x, pA.y, e.E0.Side, pB.x, pB.y, e.E1.Side,
                        obstacles, e.E0.Room, e.E1.Room, out float c1x, out float c1y, out float c2x, out float c2y);
                    body.Append(Spline(pA, pB, c1x, c1y, c2x, c2y, e.Arrow0, e.Arrow1, HexColor(ZMapLayout.FromRoom(e)), false));
                }
            // Exits that cross to another page (a level change OR entering/leaving
            // an area) are drawn as dashed cross-page connectors, coloured by the
            // FROM room.
            foreach (var e in CrossEdges(map, pos))
            {
                ZMapLayout.RouteControls(e.pUpper.x, e.pUpper.y, Dir.S, e.pLower.x, e.pLower.y, Dir.N,
                    null, -1, -1, out float c1x, out float c1y, out float c2x, out float c2y);
                body.Append(Spline(e.pUpper, e.pLower, c1x, c1y, c2x, c2y, e.arrowUpper, e.arrowLower, HexColor(e.fromRoom), true));
            }

            // ---- Boxes, text, markers, items ----
            foreach (var lb in layout)
            {
                body.Append(Text(Margin, (int)lb.HeadingY + 16, Esc(PageHeading(map, lb.Area, lb.Level)), TextCol, 14, true));
                foreach (var r in map.Rooms.Values)
                {
                    if (!r.Placed || r.Area != lb.Area || r.Level != lb.Level) continue;
                    var (px, py) = pos[r.Id];
                    bool cur = r.Id == map.CurrentRoomId;
                    ZMapLayout.NodeColor(r.Id, out float nr, out float ng, out float nb);
                    string fill = cur
                        ? "#" + Hx(nr * 1.5f + 0.06f) + Hx(ng * 1.5f + 0.06f) + Hx(nb * 1.5f + 0.06f)
                        : HexColor(r.Id);
                    body.Append(Rect(px, py, BoxW, BoxH, fill, cur ? CurBorder : Border, cur ? 2 : 1));
                    body.Append(Text((int)px + 6, (int)py + 16, Esc(Trunc(r.Name, 16) + " (#" + r.Id + ")"), TextCol, 12, true));
                    string marks = InOutMarkers(r);
                    if (marks.Length > 0) body.Append(Text((int)px + 6, (int)py + 32, Esc(marks), Border, 10, false));
                    int di = 0;
                    foreach (var o in map.Objects.Values)
                    {
                        if (o.Room != r.Id || o.Carried) continue;
                        body.Append(Circle(px + 8 + di * 10, py + BoxH - 8, 3, ItemCol, Esc(o.Name)));
                        if (++di > 10) break;
                    }
                }
            }

            // ---- Legend: all found objects, alphabetical, multi-column ----
            var objs = new List<MapObject>(map.Objects.Values);
            objs.Sort((a, b) =>
            {
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.Id - b.Id;
            });
            body.Append(Text(Margin, (int)y + 16, Esc("Objects (" + objs.Count + " found)"), TextCol, 14, true));
            y += HeadingH;
            int cols = Math.Max(1, (int)((contentW - Margin) / LegendColW));
            int rows = objs.Count > 0 ? (objs.Count + cols - 1) / cols : 0;
            for (int i = 0; i < objs.Count; i++)
            {
                int col = i / rows, row = i % rows;   // column-major: reads DOWN each column
                float ex = Margin + col * LegendColW, ey = y + row * LegendRowH + 12;
                var o = objs[i];
                string loc = o.Carried ? "carried"
                    : (map.Rooms.TryGetValue(o.Room, out var rr) ? rr.Name + " (#" + o.Room + ")" : "?");
                body.Append(Text((int)ex, (int)ey, Esc(o.Name + "  —  " + loc), TextCol, 12, false));
            }
            y += Math.Max(1, rows) * LegendRowH + 8;
            contentW = Math.Max(contentW, Margin + cols * LegendColW);

            float svgW = contentW + Margin, svgH = y + Margin;
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(svgW))
              .Append("\" height=\"").Append(F(svgH)).Append("\" viewBox=\"0 0 ").Append(F(svgW))
              .Append(' ').Append(F(svgH)).Append("\" font-family=\"Consolas, monospace\">\n");
            sb.Append(Defs());
            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(F(svgW)).Append("\" height=\"").Append(F(svgH))
              .Append("\" fill=\"").Append(PageBg).Append("\"/>\n");
            sb.Append(body);
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        struct VEdge { public (float x, float y) pUpper, pLower; public bool arrowUpper, arrowLower; public int fromRoom; }

        static string PageHeading(ZMap map, int area, int level)
        {
            if (area != 0 && map.AreaName.TryGetValue(area, out var n) && !string.IsNullOrEmpty(n))
                return n + (level == 0 ? "" : " · Level " + level);
            return level == 0 ? "Ground level" : "Level " + level;
        }

        // Exits whose two rooms sit on DIFFERENT pages (a level change or an
        // area change via in/out) → dashed cross-page connectors, resolved by
        // absolute position (upper = smaller y on the stacked page layout).
        static List<VEdge> CrossEdges(ZMap map, Dictionary<int, (float x, float y)> pos)
        {
            var up = new Dictionary<long, bool>();   // exit from upper → lower exists
            var dn = new Dictionary<long, bool>();   // exit from lower → upper exists
            var ends = new Dictionary<long, (int upper, int lower)>();
            foreach (var r in map.Rooms.Values)
            {
                if (!r.Placed) continue;
                foreach (var kv in r.Exits)
                {
                    if (!pos.ContainsKey(r.Id) || !pos.ContainsKey(kv.Value)) continue;
                    if (kv.Value == r.Id) continue;
                    if (!map.Rooms.TryGetValue(kv.Value, out var dest)) continue;
                    if (dest.Area == r.Area && dest.Level == r.Level) continue; // same page → handled as an edge
                    int a = Math.Min(r.Id, kv.Value), b = Math.Max(r.Id, kv.Value);
                    long key = ((long)a << 32) | (uint)b;
                    int upper = pos[a].y <= pos[b].y ? a : b;
                    int lower = upper == a ? b : a;
                    ends[key] = (upper, lower);
                    if (r.Id == upper) up[key] = true; else dn[key] = true;
                }
            }
            var list = new List<VEdge>();
            foreach (var kvp in ends)
            {
                var (upper, lower) = kvp.Value;
                bool upToLow = up.ContainsKey(kvp.Key);
                bool lowToUp = dn.ContainsKey(kvp.Key);
                list.Add(new VEdge
                {
                    pUpper = Attach(pos, upper, Dir.S),
                    pLower = Attach(pos, lower, Dir.N),
                    arrowLower = upToLow, // traveling upper→lower arrives at lower
                    arrowUpper = lowToUp,
                    // FROM = the non-arrow end (arrows point into the destination).
                    fromRoom = (upToLow && !lowToUp) ? upper : (lowToUp && !upToLow) ? lower : upper
                });
            }
            return list;
        }

        static (float x, float y) Attach(Dictionary<int, (float x, float y)> pos, int id, Dir side)
        {
            var (px, py) = pos[id];
            ZMapLayout.Edge(side, out float fx, out float fy);
            return (px + fx * BoxW, py + fy * BoxH);
        }

        // ---- SVG primitives ----

        static string Defs()
        {
            // fill="context-stroke": the arrowhead inherits its path's stroke
            // colour, so heads match the FROM-node-coloured lines automatically.
            string M(string id, string orient) =>
                "<marker id=\"" + id + "\" markerWidth=\"9\" markerHeight=\"9\" refX=\"6\" refY=\"3\" " +
                "orient=\"" + orient + "\" markerUnits=\"userSpaceOnUse\"><path d=\"M0,0 L6,3 L0,6 Z\" " +
                "fill=\"context-stroke\"/></marker>\n";
            return "<defs>\n" + M("aEnd", "auto") + M("aStart", "auto-start-reverse") + "</defs>\n";
        }

        static string Spline((float x, float y) p0, (float x, float y) p1,
            float c1x, float c1y, float c2x, float c2y, bool arrowA, bool arrowB, string col, bool cross)
        {
            var sb = new StringBuilder();
            sb.Append("<path d=\"M").Append(F(p0.x)).Append(',').Append(F(p0.y))
              .Append(" C").Append(F(c1x)).Append(',').Append(F(c1y)).Append(' ')
              .Append(F(c2x)).Append(',').Append(F(c2y)).Append(' ')
              .Append(F(p1.x)).Append(',').Append(F(p1.y)).Append("\" fill=\"none\" stroke=\"")
              .Append(col).Append("\" stroke-width=\"1.5\"");
            if (cross) sb.Append(" stroke-dasharray=\"5,4\"");
            if (arrowA) sb.Append(" marker-start=\"url(#aStart)\"");
            if (arrowB) sb.Append(" marker-end=\"url(#aEnd)\"");
            sb.Append("/>\n");
            return sb.ToString();
        }

        static string HexColor(int id)
        {
            ZMapLayout.NodeColor(id, out float r, out float g, out float b);
            int R = (int)(r * 255 + 0.5f), G = (int)(g * 255 + 0.5f), B = (int)(b * 255 + 0.5f);
            return "#" + R.ToString("x2") + G.ToString("x2") + B.ToString("x2");
        }

        static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        static string Esc(string s) => string.IsNullOrEmpty(s) ? ""
            : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        static string Trunc(string s, int n)
        {
            s = s ?? "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        static string Rect(float x, float y, float w, float h, string fill, string stroke, int strokeW) =>
            "<rect x=\"" + F(x) + "\" y=\"" + F(y) + "\" width=\"" + F(w) + "\" height=\"" + F(h) +
            "\" rx=\"3\" fill=\"" + fill + "\" stroke=\"" + stroke + "\" stroke-width=\"" + strokeW + "\"/>\n";

        static string Circle(float cx, float cy, float r, string fill, string title) =>
            "<circle cx=\"" + F(cx) + "\" cy=\"" + F(cy) + "\" r=\"" + F(r) + "\" fill=\"" + fill +
            "\"><title>" + title + "</title></circle>\n";

        static string Text(int x, int y, string s, string col, int size, bool bold) =>
            "<text x=\"" + x + "\" y=\"" + y + "\" fill=\"" + col + "\" font-size=\"" + size + "\"" +
            (bold ? " font-weight=\"bold\"" : "") + ">" + s + "</text>\n";

        static string InOutMarkers(MapRoom r)
        {
            var sb = new StringBuilder();
            foreach (var kv in r.Exits)
            {
                if (kv.Key == Dir.In) sb.Append("▸in ");
                else if (kv.Key == Dir.Out) sb.Append("◂out ");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
#endif
