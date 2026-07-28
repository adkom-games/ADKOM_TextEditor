// ATE Rogue 5.4.4 port — movement, traps, search, stairs, pickup
// (move.c, misc.c, new_level.c be_trapped).
using System;
using UnityEngine;

namespace AteRogue
{
    public partial class Game
    {
        public static readonly string[] TrapNames =
        {
            "a trapdoor", "an arrow trap", "a sleeping gas trap", "a beartrap",
            "a teleport trap", "a poison dart trap", "a rust trap", "a mysterious trap"
        };

        /// <summary>do_move(): one step. Consumes the turn unless blocked.</summary>
        public void DoMove(Coord delta, bool running)
        {
            FirstMove = false;
            if (NoMove > 0)
            {
                NoMove--;
                Msg("you are still stuck in the bear trap");
                OneTurn(true);
                return;
            }
            if (HasP(MF.ISHUH) && Rnd.Next(5) != 0)
            {
                delta = RndMoveDelta();
                if (delta.y == 0 && delta.x == 0)
                {
                    StopRunning();
                    OneTurn(true);
                    return;
                }
            }
            var nh = new Coord(Hero.y + delta.y, Hero.x + delta.x);
            if (nh.x < 0 || nh.x >= Const.NUMCOLS || nh.y <= 0 || nh.y >= Const.NUMLINES - 1)
            { StopRunning(); return; }
            if (!DiagOk(Hero, nh)) { StopRunning(); return; }

            // Reveal a hidden trap under the destination.
            if (!FReal[nh.y, nh.x] && Map[nh.y, nh.x] == Const.FLOOR && !HasP(MF.ISLEVIT))
            {
                Map[nh.y, nh.x] = Const.TRAP;
                FReal[nh.y, nh.x] = true;
            }

            var monster = MonsterAt(nh);
            if (HasP(MF.ISHELD) && (monster == null || monster.Type != 'F'))
            {
                Msg("you are being held");
                return;
            }

            char ch = Map[nh.y, nh.x];
            if (monster != null)
            {
                StopRunning();
                bool wasted = Fight(monster, CurWeapon, thrown: false);
                OneTurn(true);
                return;
            }
            switch (ch)
            {
                case ' ':
                case Const.WALL_H:
                case Const.WALL_V:
                    StopRunning();
                    return; // no turn
                case Const.DOOR:
                    StopRunning();
                    if (FPass[Hero.y, Hero.x]) MoveHero(nh, entering: true);
                    else MoveHero(nh, entering: false);
                    break;
                case Const.TRAP:
                {
                    int result = BeTrapped(nh);
                    if (result == T_DOOR || result == T_TELEP) { OneTurn(true); return; }
                    MoveHero(nh, entering: false);
                    break;
                }
                case Const.PASSAGE:
                    MoveHero(nh, entering: false);
                    break;
                case Const.STAIRS:
                    SeenStairs = true;
                    MoveHero(nh, entering: false);
                    break;
                default:
                    MoveHero(nh, entering: false);
                    var obj = ObjectAt(nh);
                    if (obj != null && !HasP(MF.ISLEVIT)) PickUp(obj);
                    else if (obj != null) Msg("you are floating above {0}", Items.InvName(this, obj, true));
                    StopIfInteresting(ch);
                    break;
            }
            OneTurn(true);
        }

        void StopIfInteresting(char ch)
        {
            if (ch != Const.FLOOR && ch != Const.PASSAGE) StopRunning();
        }

        void MoveHero(Coord nh, bool entering)
        {
            bool wasDoor = Map[Hero.y, Hero.x] == Const.DOOR;
            var oldRoom = RoomIn(Hero);
            Hero = nh;
            var newRoom = RoomIn(Hero);
            if (entering || (newRoom != null && newRoom != oldRoom)) EnterRoom(Hero);
            if (newRoom == null && (wasDoor || oldRoom != null)) LeaveRoom(Hero);
            if (newRoom == null) CurRoom = PassageAt(Hero);
        }

        Coord RndMoveDelta()
        {
            var d = new Coord(Rnd.Next(3) - 1, Rnd.Next(3) - 1);
            var nh = new Coord(Hero.y + d.y, Hero.x + d.x);
            if (nh.y <= 0 || nh.y >= Const.NUMLINES - 1 || nh.x < 0 || nh.x >= Const.NUMCOLS)
                return new Coord(0, 0);
            if (!DiagOk(Hero, nh) || !StepOk(Map[nh.y, nh.x])) return new Coord(0, 0);
            var t = ObjectAt(nh);
            if (t != null && t.Kind == ThingKind.Scroll && t.Which == Items.S_SCARE)
                return new Coord(0, 0);
            return d;
        }

        /// <summary>diag_ok(): no corner cutting.</summary>
        public bool DiagOk(Coord sp, Coord ep)
        {
            if (ep.x < 0 || ep.x >= Const.NUMCOLS || ep.y <= 0 || ep.y >= Const.NUMLINES - 1)
                return false;
            if (ep.y == sp.y || ep.x == sp.x) return true;
            return StepOk(Map[ep.y, sp.x]) && StepOk(Map[sp.y, ep.x]);
        }

