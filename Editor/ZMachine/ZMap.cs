#if UNITY_EDITOR
// ATE Z-Machine — auto-mapper model. Built purely by OBSERVING engine state:
// the current room (global 0) and the object containment tree, plus the
// direction word from the player's command. Nothing here drives execution.
//
// Rooms form a graph laid out on a per-level grid (compass moves place
// neighbours; up/down change level; in/out and teleports are recorded as
// links but may leave a room unplaced). Objects are tracked through the
// containment tree: current room, whether carried, and where first seen.
using System;
using System.Collections.Generic;
using System.IO;

namespace AteZMachine
{
    public enum Dir { N, S, E, W, NE, NW, SE, SW, U, D, In, Out }

    public sealed class MapRoom
    {
        public int Id;
        public string Name;
        public int Area;          // separate coordinate region (0 = exterior; interiors get their own)
        public int X, Y, Level;
        public bool Placed;
        public readonly Dictionary<Dir, int> Exits = new Dictionary<Dir, int>();
    }

    public sealed class MapObject
    {
        public int Id;
        public string Name;
        public int Room;          // containing room id (0 = unknown)
        public bool Carried;
        public int OriginRoom;    // where first seen
        public int Container;     // immediate parent when it's not the room/player (0 otherwise)
    }

    public sealed class ZMap
    {
        public readonly Dictionary<int, MapRoom> Rooms = new Dictionary<int, MapRoom>();
        public readonly Dictionary<int, MapObject> Objects = new Dictionary<int, MapObject>();
        // Display name of each area (its entry room), for the page heading.
        public readonly Dictionary<int, string> AreaName = new Dictionary<int, string>();
        public int CurrentRoomId { get; private set; }
        public int CurrentAreaId { get; private set; }
        public int PlayerObj { get; private set; } = -1;

        int _prevRoom;
        int _nextArea = 1;
        Dictionary<int, int> _prevParents = new Dictionary<int, int>();
        Dictionary<int, uint> _prevAttrs = new Dictionary<int, uint>();

        public event System.Action Changed;

        /// <summary>Called once per turn (after the command resolves) with the
        /// player's raw input, to update the map from the new engine state.</summary>
        public void Observe(ZMachine zm, string lastInput)
        {
            if (zm == null || !zm.MapReady()) return; // machine not started yet
            int room = zm.MapCurrentRoom();
            if (room <= 0) return;
            Dir? dir = ParseDir(lastInput);

            if (!Rooms.TryGetValue(room, out var mr))
            {
                mr = new MapRoom { Id = room, Name = zm.MapObjectName(room) };
                MapRoom from = null;
                if (CurrentRoomId != 0) Rooms.TryGetValue(CurrentRoomId, out from);
                if (Rooms.Count == 0)
                {
                    mr.Area = 0; mr.X = mr.Y = mr.Level = 0; mr.Placed = true;
                }
                else if (dir == Dir.In && from != null)
                {
                    // Entering a container opens a NEW area at its own origin,
                    // so an interior lays out on its own grid instead of colliding
                    // with the exterior it is nested inside.
                    mr.Area = _nextArea++;
                    mr.X = mr.Y = mr.Level = 0; mr.Placed = true;
                    AreaName[mr.Area] = mr.Name;
                }
                else if (dir.HasValue && from != null && from.Placed)
                {
                    Place(mr, from, dir.Value); // same area as 'from'
                }
                else
                {
                    mr.Area = CurrentAreaId; // reached with no known direction → leave unplaced
                }
                Rooms[room] = mr;
            }
            else if (string.IsNullOrEmpty(mr.Name)) mr.Name = zm.MapObjectName(room);

            // Record the directed edge (never assume the reverse).
            if (dir.HasValue && CurrentRoomId != 0 && CurrentRoomId != room &&
                Rooms.TryGetValue(CurrentRoomId, out var cur))
                cur.Exits[dir.Value] = room;

            SeedPlayer(zm, room);
            DetectPlayer(zm, room);
            CurrentRoomId = room;
            if (Rooms.TryGetValue(room, out var here)) CurrentAreaId = here.Area;
            if (PlayerObj > 0) Objects.Remove(PlayerObj); // never map the player avatar
            ScanObjects(zm);
            _prevRoom = room;
            Changed?.Invoke();
        }

        void Place(MapRoom mr, MapRoom from, Dir d)
        {
            mr.Area = from.Area; // stays in the same region
            var (dx, dy, dl) = Delta(d);
            mr.Level = from.Level + dl;
            mr.X = from.X + dx;
            mr.Y = from.Y + dy;
            // v1 collision policy: nudge along the same axis until free (keeps
            // the grid readable; non-Euclidean maps can still overlap and that
            // is accepted — an explorer's map, not a perfect one).
            int guard = 0;
            while (guard++ < 8 && Occupied(mr.X, mr.Y, mr.Level, mr.Area, mr.Id))
            { mr.X += (dx != 0 ? System.Math.Sign(dx) : 1); mr.Y += (dy != 0 ? System.Math.Sign(dy) : 0); }
            mr.Placed = true;
        }

