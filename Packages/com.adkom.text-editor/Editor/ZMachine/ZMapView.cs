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

        ZMap _map;
        int _level;
        VisualElement _curBox;

        readonly ScrollView _scroll;
        readonly VisualElement _canvas;
        readonly VisualElement _info;
        readonly Label _levelLabel;
        readonly List<(Vector2 a, Vector2 b)> _lines = new List<(Vector2, Vector2)>();

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
            bar.Add(new Label(L10n.Tr("Map")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 4, marginRight = 8 } });
            bar.Add(down); bar.Add(_levelLabel); bar.Add(up);
            left.Add(bar);

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1 } };
            _canvas = new VisualElement { style = { position = Position.Relative } };
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

            _canvas.style.width = (maxX - minX + 1) * CellW + 20;
            _canvas.style.height = (maxY - minY + 1) * CellH + 20;

            var center = new Dictionary<int, Vector2>();
            foreach (var r in _map.Rooms.Values)
            {
                if (!r.Placed || r.Level != _level) continue;
                float px = (r.X - minX) * CellW + 10;
                float py = (r.Y - minY) * CellH + 10;
                center[r.Id] = new Vector2(px + BoxW / 2f, py + BoxH / 2f);
                var box = BuildRoomBox(r, px, py);
                if (r.Id == _map.CurrentRoomId) _curBox = box;
                _canvas.Add(box);
            }
            // Connection lines (same-level compass exits only).
            foreach (var r in _map.Rooms.Values)
            {
                if (!r.Placed || r.Level != _level || !center.TryGetValue(r.Id, out var a)) continue;
                foreach (var kv in r.Exits)
                {
                    if (kv.Key == Dir.U || kv.Key == Dir.D || kv.Key == Dir.In || kv.Key == Dir.Out) continue;
                    if (center.TryGetValue(kv.Value, out var b)) _lines.Add((a, b));
                }
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
            if (box == _curBox) _scroll.ScrollTo(box);
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
            var title = new Label(r.Name) { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.NoWrap } };
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

        void OnDrawLines(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.strokeColor = LineCol;
            p.lineWidth = 1.5f;
            foreach (var (a, b) in _lines)
            {
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
            }
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