        // ---- Traps (move.c be_trapped) ----

        public int BeTrapped(Coord tc)
        {
            if (HasP(MF.ISLEVIT)) return T_RUST; // floats over it
            StopRunning();
            Map[tc.y, tc.x] = Const.TRAP;
            FReal[tc.y, tc.x] = true;
            FSeen[tc.y, tc.x] = true;
            int tr = TrapType[tc.y, tc.x];
            switch (tr)
            {
                case T_DOOR:
                    LevelNum++;
                    Hero = tc;
                    NewLevel();
                    Msg("you fell into a trap!");
                    break;
                case T_ARROW:
                    if (SwingAtPlayer(Player.Lvl - 1, 1))
                    {
                        Player.Hpt -= Rnd.Roll(1, 6);
                        if (Player.Hpt <= 0) { Msg("an arrow killed you"); Death('a'); return tr; }
                        Msg("oh no! An arrow shot you");
                    }
                    else
                    {
                        var arrow = new Thing { Known = true };
                        Items.InitWeapon(arrow, Items.ARROW, ref GroupCounter);
                        arrow.Count = 1;
                        FallAt(arrow, Hero);
                        Msg("an arrow shoots past you");
                    }
                    break;
                case T_SLEEP:
                    NoCommand += Rnd.Spread(5);
                    ClearP(MF.ISRUN);
                    Msg("a strange white mist envelops you and you fall asleep");
                    break;
                case T_BEAR:
                    NoMove += Rnd.Spread(3);
                    Msg("you are caught in a bear trap");
                    break;
                case T_TELEP:
                    Hero = tc;
                    Teleport();
                    break;
                case T_DART:
                    if (SwingAtPlayer(Player.Lvl + 1, 1))
                    {
                        Player.Hpt -= Rnd.Roll(1, 4);
                        if (Player.Hpt <= 0) { Msg("a poisoned dart killed you"); Death('d'); return tr; }
                        if (!WearingRing(Items.R_SUSTSTR) && !Save(Const.VS_POISON)) ChgStr(-1);
                        Msg("a small dart just hit you in the shoulder");
                    }
                    else Msg("a small dart whizzes by your ear and vanishes");
                    break;
                case T_RUST:
                    Msg("a gush of water hits you on the head");
                    RustArmor();
                    break;
                case T_MYST:
                    switch (Rnd.Next(11))
                    {
                        case 0: Msg("you are suddenly in a parallel dimension"); break;
                        case 1: Msg("the light in here seems {0}", Items.Rainbow[Rnd.Next(Items.Rainbow.Length)]); break;
                        case 2: Msg("you feel a sting in the side of your neck"); break;
                        case 3: Msg("multi-colored lines swirl around you, then fade"); break;
                        case 4: Msg("a {0} light flashes in your eyes", Items.Rainbow[Rnd.Next(Items.Rainbow.Length)]); break;
                        case 5: Msg("a spike shoots past your ear!"); break;
                        case 6: Msg("{0} sparks dance across your armor", Items.Rainbow[Rnd.Next(Items.Rainbow.Length)]); break;
                        case 7: Msg("you suddenly feel very thirsty"); break;
                        case 8: Msg("you feel time speed up suddenly"); break;
                        case 9: Msg("time now seems to be going slower"); break;
                        default: Msg("your pack turns {0}!", Items.Rainbow[Rnd.Next(Items.Rainbow.Length)]); break;
                    }
                    break;
            }
            return tr;
        }

        bool SwingAtPlayer(int atLvl, int wplus)
        {
            int arm = CurArmor != null ? CurArmor.Arm : Player.Arm;
            int res = Rnd.Next(20);
            int need = (20 - atLvl) - arm;
            return res + wplus >= need;
        }

        public void RustArmor()
        {
            if (CurArmor == null || CurArmor.Kind != ThingKind.Armor) return;
            if (CurArmor.Which == Items.A_LEATHER || CurArmor.Arm >= 9) return;
            if (CurArmor.IsProtected || WearingRing(Items.R_SUSTARM))
            { if (!ToDeath) Msg("the rust vanishes instantly"); }
            else
            {
                CurArmor.Arm++;
                Msg(Terse ? "your armor weakens" : "your armor appears to be weaker now. Oh my!");
            }
        }

        /// <summary>fall(): drop an item near a position or destroy it.</summary>
        public void FallAt(Thing t, Coord near)
        {
            int cnt = 0;
            Coord pick = default;
            bool found = false;
            for (int y = near.y - 1; y <= near.y + 1; y++)
                for (int x = near.x - 1; x <= near.x + 1; x++)
                {
                    if (y <= 0 || y >= Const.NUMLINES - 1 || x < 0 || x >= Const.NUMCOLS) continue;
                    var c = new Coord(y, x);
                    if (c == Hero) continue;
                    char ch = Map[y, x];
                    if ((ch == Const.FLOOR || ch == Const.PASSAGE) && ObjectAt(c) == null)
                        if (Rnd.Next(++cnt) == 0) { pick = c; found = true; }
                }
            if (!found)
            {
                Msg("the {0} vanishes as it hits the ground",
                    t.Kind == ThingKind.Weapon ? Items.WeaponName(t) : "object");
                return;
            }
            t.Pos = pick;
            ObjectsOnLevel.Add(t);
            Discover(pick);
        }