        bool Occupied(int x, int y, int level, int area, int selfId)
        {
            foreach (var r in Rooms.Values)
                if (r.Id != selfId && r.Placed && r.Area == area && r.Level == level && r.X == x && r.Y == y) return true;
            return false;
        }

        static (int, int, int) Delta(Dir d)
        {
            switch (d)
            {
                case Dir.N: return (0, -1, 0);
                case Dir.S: return (0, 1, 0);
                case Dir.E: return (1, 0, 0);
                case Dir.W: return (-1, 0, 0);
                case Dir.NE: return (1, -1, 0);
                case Dir.NW: return (-1, -1, 0);
                case Dir.SE: return (1, 1, 0);
                case Dir.SW: return (-1, 1, 0);
                case Dir.U: return (0, 0, 1);
                case Dir.D: return (0, 0, -1);
                default: return (0, 0, 0); // In / Out: no grid delta
            }
        }

        // Turn-1 detection: the player/actor object is a child of the current
        // room that a global variable points to (games keep a player global)
        // and holds no visible contents at the start (containers like a
        // mailbox are also global-referenced but have children). If exactly
        // one room-child qualifies, take it as the player. Movement detection
        // corrects/confirms it once the player first changes rooms.
        void SeedPlayer(ZMachine zm, int room)
        {
            if (PlayerObj > 0) return;
            int max = zm.MapMaxObject();
            int found = 0, count = 0;
            for (int o = 1; o <= max; o++)
            {
                if (zm.MapParent(o) != room || Rooms.ContainsKey(o)) continue;
                if (string.IsNullOrEmpty(zm.MapObjectName(o))) continue;
                if (!zm.MapReferencedByGlobal(o) || zm.MapChild(o) != 0) continue;
                found = o; count++;
            }
            if (count == 1) PlayerObj = found;
        }

        // The player object's parent tracks the current room: on the first
        // room change, the object that moved oldRoom → newRoom is the player.
        void DetectPlayer(ZMachine zm, int room)
        {
            if (PlayerObj > 0) return;
            if (_prevRoom != 0 && room != _prevRoom)
            {
                int max = zm.MapMaxObject();
                for (int o = 1; o <= max; o++)
                    if (zm.MapParent(o) == room && _prevParents.TryGetValue(o, out int pp) && pp == _prevRoom)
                    { PlayerObj = o; break; }
            }
        }

        void ScanObjects(ZMachine zm)
        {
            int max = zm.MapMaxObject();
            var parents = new Dictionary<int, int>(max);
            var attrs = new Dictionary<int, uint>(max);
            for (int o = 1; o <= max; o++)
            {
                int parent = zm.MapParent(o);
                parents[o] = parent;
                attrs[o] = zm.MapAttrBits(o);
                if (o == PlayerObj) continue;               // never map the player avatar
                if (Rooms.ContainsKey(o)) continue;         // it's a room, not an item
                string name = zm.MapObjectName(o);
                if (string.IsNullOrEmpty(name) || parent == 0) continue;

                int locRoom = ContainingRoom(zm, o, out bool carried, out int container);
                if (locRoom == 0) continue;                 // not in a room we know

                bool alreadySeen = Objects.ContainsKey(o);
                // NO SPOILERS: a NEW object is shown only once it is directly
                // visible — sitting in a room, or carried — OR the game has just
                // REVEALED it. Something nested in a container stays hidden until
                // then. Once seen, it stays tracked wherever it later goes.
                bool directlyVisible = Rooms.ContainsKey(parent) || (PlayerObj > 0 && parent == PlayerObj);

                // Reveal a nested object when, since last turn, its OWN attribute
                // flags changed (the game cleared an "invisible"-style flag, e.g.
                // moving leaves reveals a grating) OR its immediate container's
                // flags changed (e.g. opening a mailbox reveals the leaflet).
                // This only fires AFTER the player acts on it, so nothing is
                // spoiled at the start.
                bool revealed = !directlyVisible && !carried && (
                    (_prevAttrs.TryGetValue(o, out uint pa) && pa != attrs[o]) ||
                    (_prevAttrs.TryGetValue(parent, out uint pc) && pc != zm.MapAttrBits(parent)));

                if (!alreadySeen && !directlyVisible && !revealed) continue;

                if (!alreadySeen) Objects[o] = new MapObject { Id = o, OriginRoom = locRoom };
                var mo = Objects[o];
                mo.Name = name;
                mo.Room = locRoom;
                mo.Carried = carried;
                mo.Container = container;
            }
            _prevParents = parents;
            _prevAttrs = attrs;
        }

