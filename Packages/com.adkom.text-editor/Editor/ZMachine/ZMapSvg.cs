#if UNITY_EDITOR
// ATE Z-Machine — export the auto-map to a standalone SVG. All levels are
// stacked top-to-bottom (room boxes, compass connection lines, up/down/in/out
// and warp markers, item dots); a LEGEND of every discovered object is drawn at
// the BOTTOM — alphabetical, laid out in columns, with each item's name and
// current location. Pure rendering of the ZMap model; nothing drives the game.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AteZMachine
{
    internal static class ZMapSvg
    {
        const int CellW = 150, CellH = 92, BoxW = 128, BoxH = 74;
        const int Margin = 20, HeadingH = 26, SectionGap = 24;
        const int LegendColW = 260, LegendRowH = 18;

        // Dark theme, matching the map pane.
        const string PageBg = "#1e1e20";
        const string RoomBg = "#2e2e33";
        const string RoomCur = "#335738";
        const string Border = "#808088";
        const string LineCol = "#8c8c99";
        const string ItemCol = "#8cccff";
        const string TextCol = "#d9d9d9";
        const string WarpCol = "#d9b366";

        public static string ToSvg(ZMap map)
        {
            var body = new StringBuilder();
            float contentW = 200f, y = Margin;

            var levels = new SortedSet<int>();
            foreach (var r in map.Rooms.Values) if (r.Placed) levels.Add(r.Level);

            foreach (int level in levels)
            {
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var r in map.Rooms.Values)
                    if (r.Placed && r.Level == level)
                    {
                        if (r.X < minX) minX = r.X; if (r.Y < minY) minY = r.Y;
                        if (r.X > maxX) maxX = r.X; if (r.Y > maxY) maxY = r.Y;
                    }
                if (minX == int.MaxValue) continue;

                body.Append(Text(Margin, (int)y + 16, Esc(level == 0 ? "Ground level" : "Level " + level), TextCol, 14, true));
                y += HeadingH;

                float ox = Margin, oy = y;
                var center = new Dictionary<int, (float, float)>();
                foreach (var r in map.Rooms.Values)
                {
                    if (!r.Placed || r.Level != level) continue;
                    float px = ox + (r.X - minX) * CellW, py = oy + (r.Y - minY) * CellH;
                    center[r.Id] = (px + BoxW / 2f, py + BoxH / 2f);
                }
                // Connection lines first (under the boxes).
                foreach (var r in map.Rooms.Values)
                {
                    if (!r.Placed || r.Level != level || !center.TryGetValue(r.Id, out var a)) continue;
                    foreach (var kv in r.Exits)
                    {
                        if (kv.Key == Dir.U || kv.Key == Dir.D || kv.Key == Dir.In || kv.Key == Dir.Out) continue;
                        if (center.TryGetValue(kv.Value, out var b)) body.Append(Line(a.Item1, a.Item2, b.Item1, b.Item2));
                    }
                }
                // Room boxes.
                foreach (var r in map.Rooms.Values)
                {
                    if (!r.Placed || r.Level != level) continue;
                    float px = ox + (r.X - minX) * CellW, py = oy + (r.Y - minY) * CellH;
                    bool cur = r.Id == map.CurrentRoomId;
                    body.Append(Rect(px, py, BoxW, BoxH, cur ? RoomCur : RoomBg));
                    body.Append(Text((int)px + 6, (int)py + 16, Esc(Trunc(r.Name, 16) + " (#" + r.Id + ")"), TextCol, 12, true));

                    string marks = Markers(r);
                    if (marks.Length > 0) body.Append(Text((int)px + 6, (int)py + 32, Esc(marks), Border, 10, false));
                    string warps = Warps(map, r);
                    if (warps.Length > 0) body.Append(Text((int)px + 6, (int)py + 46, Esc(warps), WarpCol, 10, false));

                    int di = 0;
                    foreach (var o in map.Objects.Values)
                    {
                        if (o.Room != r.Id || o.Carried) continue;
                        body.Append(Circle(px + 8 + di * 10, py + BoxH - 8, 3, ItemCol, Esc(o.Name)));
                        if (++di > 10) break;
                    }
                }

                contentW = Math.Max(contentW, Margin + (maxX - minX + 1) * CellW);
                y += (maxY - minY + 1) * CellH + SectionGap;
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
                string loc = o.Carried ? "carried" : RoomName(map, o.Room);
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
            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(F(svgW)).Append("\" height=\"").Append(F(svgH))
              .Append("\" fill=\"").Append(PageBg).Append("\"/>\n");
            sb.Append(body);
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        // ---- SVG primitives ----

        static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        static string Esc(string s) => string.IsNullOrEmpty(s) ? ""
            : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        static string Trunc(string s, int n)
        {
            s = s ?? "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        static string Rect(float x, float y, float w, float h, string fill) =>
            "<rect x=\"" + F(x) + "\" y=\"" + F(y) + "\" width=\"" + F(w) + "\" height=\"" + F(h) +
            "\" rx=\"3\" fill=\"" + fill + "\" stroke=\"" + Border + "\" stroke-width=\"1\"/>\n";

        static string Line(float x1, float y1, float x2, float y2) =>
            "<line x1=\"" + F(x1) + "\" y1=\"" + F(y1) + "\" x2=\"" + F(x2) + "\" y2=\"" + F(y2) +
            "\" stroke=\"" + LineCol + "\" stroke-width=\"1.5\"/>\n";

        static string Circle(float cx, float cy, float r, string fill, string title) =>
            "<circle cx=\"" + F(cx) + "\" cy=\"" + F(cy) + "\" r=\"" + F(r) + "\" fill=\"" + fill +
            "\"><title>" + title + "</title></circle>\n";

        static string Text(int x, int y, string s, string col, int size, bool bold) =>
            "<text x=\"" + x + "\" y=\"" + y + "\" fill=\"" + col + "\" font-size=\"" + size + "\"" +
            (bold ? " font-weight=\"bold\"" : "") + ">" + s + "</text>\n";

        static string RoomName(ZMap m, int id) =>
            m.Rooms.TryGetValue(id, out var r) ? r.Name : "?";

        static string Markers(MapRoom r)
        {
            var sb = new StringBuilder();
            foreach (var kv in r.Exits)
            {
                if (kv.Key == Dir.U) sb.Append("↑");
                else if (kv.Key == Dir.D) sb.Append("↓");
                else if (kv.Key == Dir.In) sb.Append("▸in");
                else if (kv.Key == Dir.Out) sb.Append("◂out");
            }
            return sb.ToString();
        }

        static string Warps(ZMap m, MapRoom r)
        {
            var sb = new StringBuilder();
            foreach (var kv in r.Exits)
                if (IsCompass(kv.Key) && !IsGeometric(m, r, kv.Key, kv.Value))
                    sb.Append(ZMap.DirName(kv.Key)).Append("⇢ ");
            return sb.ToString().TrimEnd();
        }

        static bool IsCompass(Dir d) => d <= Dir.SW;

        static bool IsGeometric(ZMap m, MapRoom r, Dir d, int destId)
        {
            if (!m.Rooms.TryGetValue(destId, out var dest) || !dest.Placed) return false;
            int dx = 0, dy = 0;
            switch (d)
            {
                case Dir.N: dy = -1; break;
                case Dir.S: dy = 1; break;
                case Dir.E: dx = 1; break;
                case Dir.W: dx = -1; break;
                case Dir.NE: dx = 1; dy = -1; break;
                case Dir.NW: dx = -1; dy = -1; break;
                case Dir.SE: dx = 1; dy = 1; break;
                case Dir.SW: dx = -1; dy = 1; break;
                default: return false;
            }
            return dest.Level == r.Level && dest.X == r.X + dx && dest.Y == r.Y + dy;
        }
    }
}
#endif