        // ---- Teleport / search / stairs / pickup ----

        public void Teleport()
        {
            var rp = RndRoom();
            var c = FindFloor(rp, 0, true);
            var oldRoom = CurRoom;
            Hero = c;
            EnterRoom(Hero);
            if (RoomIn(Hero) == null) CurRoom = PassageAt(Hero);
            StopRunning();
            ClearP(MF.ISHELD);
            if (HasP(MF.ISHUH)) Sched.Lengthen("unconfuse", 2);
            Look(false);
        }

        public void Search(bool silent)
        {
            int probinc = (HasP(MF.ISHALU) ? 3 : 0) + (HasP(MF.ISBLIND) ? 2 : 0);
            bool found = false;
            for (int y = Hero.y - 1; y <= Hero.y + 1; y++)
                for (int x = Hero.x - 1; x <= Hero.x + 1; x++)
                {
                    if (y <= 0 || y >= Const.NUMLINES - 1 || x < 0 || x >= Const.NUMCOLS) continue;
                    if (FReal[y, x]) continue;
                    char ch = Map[y, x];
                    if (ch == Const.WALL_H || ch == Const.WALL_V)
                    {
                        if (Rnd.Next(5 + probinc) == 0)
                        {
                            Map[y, x] = Const.DOOR;
                            FReal[y, x] = true;
                            found = true;
                            if (!silent) Msg("a secret door");
                        }
                    }
                    else if (ch == Const.FLOOR)
                    {
                        if (Rnd.Next(2 + probinc) == 0)
                        {
                            Map[y, x] = Const.TRAP;
                            FReal[y, x] = true;
                            found = true;
                            if (!silent) Msg(HasP(MF.ISHALU)
                                ? "you found " + TrapNames[Rnd.Next(NTRAPS)]
                                : "you found " + TrapNames[TrapType[y, x]]);
                        }
                    }
                    else if (FPass[y, x])
                    {
                        if (Rnd.Next(3 + probinc) == 0)
                        {
                            Map[y, x] = Const.PASSAGE;
                            FReal[y, x] = true;
                            found = true;
                            if (!silent) Msg("a secret passage");
                        }
                    }
                    if (found) Discover(new Coord(y, x));
                }
            if (found) { StopRunning(); Look(false); }
        }

        public void DownLevel()
        {
            if (HasP(MF.ISLEVIT)) { Msg("You can't.  You're floating off the ground!"); return; }
            if (Map[Hero.y, Hero.x] != Const.STAIRS) { Msg("I see no way down"); return; }
            LevelNum++;
            SeenStairs = false;
            NewLevel();
            OneTurn(true);
        }

        public void UpLevel()
        {
            if (HasP(MF.ISLEVIT)) { Msg("You can't.  You're floating off the ground!"); return; }
            if (Map[Hero.y, Hero.x] != Const.STAIRS) { Msg("I see no way up"); return; }
            if (!HasAmulet) { Msg("your way is magically blocked"); return; }
            LevelNum--;
            if (LevelNum == 0) { TotalWinner(); return; }
            NewLevel();
            Msg("you feel a wrenching sensation in your gut");
            OneTurn(true);
        }

        public void PickUp(Thing t)
        {
            if (t.Kind == ThingKind.Gold)
            {
                ObjectsOnLevel.Remove(t);
                Purse += t.GoldVal;
                Map[t.Pos.y, t.Pos.x] = RestoreFloorChar(t.Pos);
                var rp = RoomIn(t.Pos);
                if (rp != null && rp.GoldPos == t.Pos) rp.GoldVal = 0;
                Msg("you found {0} gold pieces", t.GoldVal);
                RetargetMonsters(t.Pos);
                return;
            }
            bool wasScareOnFloor = t.Kind == ThingKind.Scroll && t.Which == Items.S_SCARE && t.ScareFloor;
            if (AddToPack(t, silent: false) || wasScareOnFloor)
            {
                ObjectsOnLevel.Remove(t);
                Map[t.Pos.y, t.Pos.x] = RestoreFloorChar(t.Pos);
                RetargetMonsters(t.Pos);
            }
        }

        char RestoreFloorChar(Coord c) => FPass[c.y, c.x] ? Const.PASSAGE : Const.FLOOR;

        void RetargetMonsters(Coord objPos)
        {
            foreach (var m in MonstersOnLevel)
                if (m.DestObj.HasValue && m.DestObj.Value == objPos)
                { m.DestObj = null; m.DestIsHero = true; }
        }
    }
}
