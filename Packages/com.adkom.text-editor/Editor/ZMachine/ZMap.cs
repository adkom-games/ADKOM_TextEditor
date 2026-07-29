#if UNITY_EDITOR
// ATE Z-Machine — auto-mapper model. Built purely by OBSERVING engine state:
// the current room (global 0) and the object containment tree, plus the
// direction word from the player's command. Nothing here drives execution.
//
// Rooms form a graph laid out on a per-level grid (compass moves place
// neighbours; up/down change level; in/out and teleports are recorded as
// links but may leave a room unplaced). Objects are tracked through the
// containment tree: current room, whether carried, and where first seen.
using System.Collections.Generic;

namespace AteZMachine
{
    public enum Dir { N, S, E, W, NE, NW, SE, SW, U, D, In, Out }

    public sealed class MapRoom
    {
        public int Id;
        public string Name;
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
        public int CurrentRoomId { get; private set; }
        public int PlayerObj { get; private set; } = -1;

        int _prevRoom;
        Dictionary<int, int> _prevParents = new Dictionary<int, int>();

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
                if (Rooms.Count == 0) { mr.X = mr.Y = mr.Level = 0; mr.Placed = true; }
                else if (dir.HasValue && CurrentRoomId != 0 &&
                         Rooms.TryGetValue(CurrentRoomId, out var from) && from.Placed)
                    Place(mr, from, dir.Value);
                // else: reached with no known direction → leave unplaced
                Rooms[room] = mr;
            }
            else if (string.IsNullOrEmpty(mr.Name)) mr.Name = zm.MapObjectName(room);

            // Record the directed edge (never assume the reverse).
            if (dir.HasValue && CurrentRoomId != 0 && CurrentRoomId != room &&
                Rooms.TryGetValue(CurrentRoomId, out var cur))
                cur.Exits[dir.Value] = room;

            DetectPlayer(zm, room);
            CurrentRoomId = room;
            ScanObjects(zm);
            _prevRoom = room;
            Changed?.Invoke();
        }

        void Place(MapRoom mr, MapRoom from, Dir d)
        {
            var (dx, dy, dl) = Delta(d);
            mr.Level = from.Level + dl;
            mr.X = from.X + dx;
            mr.Y = from.Y + dy;
            // v1 collision policy: nudge along the same axis until free (keeps
            // the grid readable; non-Euclidean maps can still overlap and that
            // is accepted — an explorer's map, not a perfect one).
            int guard = 0;
            while (guard++ < 8 && Occupied(mr.X, mr.Y, mr.Level, mr.Id))
            { mr.X += (dx != 0 ? System.Math.Sign(dx) : 1); mr.Y += (dy != 0 ? System.Math.Sign(dy) : 0); }
            mr.Placed = true;
        }

        bool Occupied(int x, int y, int level, int selfId)
        {
            foreach (var r in Rooms.Values)
                if (r.Id != selfId && r.Placed && r.Level == level && r.X == x && r.Y == y) return true;
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
            for (int o = 1; o <= max; o++)
            {
                int parent = zm.MapParent(o);
                parents[o] = parent;
                if (Rooms.ContainsKey(o)) continue;         // it's a room, not an item
                string name = zm.MapObjectName(o);
                if (string.IsNullOrEmpty(name) || parent == 0) continue;

                int locRoom = ContainingRoom(zm, o, out bool carried, out int container);
                if (locRoom == 0) continue;                 // not in a room we know

                if (!Objects.TryGetValue(o, out var mo))
                    Objects[o] = mo = new MapObject { Id = o, OriginRoom = locRoom };
                mo.Name = name;
                mo.Room = locRoom;
                mo.Carried = carried;
                mo.Container = container;
            }
            _prevParents = parents;
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
    }
}
#endif
