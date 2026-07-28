// ATE Rogue 5.4.4 port — monster AI (chase.c, monsters.c wake/runto).
using System;
using System.Collections.Generic;

namespace AteRogue
{
    public partial class Game
    {
        /// <summary>runners(): every monster takes its move(s). Flying
        /// monsters not adjacent get a second move.</summary>
        public void Runners()
        {
            var snapshot = new List<Monster>(MonstersOnLevel);
            foreach (var m in snapshot)
            {
                if (GameOver) return;
                if (!MonstersOnLevel.Contains(m)) continue;
                if (m.Has(MF.ISHELD) || !m.Has(MF.ISRUN)) continue;
                bool wasNotAdjacent = Coord.Dist(Hero, m.Pos) >= 3;
                MoveMonst(m);
                if (GameOver) return;
                if (!MonstersOnLevel.Contains(m)) continue;
                if (m.Has(MF.ISFLY) && wasNotAdjacent && Coord.Dist(Hero, m.Pos) >= 3)
                    MoveMonst(m);
            }
        }

        void MoveMonst(Monster m)
        {
            if (!m.Has(MF.ISSLOW) || m.TTurn) DoChase(m);
            if (GameOver || !MonstersOnLevel.Contains(m)) return;
            if (m.Has(MF.ISHASTE)) DoChase(m);
            m.TTurn = !m.TTurn;
        }

        Coord DestOf(Monster m) => m.DestIsHero || !m.DestObj.HasValue ? Hero : m.DestObj.Value;

        void DoChase(Monster m)
        {
            // Greedy monster whose room gold vanished goes for the hero.
            if (m.Has(MF.ISGREED) && !m.DestIsHero)
            {
                var rp = RoomIn(m.Pos);
                if (rp != null && rp.GoldVal == 0) { m.DestObj = null; m.DestIsHero = true; }
            }
            var dest = DestOf(m);
            var mroom = RoomIn(m.Pos) ?? PassageAt(m.Pos);
            var droom = RoomIn(dest) ?? PassageAt(dest);
            Coord target = dest;
            if (mroom != null && mroom != droom && mroom.Exits.Count > 0)
            {
                // Head for the room exit closest to the destination.
                int best = int.MaxValue;
                foreach (var e in mroom.Exits)
                {
                    int d = Coord.Dist(e, dest);
                    if (d < best) { best = d; target = e; }
                }
            }
            // Dragon flame.
            if (m.Type == 'D' && mroom != null && mroom == RoomIn(Hero) &&
                (Hero.y == m.Pos.y || Hero.x == m.Pos.x ||
                 Math.Abs(Hero.y - m.Pos.y) == Math.Abs(Hero.x - m.Pos.x)) &&
                Coord.Dist(m.Pos, Hero) <= Const.BOLT_LENGTH * Const.BOLT_LENGTH &&
                !m.Has(MF.ISCANC) && Rnd.Next(Const.DRAGONSHOT) == 0)
            {
                var delta = new Coord(Math.Sign(Hero.y - m.Pos.y), Math.Sign(Hero.x - m.Pos.x));
                StopRunning();
                FireBolt(m.Pos, delta, "flame", fromPlayer: false);
                return;
            }

            bool arrived = !ChaseStep(m, target, out Coord step);
            if (arrived)
            {
                if (step == Hero || target == Hero)
                {
                    if (DestOf(m) == Hero) { Attack(m); return; }
                }
                if (!m.DestIsHero && m.DestObj.HasValue && m.Pos == m.DestObj.Value)
                {
                    var obj = ObjectAt(m.Pos);
                    if (obj != null)
                    {
                        ObjectsOnLevel.Remove(obj);
                        m.Pack.Add(obj);
                        Map[obj.Pos.y, obj.Pos.x] = RestoreFloorChar(obj.Pos);
                        FindDest(m);
                    }
                    if (m.Type != 'F') m.Clear(MF.ISRUN);
                }
                return;
            }
            if (m.Type == 'F') return; // flytraps never move
            if (step == Hero) { Attack(m); return; }
            var occupant = MonsterAt(step);
            if (occupant != null && occupant != m) return;
            var oldRoom = RoomIn(m.Pos);
            m.Pos = step;
            if (RoomIn(m.Pos) != oldRoom) FindDest(m);
        }

        /// <summary>chase(): pick the neighbor square closest to the target.
        /// Returns false when the monster is effectively there (attacking).</summary>
        bool ChaseStep(Monster m, Coord ee, out Coord step)
        {
            step = m.Pos;
            bool confusedMove =
                (m.Has(MF.ISHUH) && Rnd.Next(5) != 0) ||
                (m.Type == 'P' && Rnd.Next(5) == 0) ||
                (m.Type == 'B' && Rnd.Next(2) == 0);
            if (confusedMove)
            {
                step = RndMonsterMove(m);
                if (m.Has(MF.ISHUH) && Rnd.Next(20) == 0) m.Clear(MF.ISHUH);
            }
            else
            {
                int best = int.MaxValue, cnt = 0;
                for (int y = m.Pos.y - 1; y <= m.Pos.y + 1; y++)
                    for (int x = m.Pos.x - 1; x <= m.Pos.x + 1; x++)
                    {
                        if (y <= 0 || y >= Const.NUMLINES - 1 || x < 0 || x >= Const.NUMCOLS) continue;
                        var c = new Coord(y, x);
                        if (c == m.Pos) continue;
                        if (!DiagOk(m.Pos, c)) continue;
                        char ch = Map[y, x];
                        if (!StepOk(ch) && c != Hero) continue;
                        var t = ObjectAt(c);
                        if (t != null && t.Kind == ThingKind.Scroll && t.Which == Items.S_SCARE) continue;
                        var occ = MonsterAt(c);
                        if (occ != null) continue;
                        int d = Coord.Dist(c, ee);
                        if (d < best) { best = d; step = c; cnt = 1; }
                        else if (d == best && Rnd.Next(++cnt) == 0) step = c;
                    }
            }
            int curdist = Coord.Dist(m.Pos, ee);
            return curdist != 0 && step != Hero;
        }