        int ContainingRoom(ZMachine zm, int obj, out bool carried, out int container)
        {
            carried = false; container = 0;
            int node = obj, immediate = zm.MapParent(obj);
            int guard = 0;
            while (node > 0 && guard++ < 64)
            {
                int parent = zm.MapParent(node);
                if (parent == 0) return Rooms.ContainsKey(node) ? node : 0;
                if (PlayerObj > 0 && parent == PlayerObj) { carried = true; return CurrentRoomId; }
                if (Rooms.ContainsKey(parent))
                {
                    if (node != obj) container = node; // obj sat inside something in the room
                    return parent;
                }
                node = parent;
            }
            return 0;
        }

        static Dir? ParseDir(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            foreach (var raw in input.ToLowerInvariant().Split(' '))
            {
                string w = raw.Trim();
                if (w.Length == 0 || w == "go" || w == "walk" || w == "run") continue;
                switch (w)
                {
                    case "n": case "north": return Dir.N;
                    case "s": case "south": return Dir.S;
                    case "e": case "east": return Dir.E;
                    case "w": case "west": return Dir.W;
                    case "ne": case "northeast": return Dir.NE;
                    case "nw": case "northwest": return Dir.NW;
                    case "se": case "southeast": return Dir.SE;
                    case "sw": case "southwest": return Dir.SW;
                    case "u": case "up": return Dir.U;
                    case "d": case "down": return Dir.D;
                    case "in": case "inside": case "enter": return Dir.In;
                    case "out": case "outside": case "exit": return Dir.Out;
                    default: return null; // first real word isn't a direction
                }
            }
            return null;
        }

        public static string DirName(Dir d) => d.ToString().ToLowerInvariant();

        // ---- Persistence (sidecar to the game save) ----

        const int MapFormat = 2;

        public void SaveTo(string path)
        {
            try
            {
                using (var w = new BinaryWriter(File.Create(path)))
                {
                    w.Write(MapFormat);
                    w.Write(CurrentRoomId); w.Write(PlayerObj); w.Write(_prevRoom);
                    w.Write(CurrentAreaId); w.Write(_nextArea);
                    w.Write(AreaName.Count);
                    foreach (var kv in AreaName) { w.Write(kv.Key); w.Write(kv.Value ?? ""); }
                    w.Write(Rooms.Count);
                    foreach (var r in Rooms.Values)
                    {
                        w.Write(r.Id); w.Write(r.Name ?? ""); w.Write(r.Area); w.Write(r.X); w.Write(r.Y);
                        w.Write(r.Level); w.Write(r.Placed);
                        w.Write(r.Exits.Count);
                        foreach (var kv in r.Exits) { w.Write((int)kv.Key); w.Write(kv.Value); }
                    }
                    w.Write(Objects.Count);
                    foreach (var o in Objects.Values)
                    {
                        w.Write(o.Id); w.Write(o.Name ?? ""); w.Write(o.Room);
                        w.Write(o.Carried); w.Write(o.OriginRoom); w.Write(o.Container);
                    }
                }
            }
            catch { /* map persistence is best-effort */ }
        }

        public bool LoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadInt32() != MapFormat) return false;
                    Rooms.Clear(); Objects.Clear(); AreaName.Clear();
                    CurrentRoomId = r.ReadInt32(); PlayerObj = r.ReadInt32(); _prevRoom = r.ReadInt32();
                    CurrentAreaId = r.ReadInt32(); _nextArea = r.ReadInt32();
                    int na = r.ReadInt32();
                    for (int i = 0; i < na; i++) { int k = r.ReadInt32(); AreaName[k] = r.ReadString(); }
                    int nr = r.ReadInt32();
                    for (int i = 0; i < nr; i++)
                    {
                        var mr = new MapRoom { Id = r.ReadInt32(), Name = r.ReadString(), Area = r.ReadInt32(),
                            X = r.ReadInt32(), Y = r.ReadInt32(), Level = r.ReadInt32(), Placed = r.ReadBoolean() };
                        int ne = r.ReadInt32();
                        for (int e = 0; e < ne; e++) mr.Exits[(Dir)r.ReadInt32()] = r.ReadInt32();
                        Rooms[mr.Id] = mr;
                    }
                    int no = r.ReadInt32();
                    for (int i = 0; i < no; i++)
                    {
                        var mo = new MapObject { Id = r.ReadInt32(), Name = r.ReadString(), Room = r.ReadInt32(),
                            Carried = r.ReadBoolean(), OriginRoom = r.ReadInt32(), Container = r.ReadInt32() };
                        Objects[mo.Id] = mo;
                    }
                }
                Changed?.Invoke();
                return true;
            }
            catch { return false; }
        }
    }
}
#endif
