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
        // Fixed canvas margin: spline headroom + scroll slack so the current
        // room can be centred even for a small map. Constant (not viewport-
        // derived) so canvas size can't feed back into viewport measurement.
        const int CenterPad = 500;

        ZMap _map;
        int _level;
        int _area;

        // Geometry is stored with its min corner at (0,0); the viewport-aware
        // Relayout() shifts it by _drawOffset (padding) and centres the current
        // room. Splines are drawn with _drawOffset applied.
        Vector2 _drawOffset;
        float _geomW, _geomH;
        Vector2 _curCenter;   // current room centre, relative to geometry min
        bool _hasGeom;
        readonly List<(VisualElement box, Vector2 basePos)> _boxItems = new List<(VisualElement, Vector2)>();

        readonly ScrollView _scroll;
        readonly VisualElement _canvas;
        readonly VisualElement _info;
        readonly Label _levelLabel;

        // Zoom (dynamic scaling): slider + ctrl-wheel, kept in sync. The canvas
        // is scaled by a transform and its layout size set to base*zoom so the
        // scrollbars track the scaled map.
        const float ZoomMin = 0.4f, ZoomMax = 2.5f;
        float _zoom = 1f, _appliedZoom = 1f, _canvasBaseW, _canvasBaseH;
        Slider _zoomSlider;
        Label _zoomLabel;

        // One drawn connection: a spline p0→p1 (control points c1,c2) with an
        // arrowhead at whichever end travel arrives, stroked in the FROM room's
        // colour.
        struct Conn { public Vector2 p0, c1, c2, p1; public bool arrowStart, arrowEnd; public Color col; }
        readonly List<Conn> _lines = new List<Conn>();

        static readonly Color Border = new Color(0.5f, 0.5f, 0.55f);
        static readonly Color CurBorder = new Color(0.55f, 0.95f, 0.6f); // "you are here" outline
        static readonly Color ItemCol = new Color(0.55f, 0.8f, 1f);

        static Color NodeColor(int id)
        {
            ZMapLayout.NodeColor(id, out float r, out float g, out float b);
            return new Color(r, g, b);
        }

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

            // Zoom control: slider + a percentage label (ctrl-wheel drives the
            // same slider so they stay in sync).
            bar.Add(new Label(L10n.Tr("Zoom")) { tooltip = L10n.Tr("Ctrl+scroll to zoom"),
                style = { marginLeft = 12, marginRight = 2 } });
            _zoomSlider = new Slider(ZoomMin, ZoomMax) { value = 1f, style = { width = 90 } };
            _zoomSlider.RegisterValueChangedCallback(e => { _zoom = e.newValue; if (_zoomLabel != null) _zoomLabel.text = ZoomPct(); ApplyZoom(false); });
            bar.Add(_zoomSlider);
            _zoomLabel = new Label("100%") { style = { width = 42, marginLeft = 4 } };
            bar.Add(_zoomLabel);
            left.Add(bar);

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1 } };
            // Ctrl+wheel zooms (routes through the slider so value + slider stay
            // in sync); plain wheel scrolls as usual. Trickle down so we can
            // consume it before the ScrollView scrolls.
            _scroll.RegisterCallback<WheelEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey)) return;
                float z = Mathf.Clamp(_zoom * (e.delta.y < 0 ? 1.1f : 1f / 1.1f), ZoomMin, ZoomMax);
                _zoomSlider.value = z; // fires the value-changed callback → ApplyZoom
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
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
            FollowPlayer();
            Rebuild();
        }

        void OnChanged()
        {
            FollowPlayer();
            Rebuild();
        }

        // Follow the player to their current area and level.
        void FollowPlayer()
        {
            if (_map != null && _map.Rooms.TryGetValue(_map.CurrentRoomId, out var r))
            { _area = r.Area; _level = r.Level; }
        }

        // The page heading: interiors are named after their entry room.
        string PageLabel()
        {
            if (_map != null && _area != 0 && _map.AreaName.TryGetValue(_area, out var n) && !string.IsNullOrEmpty(n))
                return n + " · L" + _level;
            return "Level " + _level;
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
            _boxItems.Clear();
            _hasGeom = false;
            if (_map == null || _map.Rooms.Count == 0) { _levelLabel.text = PageLabel(); _canvas.MarkDirtyRepaint(); return; }
            _levelLabel.text = PageLabel();

            // Bounds for this page (current area + level).
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var r in _map.Rooms.Values)
                if (r.Placed && r.Area == _area && r.Level == _level)
                { minX = Mathf.Min(minX, r.X); minY = Mathf.Min(minY, r.Y); maxX = Mathf.Max(maxX, r.X); maxY = Mathf.Max(maxY, r.Y); }
            if (minX == int.MaxValue) { _canvas.style.width = 200; _canvas.style.height = 60; _canvas.MarkDirtyRepaint(); return; }

            // Base (un-offset) box positions on the grid.
            var basePos = new Dictionary<int, Vector2>();
            foreach (var r in _map.Rooms.Values)
                if (r.Placed && r.Area == _area && r.Level == _level)
                    basePos[r.Id] = new Vector2((r.X - minX) * CellW, (r.Y - minY) * CellH);

            // Obstacle rectangles (all boxes on the page) for spline routing.
            var obstacles = new List<ZMapLayout.BoxRect>(basePos.Count);
            foreach (var kv in basePos)
                obstacles.Add(new ZMapLayout.BoxRect { Id = kv.Key, X = kv.Value.x, Y = kv.Value.y, W = BoxW, H = BoxH });

            // Connections (splines). Attach at the exit's side/corner, routed to
            // avoid the boxes, stroked in the FROM room's colour, arrowheads for
            // travel direction (both ends when bidirectional).
            foreach (var e in ZMapLayout.EdgesForPage(_map, _area, _level))
            {
                if (!basePos.TryGetValue(e.E0.Room, out var b0) || !basePos.TryGetValue(e.E1.Room, out var b1)) continue;
                Vector2 p0 = Attach(b0, e.E0.Side), p1 = Attach(b1, e.E1.Side);
                ZMapLayout.RouteControls(p0.x, p0.y, e.E0.Side, p1.x, p1.y, e.E1.Side,
                    obstacles, e.E0.Room, e.E1.Room,
                    out float c1x, out float c1y, out float c2x, out float c2y);
                _lines.Add(new Conn
                {
                    p0 = p0, p1 = p1,
                    c1 = new Vector2(c1x, c1y), c2 = new Vector2(c2x, c2y),
                    arrowStart = e.Arrow0, arrowEnd = e.Arrow1,
                    col = NodeColor(ZMapLayout.FromRoom(e))
                });
            }

            // Bounds over ALL geometry — boxes and spline control points (a
            // cubic stays within its control hull) — so the canvas includes the
            // splines and the scrollbars cover them instead of clipping.
            float loX = 0, loY = 0, hiX = 0, hiY = 0; bool any = false;
            void Grow(float x, float y)
            {
                if (!any) { loX = hiX = x; loY = hiY = y; any = true; return; }
                if (x < loX) loX = x; if (x > hiX) hiX = x;
                if (y < loY) loY = y; if (y > hiY) hiY = y;
            }
            foreach (var b in basePos.Values) { Grow(b.x, b.y); Grow(b.x + BoxW, b.y + BoxH); }
            foreach (var c in _lines) { Grow(c.p0.x, c.p0.y); Grow(c.p1.x, c.p1.y); Grow(c.c1.x, c.c1.y); Grow(c.c2.x, c.c2.y); }

            // Normalise so the geometry's min corner is at (0,0); Relayout()
            // applies the viewport-dependent offset and centring.
            var norm = new Vector2(-loX, -loY);
            for (int i = 0; i < _lines.Count; i++)
            {
                var c = _lines[i];
                c.p0 += norm; c.p1 += norm; c.c1 += norm; c.c2 += norm;
                _lines[i] = c;
            }
            _geomW = hiX - loX;
            _geomH = hiY - loY;
            foreach (var r in _map.Rooms.Values)
            {
                if (!r.Placed || r.Area != _area || r.Level != _level) continue;
                Vector2 p = basePos[r.Id] + norm;
                var box = BuildRoomBox(r, 0, 0);   // position set in Relayout
                _boxItems.Add((box, p));
                _canvas.Add(box);
                if (r.Id == _map.CurrentRoomId) _curCenter = p + new Vector2(BoxW / 2f, BoxH / 2f);
            }
            _hasGeom = _boxItems.Count > 0;
            Relayout();
        }

        // Positions the geometry with a FIXED margin (independent of the
        // viewport, so canvas size can never feed back into the viewport
        // measurement and explode) and scrolls so the current room sits in the
        // centre of the map viewport. The fixed margin doubles as spline
        // headroom and as scroll slack so even a small/edge map can be centred.
        void Relayout()
        {
            if (!_hasGeom) return;
            _drawOffset = new Vector2(CenterPad, CenterPad);
            _canvasBaseW = _geomW + 2 * CenterPad;
            _canvasBaseH = _geomH + 2 * CenterPad;
            foreach (var (box, basePos) in _boxItems)
            {
                box.style.left = basePos.x + CenterPad;
                box.style.top = basePos.y + CenterPad;
            }
            _canvas.MarkDirtyRepaint();
            ApplyZoom(centerOnRoom: true); // sizes the (scaled) canvas + centres
        }

        string ZoomPct() => Mathf.RoundToInt(_zoom * 100f) + "%";

        // Applies the zoom: scales the canvas by a transform (origin top-left) and
        // sets its layout size to base*zoom so the scrollbars track the scaled
        // map. Reads the viewport ONLY to compute the scroll target (never to
        // size the canvas → no feedback loop). centerOnRoom centres the current
        // room; otherwise the point at the viewport centre is kept stable so the
        // slider/wheel zoom about the centre of the view.
        void ApplyZoom(bool centerOnRoom)
        {
            if (!_hasGeom) return;
            var vp = _scroll.contentViewport.layout;
            if (float.IsNaN(vp.width) || vp.width < 1 || float.IsNaN(vp.height) || vp.height < 1)
            {
                _scroll.schedule.Execute(() => ApplyZoom(centerOnRoom));
                return;
            }
            Vector2 half = new Vector2(vp.width * 0.5f, vp.height * 0.5f);
            Vector2 baseCenter = centerOnRoom
                ? _curCenter + new Vector2(CenterPad, CenterPad)
                : (_scroll.scrollOffset + half) / Mathf.Max(0.0001f, _appliedZoom);

            _canvas.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(0f), 0f);
            _canvas.style.scale = new Scale(new Vector2(_zoom, _zoom));
            _canvas.style.width = _canvasBaseW * _zoom;
            _canvas.style.height = _canvasBaseH * _zoom;
            _appliedZoom = _zoom;

            var target = new Vector2(Mathf.Max(0f, baseCenter.x * _zoom - half.x),
                                     Mathf.Max(0f, baseCenter.y * _zoom - half.y));
            _scroll.scrollOffset = target;
            _scroll.schedule.Execute(() => _scroll.scrollOffset = target);
        }

        VisualElement BuildRoomBox(MapRoom r, float px, float py)
        {
            bool cur = r.Id == _map.CurrentRoomId;
            Color bg = NodeColor(r.Id);
            if (cur) bg = new Color(Mathf.Min(1f, bg.r * 1.5f + 0.06f), Mathf.Min(1f, bg.g * 1.5f + 0.06f), Mathf.Min(1f, bg.b * 1.5f + 0.06f));
            Color bc = cur ? CurBorder : Border;
            int bw = cur ? 2 : 1;
            var box = new VisualElement
            {
                style =
                {
                    position = Position.Absolute, left = px, top = py, width = BoxW, height = BoxH,
                    backgroundColor = bg,
                    borderTopWidth = bw, borderBottomWidth = bw, borderLeftWidth = bw, borderRightWidth = bw,
                    borderTopColor = bc, borderBottomColor = bc, borderLeftColor = bc, borderRightColor = bc,
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
                var sym = new Label("◦") { tooltip = o.Name + " (#" + o.Id + ")", style = { color = ItemCol, fontSize = 13, marginRight = 2 } };
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
            var o = _drawOffset;
            foreach (var c in _lines)
            {
                p.strokeColor = c.col;
                p.BeginPath();
                p.MoveTo(c.p0 + o);
                p.BezierCurveTo(c.c1 + o, c.c2 + o, c.p1 + o);
                p.Stroke();
                if (c.arrowEnd) Arrowhead(p, c.p1 + o, c.p1 - c.c2, c.col);
                if (c.arrowStart) Arrowhead(p, c.p0 + o, c.p0 - c.c1, c.col);
            }
        }

        // A small filled triangle at 'tip', pointing along 'dir'.
        static void Arrowhead(Painter2D p, Vector2 tip, Vector2 dir, Color col)
        {
            if (dir.sqrMagnitude < 0.001f) return;
            dir = dir.normalized;
            var perp = new Vector2(-dir.y, dir.x);
            const float len = 9f, half = 4.5f;
            Vector2 baseC = tip - dir * len;
            p.fillColor = col;
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
                : L10n.Tr("Location: ") + RoomLabel(o.Room)));
            if (o.Container != 0 && _map.Objects.TryGetValue(o.Container, out var c))
                _info.Add(Wrapped(L10n.Tr("Inside: ") + c.Name + " (#" + o.Container + ")"));
            _info.Add(Wrapped(L10n.Tr("First seen: ") + RoomLabel(o.OriginRoom)));
            if (o.Room != o.OriginRoom || o.Carried)
                _info.Add(new Label(L10n.Tr("(moved since first seen)")) { style = { color = new Color(0.85f, 0.7f, 0.4f), whiteSpace = WhiteSpace.Normal } });
        }

        void AddObjectLink(MapObject o)
        {
            var b = new Label("◦ " + o.Name + " (#" + o.Id + ")") { style = { color = ItemCol, whiteSpace = WhiteSpace.Normal } };
            int id = o.Id;
            b.RegisterCallback<MouseDownEvent>(e => { ShowObjectInfo(id); e.StopPropagation(); });
            _info.Add(b);
        }

        string RoomLabel(int id) => _map != null && _map.Rooms.TryGetValue(id, out var r) ? r.Name + " (#" + id + ")" : "?";

        static Label Header(string t) =>
            new Label(t) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } };
        static Label Wrapped(string t) =>
            new Label(t) { style = { whiteSpace = WhiteSpace.Normal } };
    }
}
#endif