        Coord RndMonsterMove(Monster m)
        {
            var c = new Coord(m.Pos.y + Rnd.Next(3) - 1, m.Pos.x + Rnd.Next(3) - 1);
            if (c.y <= 0 || c.y >= Const.NUMLINES - 1 || c.x < 0 || c.x >= Const.NUMCOLS) return m.Pos;
            if (c == m.Pos) return m.Pos;
            if (!DiagOk(m.Pos, c)) return m.Pos;
            if (c == Hero) return c;
            if (!StepOk(Map[c.y, c.x])) return m.Pos;
            if (MonsterAt(c) != null) return m.Pos;
            var t = ObjectAt(c);
            if (t != null && t.Kind == ThingKind.Scroll && t.Which == Items.S_SCARE) return m.Pos;
            return c;
        }

        // ---- Waking and destinations (monsters.c) ----

        public void WakeMonster(Monster m)
        {
            if (!m.Has(MF.ISRUN) && Rnd.Next(3) != 0 && m.Has(MF.ISMEAN) &&
                !m.Has(MF.ISHELD) && !WearingRing(Items.R_STEALTH) && !HasP(MF.ISLEVIT))
            {
                m.DestObj = null; m.DestIsHero = true;
                m.Set(MF.ISRUN);
            }
            if (m.Type == 'M' && !HasP(MF.ISBLIND) && !HasP(MF.ISHALU) &&
                !m.Has(MF.ISFOUND) && !m.Has(MF.ISCANC) && m.Has(MF.ISRUN))
            {
                var rp = RoomIn(Hero);
                if ((rp != null && !rp.IsDark) || Coord.Dist(m.Pos, Hero) < Const.LAMPDIST)
                {
                    m.Set(MF.ISFOUND);
                    if (!Save(Const.VS_MAGIC))
                    {
                        if (HasP(MF.ISHUH)) Sched.Lengthen("unconfuse", Rnd.Spread(Const.HUHDURATION));
                        else
                        {
                            SetP(MF.ISHUH);
                            Sched.Fuse("unconfuse", Unconfuse, Rnd.Spread(Const.HUHDURATION));
                        }
                        Msg("the medusa's gaze has confused you");
                    }
                }
            }
            if (m.Has(MF.ISGREED) && !m.Has(MF.ISRUN))
            {
                m.Set(MF.ISRUN);
                var rp = RoomIn(m.Pos);
                if (rp != null && rp.GoldVal > 0) { m.DestObj = rp.GoldPos; m.DestIsHero = false; }
                else { m.DestObj = null; m.DestIsHero = true; }
            }
        }

        public void RunTo(Monster m)
        {
            m.Set(MF.ISRUN);
            m.Clear(MF.ISHELD);
            FindDest(m);
        }

        public void FindDest(Monster m)
        {
            int carry = Monsters.Info(m.Type).Carry;
            var mroom = RoomIn(m.Pos);
            if (carry <= 0 || (mroom != null && mroom == RoomIn(Hero)) || CanSeeMonster(m))
            { m.DestObj = null; m.DestIsHero = true; return; }
            foreach (var obj in ObjectsOnLevel)
            {
                if (obj.Kind == ThingKind.Scroll && obj.Which == Items.S_SCARE) continue;
                if (RoomIn(obj.Pos) != mroom) continue;
                if (Rnd.Next(100) >= carry) continue;
                bool taken = false;
                foreach (var other in MonstersOnLevel)
                    if (other != m && other.DestObj.HasValue && other.DestObj.Value == obj.Pos)
                        taken = true;
                if (taken) continue;
                m.DestObj = obj.Pos;
                m.DestIsHero = false;
                return;
            }
            m.DestObj = null; m.DestIsHero = true;
        }

        public void Aggravate()
        {
            foreach (var m in MonstersOnLevel) RunTo(m);
        }

        /// <summary>wanderer(): spawn a wandering monster off-room.</summary>
        public void Wanderer()
        {
            Coord c;
            int guard = 200;
            do { c = FindFloor(null, 0, true); }
            while (RoomIn(c) == RoomIn(Hero) && guard-- > 0);
            var m = Monsters.New(Monsters.RandMonster(true, LevelNum), c, LevelNum);
            MonstersOnLevel.Add(m);
            RunTo(m);
        }
    }
}
