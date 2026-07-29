#if UNITY_EDITOR
// ATE Z-Machine — the interactive map pane. A bidirectionally-scrollable
// canvas of room boxes (compass connections drawn as lines; up/down/in/out
// shown as markers), with item symbols inside each room and a side info
// panel. Clicking a room or an item shows its details.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AteZMachine
{
    public sealed class ZMapView : VisualElement
    {
        const int CellW = 150, CellH = 92, BoxW = 128, BoxH = 74;
        const int Pad = 40; // canvas margin so edge boxes/splines aren't clipped

        ZMap _map;
        int _level;
        VisualElement _curBox;

        readonly ScrollView _scroll;
        readonly VisualElement _canvas;
        readonly VisualElement _info;
        readonly Label _levelLabel;

        // One drawn connection: a spline p0→p1 (control points c1,c2) with an
        // arrowhead at whichever end travel arrives.
        struct Conn { public Vector2 p0, c1, c2, p1; public bool arrowStart, arrowEnd; }
        readonly List<Conn> _lines = new List<Conn>();

        static readonly Color RoomBg = new Color(0.18f, 0.18f, 0.20f);
        static readonly Color RoomCur = new Color(0.20f, 0.34f, 0.22f);
        static readonly Color Border = new Color(0.5f, 0.5f, 0.55f);
        static readonly Color LineCol = new Color(0.55f, 0.55f, 0.6f);
        static readonly Color ItemCol = new Color(0.55f, 0.8f, 1f);

        public ZMapView()
        {
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1;

            var left = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, height = 20 } };
            var down = new Button(() => { _level--; Rebuild(); }) { text = "▾" };
            var up = new Button(() => { _level++; Rebuild(); }) { text = "▴" };
            _levelLabel = new Label("Level 0") { style = { marginLeft = 4, marginRight = 4 } };
            var svg = new Button(ExportSvg) { text = L10n.Tr("SVG"), tooltip = L10n.Tr("Export map as SVG"),
                style = { marginLeft = 12 } };
            bar.Add(new Label(L10n.Tr("Map")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 4, marginRight = 8 } });
            bar.Add(down); bar.Add(_levelLabel); bar.Add(up); bar.Add(svg);
            left.Add(bar);

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1 } };
            // Show scrollbars whenever the map is bigger than the viewport so the
            // user can scroll around it.
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            // The canvas keeps its explicit size (set per Rebuild); without this
            // the scroll content container shrinks it to the viewport and the
            // content never overflows, so no scrollbars appear.
            _canvas = new VisualElement { style = { position = Position.Relative, flexShrink = 0 } };
            _canvas.generateVisualContent += OnDrawLines;
            _scroll.Add(_canvas);
            left.Add(_scroll);
            Add(left);

            _info = new VisualElement
            {
                style = { width = 240, flexShrink = 0, paddingLeft = 8, paddingRight = 8, paddingTop = 6,
                          borderLeftWidth = 1, borderLeftColor = Border }
            };
            Add(_info);
            ShowHint();
        }

        public void SetMap(ZMap map)
        {
            if (_map != null) _map.Changed -= OnChanged;
            _map = map;
            if (_map != null) _map.Changed += OnChanged;
            _level = _map != null && _map.Rooms.TryGetValue(_map.CurrentRoomId, out var r) ? r.Level : 0;
            Rebuild();
        }

        void OnChanged()
        {
            // Follow the player to their current level.
            if (_map != null && _map.Rooms.TryGetValue(_map.CurrentRoomId, out var r)) _level = r.Level;
            Rebuild();
        }

        void ExportSvg()
        {
            if (_map == null || _map.Rooms.Count == 0) return;
            string path = EditorUtility.SaveFilePanel(L10n.Tr("Export map as SVG"), "", "map.svg", "svg");
            if (string.IsNullOrEmpty(path)) return;
            try { System.IO.File.WriteAllText(path, ZMapSvg.ToSvg(_map)); }
            catch (System.Exception e) { Debug.LogWarning("[ADKOM Text Editor] SVG export failed: " + e.Message); }
        }

        void Rebuild()
        {
            _canvas.Clear();
            _lines.Clear();
            _curBox = null;
            if (_map == null || _map.Rooms.Count == 0) { _levelLabel.text = "Level " + _level; _canvas.MarkDirtyRepaint(); return; }
            _levelLabel.text = "Level " + _level;

            // Bounds for this level.
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var r in _map.Rooms.Values)
                if (r.Placed && r.Level == _level)
                { minX = Mathf.Min(minX, r.X); minY = Mathf.Min(minY, r.Y); maxX = Mathf.Max(maxX, r.X); maxY = Mathf.Max(maxY, r.Y); }
            if (minX == int.MaxValue) { _canvas.style.width = 200; _canvas.style.height = 60; _canvas.MarkDirtyRepaint(); return; }

            _canvas.style.width = (maxX - minX + 1) * CellW + 2 * Pad;
            _canvas.style.height = (maxY - minY + 1) * CellH + 2 * Pad;

            var pos = new Dictionary<int, Vector2>();
            foreach (var r in _map.Rooms.Values)
            {
                if (!r.Placed || r.Level != _level) continue;
                float px = (r.X - minX) * CellW + Pad;
                float py = (r.Y - minY) * CellH + Pad;
                pos[r.Id] = new Vector2(px, py);
                var box = BuildRoomBox(r, px, py);
                if (r.Id == _map.CurrentRoomId) _curBox = box;
                _canvas.Add(box);
            }
            // Connections as directed splines that attach at the exit's side/
            // corner, with arrowheads showing travel direction (both ends when
            // bidirectional). Resolves non-Euclidean links (e.g. a SOUTHWEST
            // exit back to a room due south) into a visible curve.
            foreach (var e in ZMapLayout.EdgesForLevel(_map, _level))
            {
                if (!pos.TryGetValue(e.E0.Room, out var b0) || !pos.TryGetValue(e.E1.Room, out var b1)) continue;
                Vector2 p0 = Attach(b0, e.E0.Side), p1 = Attach(b1, e.E1.Side);
                ZMapLayout.Controls(p0.x, p0.y, e.E0.Side, p1.x, p1.y, e.E1.Side,
                    out float c1x, out float c1y, out float c2x, out float c2y);
                _lines.Add(new Conn
                {
                    p0 = p0, p1 = p1,
                    c1 = new Vector2(c1x, c1y), c2 = new Vector2(c2x, c2y),
                    arrowStart = e.Arrow0, arrowEnd = e.Arrow1
                });
            }
            _canvas.MarkDirtyRepaint();

            // Keep the current room in view as the player moves. ScrollTo needs
            // the box's resolved layout, so defer a frame until it has one.
            if (_curBox != null)
                _curBox.RegisterCallback<GeometryChangedEvent>(OnCurBoxLaidOut);
        }

        void OnCurBoxLaidOut(GeometryChangedEvent e)
        {
            var box = e.target as VisualElement;
            if (box == null) return;
            box.UnregisterCallback<GeometryChangedEvent>(OnCurBoxLaidOut);
            if (box == _curBox) CenterOn(box);
        }

        // Scroll so the current room sits in the centre of the map viewport
        // (the scroll area, not counting the info panel). scrollOffset clamps
        // itself to the scrollable range at the edges of the map.
        void CenterOn(VisualElement box)
        {
            var vp = _scroll.contentViewport.layout;
            if (float.IsNaN(vp.width) || vp.width < 1 || float.IsNaN(box.layout.x)) return;
            float cx = box.layout.x + box.layout.width / 2f;
            float cy = box.layout.y + box.layout.height / 2f;
            _scroll.scrollOffset = new Vector2(
                Mathf.Max(0f, cx - vp.width / 2f),
                Mathf.Max(0f, cy - vp.height / 2f));
        }

        VisualElement BuildRoomBox(MapRoom r, float px, float py)
        {
            bool cur = r.Id == _map.CurrentRoomId;
            var box = new VisualElement
            {
                style =
                {
                    position = Position.Absolute, left = px, top = py, width = BoxW, height = BoxH,
                    backgroundColor = cur ? RoomCur : RoomBg,
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = Border, borderBottomColor = Border, borderLeftColor = Border, borderRightColor = Border,
                    paddingLeft = 4, paddingRight = 4, paddingTop = 2, overflow = Overflow.Hidden
                }
            };
            var title = new Label(r.Name + " (#" + r.Id + ")") { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.NoWrap } };
            box.Add(title);

            // Vertical / in-out markers.
            string marks = "";
            foreach (var kv in r.Exits)
            {
                if (kv.Key == Dir.U) marks += "↑";
                else if (kv.Key == Dir.D) marks += "↓";
                else if (kv.Key == Dir.In) marks += "▸in";
                else if (kv.Key == Dir.Out) marks += "◂out";
            }
            if (marks.Length > 0) box.Add(new Label(marks) { style = { fontSize = 10, color = Border } });

            // (Non-Euclidean compass exits are now drawn as curved connections
            // with arrowheads, so they need no in-box marker.)

            // Item symbols (clickable).
            var items = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            foreach (var o in _map.Objects.Values)
            {
                if (o.Room != r.Id || o.Carried) continue;
                var sym = new Label("◦") { tooltip = o.Name, style = { color = ItemCol, fontSize = 13, marginRight = 2 } };
                int id = o.Id;
                sym.RegisterCallback<MouseDownEvent>(e => { ShowObjectInfo(id); e.StopPropagation(); });
                items.Add(sym);
            }
            box.Add(items);

            int rid = r.Id;
            box.RegisterCallback<MouseDownEvent>(e => { ShowRoomInfo(rid); e.StopPropagation(); });
            return box;
        }

        Vector2 Attach(Vector2 boxTopLeft, Dir side)
        {
            ZMapLayout.Edge(side, out float fx, out float fy);
            return new Vector2(boxTopLeft.x + fx * BoxW, boxTopLeft.y + fy * BoxH);
        }

        void OnDrawLines(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineWidth = 1.5f;
            foreach (var c in _lines)
            {
                p.strokeColor = LineCol;
                p.BeginPath();
                p.MoveTo(c.p0);
                p.BezierCurveTo(c.c1, c.c2, c.p1);
                p.Stroke();
                if (c.arrowEnd) Arrowhead(p, c.p1, c.p1 - c.c2);
                if (c.arrowStart) Arrowhead(p, c.p0, c.p0 - c.c1);
            }
        }

        // A small filled triangle at 'tip', pointing along 'dir'.
        static void Arrowhead(Painter2D p, Vector2 tip, Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.001f) return;
            dir = dir.normalized;
            var perp = new Vector2(-dir.y, dir.x);
            const float len = 9f, half = 4.5f;
            Vector2 baseC = tip - dir * len;
            p.fillColor = LineCol;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(baseC + perp * half);
            p.LineTo(baseC - perp * half);
            p.ClosePath();
            p.Fill();
        }

        // ---- Info panel ----

        void ShowHint()
        {
            _info.Clear();
            _info.Add(new Label(L10n.Tr("Auto-map")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });
            _info.Add(Wrapped(L10n.Tr("Explore to build the map. Click a room or an item (◦) for details.")));
        }

        void ShowRoomInfo(int roomId)
        {
            if (_map == null || !_map.Rooms.TryGetValue(roomId, out var r)) return;
            _info.Clear();
            _info.Add(new Label(r.Name) { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });
            _info.Add(new Label(L10n.Tr("Room #") + r.Id + "   (" + r.X + "," + r.Y + " L" + r.Level + ")")
                { style = { color = Border, fontSize = 11, marginBottom = 4 } });
            if (r.Id == _map.CurrentRoomId) _info.Add(new Label(L10n.Tr("(you are here)")) { style = { color = new Color(0.5f, 0.85f, 0.5f) } });

            _info.Add(Header(L10n.Tr("Exits")));
            if (r.Exits.Count == 0) _info.Add(Wrapped(L10n.Tr("none seen")));
            foreach (var kv in r.Exits)
            {
                string dest = _map.Rooms.TryGetValue(kv.Value, out var dr) ? dr.Name : "?";
                _info.Add(Wrapped(ZMap.DirName(kv.Key) + " → " + dest + " (#" + kv.Value + ")"));
            }

            _info.Add(Header(L10n.Tr("Items here")));
            bool any = false;
            foreach (var o in _map.Objects.Values)
                if (o.Room == r.Id && !o.Carried) { AddObjectLink(o); any = true; }
            if (!any) _info.Add(Wrapped(L10n.Tr("none")));
        }

        void ShowObjectInfo(int objId)
        {
            if (_map == null || !_map.Objects.TryGetValue(objId, out var o)) return;
            _info.Clear();
            _info.Add(new Label(o.Name) { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });
            _info.Add(new Label(L10n.Tr("Object #") + o.Id) { style = { color = Border, fontSize = 11, marginBottom = 4 } });
            _info.Add(Wrapped(o.Carried ? L10n.Tr("Carried by you")
                : L10n.Tr("Location: ") + RoomName(o.Room)));
            if (o.Container != 0 && _map.Objects.TryGetValue(o.Container, out var c))
                _info.Add(Wrapped(L10n.Tr("Inside: ") + c.Name));
            _info.Add(Wrapped(L10n.Tr("First seen: ") + RoomName(o.OriginRoom)));
            if (o.Room != o.OriginRoom || o.Carried)
                _info.Add(new Label(L10n.Tr("(moved since first seen)")) { style = { color = new Color(0.85f, 0.7f, 0.4f), whiteSpace = WhiteSpace.Normal } });
        }

        void AddObjectLink(MapObject o)
        {
            var b = new Label("◦ " + o.Name) { style = { color = ItemCol, whiteSpace = WhiteSpace.Normal } };
            int id = o.Id;
            b.RegisterCallback<MouseDownEvent>(e => { ShowObjectInfo(id); e.StopPropagation(); });
            _info.Add(b);
        }

        string RoomName(int id) => _map != null && _map.Rooms.TryGetValue(id, out var r) ? r.Name : "?";

        static Label Header(string t) =>
            new Label(t) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } };
        static Label Wrapped(string t) =>
            new Label(t) { style = { whiteSpace = WhiteSpace.Normal } };
    }
}
#endif
