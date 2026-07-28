// ATE Rogue 5.4.4 port — level generation and rendering
// (new_level.c, rooms.c, passages.c, misc.c look()).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AteRogue
{
    public partial class Game
    {
        // What the player has discovered (terrain memory painted to screen).
        readonly char[,] _shown = new char[Const.NUMLINES, Const.NUMCOLS];

        const int NTRAPS = 8;
        public const int T_DOOR = 0, T_ARROW = 1, T_SLEEP = 2, T_BEAR = 3,
            T_TELEP = 4, T_DART = 5, T_RUST = 6, T_MYST = 7;

        public void NewLevel()
        {
            ClearP(MF.ISHELD);
            if (LevelNum > MaxLevel) MaxLevel = LevelNum;
            for (int y = 0; y < Const.NUMLINES; y++)
                for (int x = 0; x < Const.NUMCOLS; x++)
                {
                    Map[y, x] = ' ';
                    FPass[y, x] = false; FReal[y, x] = true; FSeen[y, x] = false;
                    TrapType[y, x] = 0; PassNum[y, x] = 0;
                    _shown[y, x] = ' ';
                }
            MonstersOnLevel.Clear();
            ObjectsOnLevel.Clear();
            Passages.Clear();

            DoRooms();
            DoPassages();
            NoFood++;
            PutThings();

            // Traps (never in mazes/passages: cell must be FLOOR).
            if (Rnd.Next(10) < LevelNum)
            {
                int ntraps = Math.Min(Rnd.Next(LevelNum / 4 + 1) + 1, Const.MAXTRAPS);
                for (int i = 0; i < ntraps; i++)
                {
                    Coord c;
                    int guard = 200;
                    do { c = FindFloorAny(); } while (Map[c.y, c.x] != Const.FLOOR && guard-- > 0);
                    if (guard <= 0) continue;
                    FReal[c.y, c.x] = false;
                    TrapType[c.y, c.x] = Rnd.Next(NTRAPS);
                }
            }

            Stairs = FindFloorAny();
            Map[Stairs.y, Stairs.x] = Const.STAIRS;
            SeenStairs = false;

            // Hero: any monster-free steppable interior spot.
            Coord h;
            do { h = FindFloorAny(); } while (MonsterAt(h) != null);
            Hero = h;
            CurRoom = RoomIn(Hero);
            EnterRoom(Hero);
            Look(wakeup: false);
            UpdateStatus();
        }

        // ---- Rooms (rooms.c) ----

        void DoRooms()
        {
            int bszeX = Const.NUMCOLS / 3, bszeY = Const.NUMLINES / 3; // 26 x 8
            for (int i = 0; i < Const.MAXROOMS; i++) Rooms[i] = new Room();
            int leftOut = Rnd.Next(4);
            for (int i = 0; i < leftOut; i++)
                Rooms[RndRoomIndexNotGone()].Flags |= RoomFlags.ISGONE;

            for (int i = 0; i < Const.MAXROOMS; i++)
            {
                var rp = Rooms[i];
                var top = new Coord((i / 3) * bszeY, (i % 3) * bszeX + 1);
                if (rp.IsGone)
                {
                    do
                    {
                        rp.Pos = new Coord(top.y + Rnd.Next(bszeY - 2) + 1,
                                           top.x + Rnd.Next(bszeX - 2) + 1);
                        rp.Max = new Coord(-Const.NUMLINES, -Const.NUMCOLS);
                    } while (!(rp.Pos.y > 0 && rp.Pos.y < Const.NUMLINES - 1));
                    continue;
                }
                if (Rnd.Next(10) < LevelNum - 1)
                {
                    rp.Flags |= RoomFlags.ISDARK;
                    if (Rnd.Next(15) == 0) rp.Flags = RoomFlags.ISMAZE;
                }
                if (rp.IsMaze)
                {
                    rp.Max = new Coord(bszeY - 1, bszeX - 1);
                    int px = top.x == 1 ? 0 : top.x;
                    int py = top.y;
                    var max = rp.Max;
                    if (py == 0) { py = 1; max.y--; }
                    rp.Pos = new Coord(py, px);
                    rp.Max = max;
                }
                else
                {
                    do
                    {
                        rp.Max = new Coord(Rnd.Next(bszeY - 4) + 4, Rnd.Next(bszeX - 4) + 4);
                        rp.Pos = new Coord(top.y + Rnd.Next(bszeY - rp.Max.y),
                                           top.x + Rnd.Next(bszeX - rp.Max.x));
                    } while (rp.Pos.y == 0);
                }
                DrawRoom(rp);

                // Gold.
                if (Rnd.Next(2) == 0 && (!HasAmulet || LevelNum >= MaxLevel))
                {
                    rp.GoldVal = GoldCalc();
                    rp.GoldPos = FindFloor(rp, 0, false);
                    var gold = new Thing { Kind = ThingKind.Gold, GoldVal = rp.GoldVal, Pos = rp.GoldPos, Group = 1 };
                    ObjectsOnLevel.Add(gold);
                }
                // Monster.
                if (Rnd.Next(100) < (rp.GoldVal > 0 ? 80 : 25))
                {
                    var mp = FindFloor(rp, 0, true);
                    var m = Monsters.New(Monsters.RandMonster(false, LevelNum), mp, LevelNum);
                    MonstersOnLevel.Add(m);
                    GivePack(m);
                }
            }
        }

        int RndRoomIndexNotGone()
        {
            int i;
            do { i = Rnd.Next(Const.MAXROOMS); } while (Rooms[i].IsGone);
            return i;
        }

        public Room RndRoom()
        {
            return Rooms[RndRoomIndexNotGone()];
        }

        void DrawRoom(Room rp)
        {
            if (rp.IsMaze) { DoMaze(rp); return; }
            for (int y = rp.Pos.y + 1; y < rp.Pos.y + rp.Max.y - 1; y++)
            {
                Map[y, rp.Pos.x] = Const.WALL_V;
                Map[y, rp.Pos.x + rp.Max.x - 1] = Const.WALL_V;
            }
            for (int x = rp.Pos.x; x < rp.Pos.x + rp.Max.x; x++)
            {
                Map[rp.Pos.y, x] = Const.WALL_H;
                Map[rp.Pos.y + rp.Max.y - 1, x] = Const.WALL_H;
            }
            for (int y = rp.Pos.y + 1; y < rp.Pos.y + rp.Max.y - 1; y++)
                for (int x = rp.Pos.x + 1; x < rp.Pos.x + rp.Max.x - 1; x++)
                    Map[y, x] = Const.FLOOR;
        }

        public void GivePack(Monster m)
        {
            if (LevelNum >= MaxLevel && Rnd.Next(100) < Monsters.Info(m.Type).Carry)
                m.Pack.Add(NewThing());
        }

        // ---- Mazes (rooms.c dig) ----

        void DoMaze(Room rp)
        {
            int maxY = rp.Max.y - 1, maxX = rp.Max.x - 1;
            var start = new Coord(Rnd.Next(rp.Max.y) / 2 * 2, Rnd.Next(rp.Max.x) / 2 * 2);
            PutPass(new Coord(rp.Pos.y + start.y, rp.Pos.x + start.x));
            Dig(rp, start, maxY, maxX);
        }

        void Dig(Room rp, Coord cell, int maxY, int maxX)
        {
            while (true)
            {
                int cnt = 0;
                Coord pick = default;
                foreach (var d in new[] { new Coord(-2, 0), new Coord(2, 0), new Coord(0, -2), new Coord(0, 2) })
                {
                    var nc = new Coord(cell.y + d.y, cell.x + d.x);
                    if (nc.y < 0 || nc.y > maxY || nc.x < 0 || nc.x > maxX) continue;
                    if (FPass[rp.Pos.y + nc.y, rp.Pos.x + nc.x]) continue;
                    if (Rnd.Next(++cnt) == 0) pick = nc;
                }
                if (cnt == 0) return;
                var between = new Coord(rp.Pos.y + (cell.y + pick.y) / 2, rp.Pos.x + (cell.x + pick.x) / 2);
                PutPass(between);
                PutPass(new Coord(rp.Pos.y + pick.y, rp.Pos.x + pick.x));
                Dig(rp, pick, maxY, maxX);
            }
        }

        void PutPass(Coord c)
        {
            FPass[c.y, c.x] = true;
            if (Rnd.Next(10) + 1 < LevelNum && Rnd.Next(40) == 0)
                FReal[c.y, c.x] = false; // secret passage, stays blank
            else Map[c.y, c.x] = Const.PASSAGE;
        }

        // ---- Passages (passages.c) ----

        static readonly int[][] Adjacent =
        {
            new[]{1,3}, new[]{0,2,4}, new[]{1,5},
            new[]{0,4,6}, new[]{1,3,5,7}, new[]{2,4,8},
            new[]{3,7}, new[]{4,6,8}, new[]{5,7}
        };

        void DoPassages()
        {
            bool[,] isconn = new bool[9, 9];
            bool[] ingraph = new bool[9];
            int r1 = Rnd.Next(9);
            ingraph[r1] = true;
            int roomcount = 1;
            while (roomcount < 9)
            {
                int j = 0, r2 = -1;
                foreach (int adj in Adjacent[r1])
                    if (!ingraph[adj] && Rnd.Next(++j) == 0) r2 = adj;
                if (j == 0)
                {
                    do { r1 = Rnd.Next(9); } while (!ingraph[r1]);
                }
                else
                {
                    Conn(r1, r2);
                    isconn[r1, r2] = isconn[r2, r1] = true;
                    ingraph[r2] = true;
                    roomcount++;
                    r1 = r2;
                }
            }
            int extras = Rnd.Next(5);
            for (int i = 0; i < extras; i++)
            {
                r1 = Rnd.Next(9);
                int j = 0, r2 = -1;
                foreach (int adj in Adjacent[r1])
                    if (!isconn[r1, adj] && Rnd.Next(++j) == 0) r2 = adj;
                if (j > 0)
                {
                    Conn(r1, r2);
                    isconn[r1, r2] = isconn[r2, r1] = true;
                }
            }
            PassnumAll();
        }

        void Conn(int ra, int rb)
        {
            int rm = Math.Min(ra, rb);
            bool right = Math.Abs(ra - rb) == 1;
            var rpf = Rooms[rm];
            var rpt = Rooms[right ? rm + 1 : rm + 3];
            Coord spos, epos, del, turnDelta;
            int distance, turnDistance;
            if (!right)
            {
                del = new Coord(1, 0);
                spos = rpf.IsGone ? rpf.Pos
                    : new Coord(rpf.Pos.y + rpf.Max.y - 1, rpf.Pos.x + Rnd.Next(rpf.Max.x - 2) + 1);
                if (!rpf.IsGone && rpf.IsMaze) spos = MazeEdge(rpf, spos, bottom: true);
                epos = rpt.IsGone ? rpt.Pos
                    : new Coord(rpt.Pos.y, rpt.Pos.x + Rnd.Next(rpt.Max.x - 2) + 1);
                if (!rpt.IsGone && rpt.IsMaze) epos = MazeEdge(rpt, epos, bottom: false);
                distance = Math.Abs(spos.y - epos.y) - 1;
                turnDelta = new Coord(0, Math.Sign(epos.x - spos.x));
                turnDistance = Math.Abs(spos.x - epos.x);
            }
            else
            {
                del = new Coord(0, 1);
                spos = rpf.IsGone ? rpf.Pos
                    : new Coord(rpf.Pos.y + Rnd.Next(rpf.Max.y - 2) + 1, rpf.Pos.x + rpf.Max.x - 1);
                if (!rpf.IsGone && rpf.IsMaze) spos = MazeEdgeRight(rpf, spos, rightWall: true);
                epos = rpt.IsGone ? rpt.Pos
                    : new Coord(rpt.Pos.y + Rnd.Next(rpt.Max.y - 2) + 1, rpt.Pos.x);
                if (!rpt.IsGone && rpt.IsMaze) epos = MazeEdgeRight(rpt, epos, rightWall: false);
                distance = Math.Abs(spos.x - epos.x) - 1;
                turnDelta = new Coord(Math.Sign(epos.y - spos.y), 0);
                turnDistance = Math.Abs(spos.y - epos.y);
            }
            int turnSpot = distance > 1 ? Rnd.Next(distance - 1) + 1 : 1;
            if (rpf.IsGone) PutPass(spos); else PlaceDoor(rpf, spos);
            if (rpt.IsGone) PutPass(epos); else PlaceDoor(rpt, epos);
            var curr = spos;
            while (distance > 0)
            {
                curr = new Coord(curr.y + del.y, curr.x + del.x);
                if (distance == turnSpot)
                {
                    int td = turnDistance;
                    while (td-- > 0)
                    {
                        PutPass(curr);
                        curr = new Coord(curr.y + turnDelta.y, curr.x + turnDelta.x);
                    }
                }
                PutPass(curr);
                distance--;
            }
        }

        // Maze rooms have no walls: the connection endpoint must land on a
        // maze corridor cell of the touching edge.
        Coord MazeEdge(Room rp, Coord want, bool bottom)
        {
            int y = bottom ? rp.Pos.y + rp.Max.y - 1 : rp.Pos.y;
            for (int off = 0; off < rp.Max.x; off++)
                foreach (int sgn in new[] { 1, -1 })
                {
                    int x = want.x + off * sgn;
                    if (x < rp.Pos.x || x >= rp.Pos.x + rp.Max.x) continue;
                    for (int dy = 0; dy < rp.Max.y; dy++)
                    {
                        int yy = bottom ? y - dy : y + dy;
                        if (FPass[yy, x]) return new Coord(yy, x);
                    }
                }
            return want;
        }

        Coord MazeEdgeRight(Room rp, Coord want, bool rightWall)
        {
            int x = rightWall ? rp.Pos.x + rp.Max.x - 1 : rp.Pos.x;
            for (int off = 0; off < rp.Max.y; off++)
                foreach (int sgn in new[] { 1, -1 })
                {
                    int y = want.y + off * sgn;
                    if (y < rp.Pos.y || y >= rp.Pos.y + rp.Max.y) continue;
                    for (int dx = 0; dx < rp.Max.x; dx++)
                    {
                        int xx = rightWall ? x - dx : x + dx;
                        if (FPass[y, xx]) return new Coord(y, xx);
                    }
                }
            return want;
        }

        void PlaceDoor(Room rp, Coord c)
        {
            rp.Exits.Add(c);
            if (rp.IsMaze) return;
            if (Rnd.Next(10) + 1 < LevelNum && Rnd.Next(5) == 0)
            {
                Map[c.y, c.x] = (c.y == rp.Pos.y || c.y == rp.Pos.y + rp.Max.y - 1)
                    ? Const.WALL_H : Const.WALL_V;
                FReal[c.y, c.x] = false; // secret door
            }
            else Map[c.y, c.x] = Const.DOOR;
        }

        void PassnumAll()
        {
            int pnum = 0;
            var visited = new bool[Const.NUMLINES, Const.NUMCOLS];
            foreach (var rp in Rooms)
            {
                foreach (var exit in rp.Exits)
                {
                    if (visited[exit.y, exit.x]) continue;
                    pnum++;
                    var pass = new Room { Flags = RoomFlags.ISGONE | RoomFlags.ISDARK };
                    Passages.Add(pass);
                    FloodPass(exit, pnum, pass, visited);
                }
            }
        }

        void FloodPass(Coord c, int pnum, Room pass, bool[,] visited)
        {
            var stack = new Stack<Coord>();
            stack.Push(c);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.x < 0 || p.x >= Const.NUMCOLS || p.y <= 0 || p.y >= Const.NUMLINES - 1) continue;
                if (visited[p.y, p.x]) continue;
                bool isDoor = Map[p.y, p.x] == Const.DOOR ||
                    (!FReal[p.y, p.x] && (Map[p.y, p.x] == Const.WALL_H || Map[p.y, p.x] == Const.WALL_V));
                if (!isDoor && !FPass[p.y, p.x]) continue;
                visited[p.y, p.x] = true;
                PassNum[p.y, p.x] = pnum;
                if (isDoor) { pass.Exits.Add(p); continue; }
                stack.Push(new Coord(p.y - 1, p.x));
                stack.Push(new Coord(p.y + 1, p.x));
                stack.Push(new Coord(p.y, p.x - 1));
                stack.Push(new Coord(p.y, p.x + 1));
            }
        }

        public Room PassageAt(Coord c)
        {
            int n = PassNum[c.y, c.x];
            return n >= 1 && n <= Passages.Count ? Passages[n - 1] : null;
        }

        // ---- Things on the level (things.c put_things + treas_room) ----

        const int TREAS_ROOM = 20, MINTREAS = 2, MAXTREAS = 10;

        void PutThings()
        {
            if (HasAmulet && LevelNum < MaxLevel) return;
            if (Rnd.Next(TREAS_ROOM) == 0) TreasureRoom();
            for (int i = 0; i < Const.MAXOBJ; i++)
            {
                if (Rnd.Next(100) >= 36) continue;
                var t = NewThing();
                t.Pos = FindFloorAny();
                ObjectsOnLevel.Add(t);
            }
            if (LevelNum >= Const.AMULETLEVEL && !HasAmulet)
            {
                var amulet = new Thing { Kind = ThingKind.Amulet, Arm = 11, Pos = FindFloorAny() };
                ObjectsOnLevel.Add(amulet);
            }
        }

        void TreasureRoom()
        {
            var rp = RndRoom();
            if (rp.IsMaze) return;
            int spots = Math.Min((rp.Max.y - 2) * (rp.Max.x - 2) - 2, MAXTREAS - MINTREAS);
            if (spots < 1) return;
            int numTreasure = Rnd.Next(spots) + MINTREAS;
            for (int i = 0; i < numTreasure; i++)
            {
                var t = NewThing();
                t.Pos = FindFloor(rp, 20, false);
                ObjectsOnLevel.Add(t);
            }
            int nm = Math.Max(Rnd.Next(spots) + MINTREAS, numTreasure + 2);
            nm = Math.Min(nm, (rp.Max.y - 2) * (rp.Max.x - 2));
            LevelNum++;
            for (int i = 0; i < nm; i++)
            {
                var pos = FindFloor(rp, 10, true);
                if (MonsterAt(pos) != null) continue;
                var m = Monsters.New(Monsters.RandMonster(false, LevelNum), pos, LevelNum);
                m.Set(MF.ISMEAN);
                MonstersOnLevel.Add(m);
                GivePack(m);
            }
            LevelNum--;
        }

        /// <summary>find_floor(): random interior spot of a (or any) room.</summary>
        public Coord FindFloor(Room rp, int limit, bool monst)
        {
            int tries = limit > 0 ? limit : int.MaxValue;
            while (tries-- > 0)
            {
                var r = rp ?? RndRoom();
                if (r.Max.y <= 2 || r.Max.x <= 2) { if (rp != null) break; continue; }
                var c = new Coord(r.Pos.y + Rnd.Next(r.Max.y - 2) + 1,
                                  r.Pos.x + Rnd.Next(r.Max.x - 2) + 1);
                char compchar = r.IsMaze ? Const.PASSAGE : Const.FLOOR;
                if (monst)
                {
                    if (MonsterAt(c) == null && StepOk(Map[c.y, c.x]) && Hero != c) return c;
                }
                else if (Map[c.y, c.x] == compchar && ObjectAt(c) == null) return c;
            }
            // Fallback: exhaustive scan.
            foreach (var r in Rooms)
            {
                if (r.IsGone) continue;
                for (int y = r.Pos.y + 1; y < r.Pos.y + r.Max.y - 1; y++)
                    for (int x = r.Pos.x + 1; x < r.Pos.x + r.Max.x - 1; x++)
                    {
                        var c = new Coord(y, x);
                        char compchar = r.IsMaze ? Const.PASSAGE : Const.FLOOR;
                        if (monst ? (MonsterAt(c) == null && StepOk(Map[y, x]))
                                  : (Map[y, x] == compchar && ObjectAt(c) == null))
                            return c;
                    }
            }
            return new Coord(1, 1);
        }

        public Coord FindFloorAny() => FindFloor(null, 0, false);

        // ---- Visibility / drawing ----

        public void EnterRoom(Coord cp)
        {
            var rp = RoomIn(cp);
            CurRoom = rp ?? PassageAt(cp);
            if (rp == null) return;
            DoorOpen(rp);
            if (!rp.IsDark && !HasP(MF.ISBLIND))
                for (int y = rp.Pos.y; y < rp.Pos.y + rp.Max.y; y++)
                    for (int x = rp.Pos.x; x < rp.Pos.x + rp.Max.x; x++)
                        Discover(new Coord(y, x));
        }

        public void LeaveRoom(Coord cp)
        {
            var rp = CurRoom;
            CurRoom = PassageAt(cp);
            if (rp == null || rp.IsGone || rp.IsMaze) return;
            if (rp.IsDark && !HasP(MF.ISBLIND))
            {
                // Dark room floors are forgotten on exit (only walls/doors stay).
                for (int y = rp.Pos.y + 1; y < rp.Pos.y + rp.Max.y - 1; y++)
                    for (int x = rp.Pos.x + 1; x < rp.Pos.x + rp.Max.x - 1; x++)
                        _shown[y, x] = ' ';
            }
            DoorOpen(rp);
        }

        void DoorOpen(Room rp)
        {
            foreach (var m in MonstersOnLevel)
                if (rp.Contains(m.Pos)) WakeMonster(m);
        }

        /// <summary>The display char of the map cell as the player knows it
        /// (hidden traps look like floor, secret doors like wall, secret
        /// passages like rock).</summary>
        char KnownChar(int y, int x)
        {
            char c = Map[y, x];
            if (!FReal[y, x])
            {
                if (c == Const.FLOOR) return Const.FLOOR;         // hidden trap
                if (FPass[y, x]) return ' ';                      // secret passage
                return c;                                         // secret door: wall char
            }
            return c;
        }

        void Discover(Coord c)
        {
            char ch = KnownChar(c.y, c.x);
            var t = ObjectAt(c);
            if (t != null && FReal[c.y, c.x]) ch = t.Ch;
            _shown[c.y, c.x] = ch;
            FSeen[c.y, c.x] = true;
        }

        /// <summary>look(): per-turn discovery of the 3x3 around the hero,
        /// with corridor gating and diagonal blocking (misc.c).</summary>
        public void Look(bool wakeup)
        {
            if (HasP(MF.ISBLIND)) return;
            bool heroPass = FPass[Hero.y, Hero.x];
            bool heroDoor = Map[Hero.y, Hero.x] == Const.DOOR;
            for (int y = Math.Max(1, Hero.y - 1); y <= Math.Min(Const.NUMLINES - 2, Hero.y + 1); y++)
                for (int x = Math.Max(0, Hero.x - 1); x <= Math.Min(Const.NUMCOLS - 1, Hero.x + 1); x++)
                {
                    if (y == Hero.y && x == Hero.x) continue;
                    char raw = KnownChar(y, x);
                    if (raw == ' ' && MonsterAt(new Coord(y, x)) == null) continue;
                    bool cellDoor = Map[y, x] == Const.DOOR;
                    if (!heroDoor && !cellDoor && heroPass != FPass[y, x]) continue;
                    if ((heroPass || heroDoor) && y != Hero.y && x != Hero.x)
                    {
                        if (!StepOk(Map[y, Hero.x]) && !StepOk(Map[Hero.y, x])) continue;
                    }
                    var mc = new Coord(y, x);
                    var m = MonsterAt(mc);
                    if (m != null && wakeup) WakeMonster(m);
                    Discover(mc);
                }
            // Lit room: keep it discovered (items may have moved).
            var rp = RoomIn(Hero);
            if (rp != null && !rp.IsDark)
                for (int y = rp.Pos.y; y < rp.Pos.y + rp.Max.y; y++)
                    for (int x = rp.Pos.x; x < rp.Pos.x + rp.Max.x; x++)
                        Discover(new Coord(y, x));
        }

        // ---- Screen composition ----

        static readonly Color GoldColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color MonsterColor = new Color(1f, 0.55f, 0.35f);
        static readonly Color HeroColor = Color.white;
        static readonly Color ItemColor = new Color(0.45f, 0.8f, 1f);
        static readonly Color DetectColor = new Color(0.7f, 0.5f, 1f);

        public void Redraw()
        {
            RedrawMap();
            Term.Flush();
            Term.CursorAt(Hero.y, Hero.x);
        }

        public void RedrawAll()
        {
            Term.Clear();
            Term.ClearToEol(0, 0);
            if (_shownMsg.Length > 0) Term.PutStr(0, 0, _shownMsg);
            RedrawMap();
            UpdateStatus();
            Term.Flush();
        }

        public void RedrawMap()
        {
            for (int y = 1; y < Const.STATLINE; y++)
                for (int x = 0; x < Const.NUMCOLS; x++)
                {
                    char c = _shown[y, x];
                    Color col = Term.Default;
                    if (c == Const.GOLD) col = GoldColor;
                    else if (c == Const.POTION || c == Const.SCROLL || c == Const.RING ||
                             c == Const.STICK || c == Const.WEAPON || c == Const.ARMOR ||
                             c == Const.FOOD || c == Const.AMULET) col = ItemColor;
                    Term.Put(y, x, c, col);
                }
            foreach (var m in MonstersOnLevel)
            {
                if (CanSeeMonster(m))
                {
                    char mc = HasP(MF.ISHALU) ? (char)('A' + Rnd.Next(26)) : m.Disguise;
                    Term.Put(m.Pos.y, m.Pos.x, mc, MonsterColor);
                }
                else if (HasP(MF.SEEMONST))
                    Term.Put(m.Pos.y, m.Pos.x, m.Type, DetectColor);
            }
            Term.Put(Hero.y, Hero.x, Const.PLAYER, HeroColor);
        }
    }
}
