// ATE Rogue 5.4.4 port — potions, scrolls, sticks, ring effects, and the
// daemons (potions.c, scrolls.c, sticks.c, rings.c, daemons.c).
using System;
using System.Collections.Generic;

namespace AteRogue
{
    public partial class Game
    {
        // ---- Daemons (daemons.c) ----

        public void Doctor()
        {
            Quiet++;
            int heal = 0;
            if (Player.Lvl < 8)
            {
                if (Quiet + Player.Lvl * 2 > 20) heal = 1;
            }
            else if (Quiet >= 3) heal = Rnd.Next(Player.Lvl - 7) + 1;
            if (LeftRing != null && LeftRing.Which == Items.R_REGEN) heal++;
            if (RightRing != null && RightRing.Which == Items.R_REGEN) heal++;
            if (heal > 0 && Player.Hpt < Player.MaxHp)
            {
                Player.Hpt = Math.Min(Player.MaxHp, Player.Hpt + heal);
                Quiet = 0;
            }
        }

        int _rollwandCalls;
        bool _rollwandActive;
        bool _rollwandDaemonAdded;

        void StartWanderer()
        {
            _rollwandActive = true;
            if (!_rollwandDaemonAdded)
            {
                _rollwandDaemonAdded = true;
                Sched.Daemon("rollwand", RollWand);
            }
        }

        void RollWand()
        {
            if (!_rollwandActive) return;
            if (++_rollwandCalls < 4) return;
            _rollwandCalls = 0;
            if (Rnd.Roll(1, 6) == 4)
            {
                Wanderer();
                // rollwand retires until the next swander fuse re-arms it.
                _rollwandActive = false;
                Sched.Fuse("swander", StartWanderer, Rnd.Spread(70));
            }
        }

        public void Stomach()
        {
            if (FoodLeft <= 0)
            {
                FoodLeft--;
                if (FoodLeft < -Const.STARVETIME) { Death('s'); return; }
                if (NoCommand == 0 && Rnd.Next(5) == 0)
                {
                    NoCommand += Rnd.Next(8) + 4;
                    HungryState = 3;
                    ClearP(MF.ISRUN);
                    Msg(HasP(MF.ISHALU)
                        ? "the munchies overpower your motor capabilities.  You freak out"
                        : Terse ? "you faint" : "you feel too weak from lack of food.  You faint");
                    StopRunning();
                }
                return;
            }
            int oldFood = FoodLeft;
            FoodLeft -= RingEat(LeftRing) + RingEat(RightRing) + 1 - (HasAmulet ? 1 : 0);
            if (FoodLeft < Const.MORETIME && oldFood >= Const.MORETIME)
            {
                HungryState = 2;
                Msg(HasP(MF.ISHALU) ? "the munchies are interfering with your motor capabilites"
                    : "you are starting to feel weak");
                StopRunning();
            }
            else if (FoodLeft < 2 * Const.MORETIME && oldFood >= 2 * Const.MORETIME)
            {
                HungryState = 1;
                Msg(HasP(MF.ISHALU) ? "you are getting the munchies"
                    : Terse ? "getting hungry" : "you are starting to get hungry");
                StopRunning();
            }
        }

        static readonly int[] RingUses =
        { 1, 1, 1, -3, -5, 0, 0, -3, -3, 2, -2, 0, 1, 1 };

        int RingEat(Thing ring)
        {
            if (ring == null) return 0;
            int use = RingUses[ring.Which];
            int eat;
            if (use >= 0) eat = use;
            else eat = Rnd.Next(-use) == 0 ? 1 : 0;
            if (ring.Which == Items.R_DIGEST) eat = -eat;
            return eat;
        }

        // ---- Fuse effects ----

        public void Unconfuse()
        {
            ClearP(MF.ISHUH);
            Msg(HasP(MF.ISHALU) ? "you feel less trippy now" : "you feel less confused now");
        }

        public void Sight()
        {
            if (!HasP(MF.ISBLIND)) return;
            Sched.Extinguish("sight");
            ClearP(MF.ISBLIND);
            EnterRoom(Hero);
            Look(false);
            Msg(HasP(MF.ISHALU) ? "far out!  Everything is all cosmic again"
                : "the veil of darkness lifts");
        }

        public void Unsee()
        {
            ClearP(MF.CANSEE);
        }

        public void NoHaste()
        {
            ClearP(MF.ISHASTE);
            Msg("you feel yourself slowing down");
        }

        public void ComeDown()
        {
            if (!HasP(MF.ISHALU)) return;
            Sched.Extinguish("come_down");
            ClearP(MF.ISHALU);
            Look(false);
            Msg("Everything looks SO boring now.");
        }

        public void Land()
        {
            ClearP(MF.ISLEVIT);
            Msg(HasP(MF.ISHALU) ? "bummer!  You've hit the ground" : "you float gently to the ground");
        }

        void DoPot(MF flag, string fuseName, Action fuse, int baseTime)
        {
            int t = Rnd.Spread(baseTime);
            if (!HasP(flag))
            {
                SetP(flag);
                Sched.Fuse(fuseName, fuse, t);
                Look(false);
            }
            else Sched.Lengthen(fuseName, t);
        }

        // ---- Potions (potions.c quaff) ----

        public void Quaff(Thing t)
        {
            int which = t.Which;
            bool trip = HasP(MF.ISHALU);
            RemoveOne(t);
            bool know = false;
            switch (which)
            {
                case Items.P_CONFUSE:
                    DoPot(MF.ISHUH, "unconfuse", Unconfuse, Const.HUHDURATION);
                    Msg(trip ? "what a tripy feeling!"
                        : "wait, what's going on here. Huh? What? Who?");
                    know = !trip;
                    break;
                case Items.P_LSD:
                    if (!trip) SeenStairs = StairsSeen();
                    DoPot(MF.ISHALU, "come_down", ComeDown, Const.SEEDURATION);
                    Msg("Oh, wow!  Everything seems so cosmic!");
                    know = true;
                    break;
                case Items.P_POISON:
                    know = true;
                    if (WearingRing(Items.R_SUSTSTR)) Msg("you feel momentarily sick");
                    else
                    {
                        ChgStr(-(Rnd.Next(3) + 1));
                        Msg("you feel very sick now");
                        ComeDown();
                    }
                    break;
                case Items.P_STRENGTH:
                    know = true;
                    ChgStr(1);
                    Msg("you feel stronger, now.  What bulging muscles!");
                    break;
                case Items.P_SEEINVIS:
                    Msg("this potion tastes like {0} juice", Fruit);
                    if (!HasP(MF.CANSEE))
                    {
                        SetP(MF.CANSEE);
                        Sched.Fuse("unsee", Unsee, Rnd.Spread(Const.SEEDURATION));
                    }
                    else Sched.Lengthen("unsee", Rnd.Spread(Const.SEEDURATION));
                    Sight();
                    break;
                case Items.P_HEALING:
                    know = true;
                    Player.Hpt += Rnd.Roll(Player.Lvl, 4);
                    if (Player.Hpt > Player.MaxHp) Player.Hpt = ++Player.MaxHp;
                    Sight();
                    Msg("you begin to feel better");
                    break;
                case Items.P_XHEAL:
                    know = true;
                    Player.Hpt += Rnd.Roll(Player.Lvl, 8);
                    if (Player.Hpt > Player.MaxHp)
                    {
                        if (Player.Hpt > Player.MaxHp + Player.Lvl + 1) Player.MaxHp++;
                        Player.Hpt = ++Player.MaxHp;
                    }
                    Sight();
                    ComeDown();
                    Msg("you begin to feel much better");
                    break;
                case Items.P_MFIND:
                    SetP(MF.SEEMONST);
                    Sched.Fuse("turnsee", () => ClearP(MF.SEEMONST), Const.HUHDURATION);
                    if (MonstersOnLevel.Count > 0) know = true;
                    else Msg("you have a {0} feeling for a moment, then it passes",
                        trip ? "normal" : "strange");
                    break;
                case Items.P_TFIND:
                {
                    bool found = false;
                    foreach (var obj in ObjectsOnLevel) if (IsMagicItem(obj)) found = true;
                    foreach (var m in MonstersOnLevel)
                        foreach (var obj in m.Pack) if (IsMagicItem(obj)) found = true;
                    if (found)
                    {
                        know = true;
                        Msg("You sense the presence of magic on this level.");
                    }
                    else Msg("you have a {0} feeling for a moment, then it passes",
                        trip ? "normal" : "strange");
                    break;
                }
                case Items.P_RAISE:
                    know = true;
                    Msg("you suddenly feel much more skillful");
                    RaiseLevel();
                    break;
                case Items.P_HASTE:
                    know = true;
                    if (HasP(MF.ISHASTE))
                    {
                        NoCommand += Rnd.Next(8);
                        ClearP(MF.ISRUN | MF.ISHASTE);
                        Sched.Extinguish("nohaste");
                        Msg("you faint from exhaustion");
                    }
                    else
                    {
                        SetP(MF.ISHASTE);
                        Sched.Fuse("nohaste", NoHaste, Rnd.Next(4) + 4);
                        Msg("you feel yourself moving much faster");
                    }
                    break;
                case Items.P_RESTORE:
                {
                    int add = RingStrBonus();
                    Player.Str -= add;
                    if (Player.Str < MaxStats.Str) Player.Str = MaxStats.Str;
                    Player.Str += add;
                    Msg("hey, this tastes great.  It make you feel warm all over");
                    break;
                }
                case Items.P_BLIND:
                    if (!HasP(MF.ISBLIND))
                    {
                        SetP(MF.ISBLIND);
                        Sched.Fuse("sight", Sight, Rnd.Spread(Const.SEEDURATION));
                    }
                    else Sched.Lengthen("sight", Rnd.Spread(Const.SEEDURATION));
                    Msg(trip ? "oh, bummer!  Everything is dark!  Help!"
                        : "a cloak of darkness falls around you");
                    know = true;
                    break;
                case Items.P_LEVIT:
                    DoPot(MF.ISLEVIT, "land", Land, 30);
                    Msg(trip ? "oh, wow!  You're floating in the air!"
                        : "you start to float in the air");
                    know = true;
                    break;
            }
            if (know) PotionKnown[which] = true;
            else if (!PotionKnown[which]) CallIt(ThingKind.Potion, which);
            UpdateStatus();
        }

        bool StairsSeen() => FSeen[Stairs.y, Stairs.x];

        bool IsMagicItem(Thing t) => t.Kind switch
        {
            ThingKind.Armor => t.IsProtected || t.Arm != Items.ArmorClass[t.Which],
            ThingKind.Weapon => t.HPlus != 0 || t.DPlus != 0,
            ThingKind.Potion or ThingKind.Scroll or ThingKind.Stick or
            ThingKind.Ring or ThingKind.Amulet => true,
            _ => false
        };

        // ---- Scrolls (scrolls.c read_scroll) ----

        public void ReadScroll(Thing t)
        {
            int which = t.Which;
            RemoveOne(t);
            bool know = false;
            switch (which)
            {
                case Items.S_CONFUSE:
                    SetP(MF.CANHUH);
                    Msg("your hands begin to glow {0}",
                        HasP(MF.ISHALU) ? Items.Rainbow[Rnd.Next(Items.Rainbow.Length)] : "red");
                    break;
                case Items.S_MAP:
                    know = true;
                    Msg("oh, now this scroll has a map on it");
                    MagicMap();
                    break;
                case Items.S_HOLD:
                {
                    int held = 0;
                    foreach (var m in MonstersOnLevel)
                        if (Math.Abs(m.Pos.y - Hero.y) <= 2 && Math.Abs(m.Pos.x - Hero.x) <= 2
                            && m.Has(MF.ISRUN))
                        { m.Clear(MF.ISRUN); m.Set(MF.ISHELD); held++; }
                    if (held > 0)
                    {
                        know = true;
                        Msg(held == 1 ? "the monster freezes" : "the monsters around you freeze");
                    }
                    else Msg("you feel a strange sense of loss");
                    break;
                }
                case Items.S_SLEEP:
                    know = true;
                    NoCommand += Rnd.Next(Rnd.Spread(5)) + 4;
                    ClearP(MF.ISRUN);
                    Msg("you fall asleep");
                    break;
                case Items.S_ARMOR:
                    if (CurArmor != null)
                    {
                        CurArmor.Arm--;
                        CurArmor.Cursed = false;
                        Msg("your armor glows {0} for a moment",
                            HasP(MF.ISHALU) ? Items.Rainbow[Rnd.Next(Items.Rainbow.Length)] : "silver");
                    }
                    break;
                case Items.S_ID_POTION: case Items.S_ID_SCROLL:
                case Items.S_ID_WEAPON: case Items.S_ID_ARMOR: case Items.S_ID_R_OR_S:
                    know = true;
                    ScrollKnown[which] = true;
                    Msg("this scroll is an identify scroll");
                    IdentifyByScroll(which);
                    return; // call_it after selection completes
                case Items.S_SCARE:
                    Msg("you hear maniacal laughter in the distance");
                    break;
                case Items.S_FDET:
                {
                    bool any = false;
                    foreach (var obj in ObjectsOnLevel)
                        if (obj.Kind == ThingKind.Food) { any = true; Discover(obj.Pos); }
                    if (any) { know = true; Msg("Your nose tingles and you smell food."); }
                    else Msg("your nose tingles");
                    break;
                }
                case Items.S_TELEP:
                {
                    var before = CurRoom;
                    Teleport();
                    if (CurRoom != before) know = true;
                    break;
                }
                case Items.S_ENCH:
                    if (CurWeapon == null) Msg("you feel a strange sense of loss");
                    else
                    {
                        CurWeapon.Cursed = false;
                        if (Rnd.Next(2) == 0) CurWeapon.HPlus++;
                        else CurWeapon.DPlus++;
                        Msg("your {0} glows {1} for a moment", Items.WeaponName(CurWeapon),
                            HasP(MF.ISHALU) ? Items.Rainbow[Rnd.Next(Items.Rainbow.Length)] : "blue");
                    }
                    break;
                case Items.S_CREATE:
                {
                    int cnt = 0;
                    Coord pick = default;
                    for (int y = Hero.y - 1; y <= Hero.y + 1; y++)
                        for (int x = Hero.x - 1; x <= Hero.x + 1; x++)
                        {
                            if (y <= 0 || y >= Const.NUMLINES - 1 || x < 0 || x >= Const.NUMCOLS) continue;
                            var c = new Coord(y, x);
                            if (c == Hero || !StepOk(Map[y, x]) || MonsterAt(c) != null) continue;
                            var obj = ObjectAt(c);
                            if (obj != null && obj.Kind == ThingKind.Scroll && obj.Which == Items.S_SCARE) continue;
                            if (Rnd.Next(++cnt) == 0) pick = c;
                        }
                    if (cnt == 0) Msg("you hear a faint cry of anguish in the distance");
                    else
                    {
                        var m = Monsters.New(Monsters.RandMonster(false, LevelNum), pick, LevelNum);
                        MonstersOnLevel.Add(m);
                        RunTo(m);
                    }
                    break;
                }
                case Items.S_REMOVE:
                    if (CurArmor != null) CurArmor.Cursed = false;
                    if (CurWeapon != null) CurWeapon.Cursed = false;
                    if (LeftRing != null) LeftRing.Cursed = false;
                    if (RightRing != null) RightRing.Cursed = false;
                    Msg(HasP(MF.ISHALU) ? "you feel in touch with the Universal Onenes"
                        : "you feel as if somebody is watching over you");
                    break;
                case Items.S_AGGR:
                    Aggravate();
                    Msg("you hear a high pitched humming noise");
                    break;
                case Items.S_PROTECT:
                    if (CurArmor != null)
                    {
                        CurArmor.IsProtected = true;
                        Msg("your armor is covered by a shimmering {0} shield",
                            HasP(MF.ISHALU) ? Items.Rainbow[Rnd.Next(Items.Rainbow.Length)] : "gold");
                    }
                    else Msg("you feel a strange sense of loss");
                    break;
            }
            if (know) ScrollKnown[which] = true;
            else if (!ScrollKnown[which]) CallIt(ThingKind.Scroll, which);
            Look(true);
            UpdateStatus();
        }

        void MagicMap()
        {
            for (int y = 1; y < Const.STATLINE; y++)
                for (int x = 0; x < Const.NUMCOLS; x++)
                {
                    if (!FReal[y, x])
                    {
                        FReal[y, x] = true;
                        if (FPass[y, x] && Map[y, x] == ' ') Map[y, x] = Const.PASSAGE;
                        else if (Map[y, x] == Const.WALL_H || Map[y, x] == Const.WALL_V)
                            Map[y, x] = Const.DOOR;
                        else if (Map[y, x] == Const.FLOOR) Map[y, x] = Const.TRAP;
                    }
                    char c = Map[y, x];
                    if (c == Const.PASSAGE || c == Const.DOOR || c == Const.WALL_H ||
                        c == Const.WALL_V || c == Const.STAIRS || c == Const.TRAP)
                    {
                        _shown[y, x] = c;
                        FSeen[y, x] = true;
                    }
                }
        }

        void IdentifyByScroll(int scroll)
        {
            Func<Thing, bool> filter = scroll switch
            {
                Items.S_ID_POTION => t => t.Kind == ThingKind.Potion,
                Items.S_ID_SCROLL => t => t.Kind == ThingKind.Scroll,
                Items.S_ID_WEAPON => t => t.Kind == ThingKind.Weapon,
                Items.S_ID_ARMOR => t => t.Kind == ThingKind.Armor,
                _ => t => t.Kind == ThingKind.Ring || t.Kind == ThingKind.Stick
            };
            AskItem("identify", filter, t =>
            {
                switch (t.Kind)
                {
                    case ThingKind.Potion: PotionKnown[t.Which] = true; break;
                    case ThingKind.Scroll: ScrollKnown[t.Which] = true; break;
                    case ThingKind.Ring: RingKnown[t.Which] = true; break;
                    case ThingKind.Stick: StickKnown[t.Which] = true; break;
                }
                t.Known = true;
                Msg(Items.InvName(this, t, false) + " (" + t.PackChar + ")");
                OneTurn(true);
            });
        }

        void CallIt(ThingKind kind, int which)
        {
            // Original prompts after use; keep it optional and quiet: only
            // prompt when the type has no guess yet.
            string[] guesses = kind == ThingKind.Potion ? PotionGuess
                : kind == ThingKind.Scroll ? ScrollGuess
                : kind == ThingKind.Ring ? RingGuess : StickGuess;
            if (guesses[which] != null) return;
            AskText(Terse ? "call it: " : "what do you want to call it? ", s =>
            {
                if (!string.IsNullOrWhiteSpace(s)) guesses[which] = s.Trim();
            });
        }

        void RemoveOne(Thing t)
        {
            if (t.Count > 1) t.Count--;
            else RemoveFromPack(t);
        }

        // ---- Sticks (sticks.c do_zap) ----

        public void Zap(Thing stick, Coord delta)
        {
            if (stick.Charges <= 0) { Msg("nothing happens"); OneTurn(true); return; }
            stick.Charges--;
            int which = stick.Which;
            bool know = false;
            switch (which)
            {
                case Items.WS_LIGHT:
                {
                    know = true;
                    var rp = RoomIn(Hero);
                    if (rp == null) Msg("the corridor glows and then fades");
                    else
                    {
                        rp.Flags &= ~RoomFlags.ISDARK;
                        EnterRoom(Hero);
                        Msg(Terse ? "the room is lit"
                            : "the room is lit by a shimmering {0} light",
                            HasP(MF.ISHALU) ? Items.Rainbow[Rnd.Next(Items.Rainbow.Length)] : "blue");
                    }
                    break;
                }
                case Items.WS_DRAIN:
                {
                    if (Player.Hpt < 2) { Msg("you are too weak to use it"); OneTurn(true); return; }
                    var targets = new List<Monster>();
                    var hroom = RoomIn(Hero) ?? PassageAt(Hero);
                    foreach (var m in MonstersOnLevel)
                        if ((RoomIn(m.Pos) ?? PassageAt(m.Pos)) == hroom) targets.Add(m);
                    if (targets.Count == 0) { Msg("you have a tingling feeling"); break; }
                    Player.Hpt /= 2;
                    int dmg = Math.Max(1, Player.Hpt / targets.Count);
                    foreach (var m in new List<Monster>(targets))
                    {
                        m.Stats.Hpt -= dmg;
                        if (m.Stats.Hpt <= 0) Killed(m, byPlayer: true);
                        else RunTo(m);
                    }
                    break;
                }
                case Items.WS_ELECT: case Items.WS_FIRE: case Items.WS_COLD:
                    know = true;
                    FireBolt(Hero, delta,
                        which == Items.WS_ELECT ? "bolt" : which == Items.WS_FIRE ? "flame" : "ice",
                        fromPlayer: true);
                    break;
                case Items.WS_MISSILE:
                {
                    know = true;
                    var target = RayTarget(delta, out Coord hitPos);
                    if (target == null || SaveThrow(Const.VS_MAGIC, target.Stats.Lvl))
                        Msg(Terse ? "missle vanishes" : "the missle vanishes with a puff of smoke");
                    else
                    {
                        var missile = new Thing { Kind = ThingKind.Weapon, Which = Items.SWORD,
                            Hurl = "1x4", HPlus = 100, DPlus = 1, Launch = -1, Damage = "0x0" };
                        Fight(target, missile, thrown: true);
                    }
                    break;
                }
                case Items.WS_HASTE_M:
                {
                    var target = RayTarget(delta, out _);
                    if (target != null)
                    {
                        if (target.Has(MF.ISSLOW)) target.Clear(MF.ISSLOW);
                        else target.Set(MF.ISHASTE);
                        FreeIfFlytrap(target);
                        RunTo(target);
                    }
                    break;
                }
                case Items.WS_SLOW_M:
                {
                    var target = RayTarget(delta, out _);
                    if (target != null)
                    {
                        if (target.Has(MF.ISHASTE)) target.Clear(MF.ISHASTE);
                        else target.Set(MF.ISSLOW);
                        target.TTurn = true;
                        RunTo(target);
                    }
                    break;
                }
                case Items.WS_POLYMORPH:
                {
                    var target = RayTarget(delta, out _);
                    if (target == null) break;
                    FreeIfFlytrap(target);
                    var pack = target.Pack;
                    var pos = target.Pos;
                    MonstersOnLevel.Remove(target);
                    var nm = Monsters.New((char)('A' + Rnd.Next(26)), pos, LevelNum);
                    nm.Pack.AddRange(pack);
                    MonstersOnLevel.Add(nm);
                    RunTo(nm);
                    if (CanSeeMonster(nm)) know = true;
                    break;
                }
                case Items.WS_CANCEL:
                {
                    var target = RayTarget(delta, out _);
                    if (target != null)
                    {
                        target.Set(MF.ISCANC);
                        target.Clear(MF.ISINVIS | MF.CANHUH);
                        target.Disguise = target.Type;
                    }
                    break;
                }
                case Items.WS_INVIS:
                {
                    var target = RayTarget(delta, out _);
                    if (target != null) { target.Set(MF.ISINVIS); RunTo(target); }
                    break;
                }
                case Items.WS_TELAWAY:
                {
                    var target = RayTarget(delta, out _);
                    if (target != null)
                    {
                        FreeIfFlytrap(target);
                        target.Pos = FindFloor(null, 0, true);
                        target.DestObj = null; target.DestIsHero = true;
                        target.Set(MF.ISRUN);
                    }
                    break;
                }
                case Items.WS_TELTO:
                {
                    var target = RayTarget(delta, out _);
                    if (target != null)
                    {
                        FreeIfFlytrap(target);
                        var c = new Coord(Hero.y + delta.y, Hero.x + delta.x);
                        if (StepOk(Map[c.y, c.x]) && MonsterAt(c) == null) target.Pos = c;
                        target.DestObj = null; target.DestIsHero = true;
                        target.Set(MF.ISRUN);
                    }
                    break;
                }
                case Items.WS_NOP:
                    break;
            }
            if (know) StickKnown[which] = true;
            else if (!StickKnown[which]) CallIt(ThingKind.Stick, which);
            stick.Known = true;
            OneTurn(true);
        }

        void FreeIfFlytrap(Monster m)
        {
            if (m.Type == 'F') ClearP(MF.ISHELD);
        }

        Monster RayTarget(Coord delta, out Coord stop)
        {
            var c = Hero;
            while (true)
            {
                c = new Coord(c.y + delta.y, c.x + delta.x);
                if (c.y <= 0 || c.y >= Const.NUMLINES - 1 || c.x < 0 || c.x >= Const.NUMCOLS)
                { stop = c; return null; }
                var m = MonsterAt(c);
                if (m != null) { stop = c; return m; }
                if (!StepOk(Map[c.y, c.x])) { stop = c; return null; }
            }
        }

        /// <summary>fire_bolt(): lightning/fire/cold, and dragon flame.
        /// Bounces off walls; dragons are immune to flame.</summary>
        public void FireBolt(Coord from, Coord delta, string name, bool fromPlayer)
        {
            var c = from;
            var dir = delta;
            for (int len = 0; len < Const.BOLT_LENGTH * 2; len++)
            {
                var next = new Coord(c.y + dir.y, c.x + dir.x);
                if (len >= Const.BOLT_LENGTH && !fromPlayer) break;
                if (len >= Const.BOLT_LENGTH) break;
                if (next.y <= 0 || next.y >= Const.NUMLINES - 1 || next.x < 0 || next.x >= Const.NUMCOLS ||
                    !StepOk(Map[next.y, next.x]) && MonsterAt(next) == null && next != Hero)
                {
                    Msg("the {0} bounces", name);
                    dir = new Coord(-dir.y, -dir.x);
                    continue;
                }
                c = next;
                var m = MonsterAt(c);
                if (m != null)
                {
                    if (name == "flame" && m.Type == 'D')
                    {
                        Msg(Terse ? "the flame bounces" : "the flame bounces off the dragon");
                        continue;
                    }
                    if (!SaveThrow(Const.VS_MAGIC, m.Stats.Lvl))
                    {
                        m.Stats.Hpt -= Rnd.Roll(6, 6);
                        string nm = MonsterName(m);
                        if (m.Stats.Hpt <= 0)
                        {
                            if (fromPlayer) Killed(m, byPlayer: true);
                            else RemoveMonster(m, silent: true);
                        }
                        else Msg("the {0} hits the {1}", name, nm);
                        return;
                    }
                    Msg("the {0} whizzes past the {1}", name, MonsterName(m));
                    RunTo(m);
                    continue;
                }
                if (c == Hero && !fromPlayer)
                {
                    if (Save(Const.VS_MAGIC)) Msg("the {0} whizzes by you", name);
                    else
                    {
                        Player.Hpt -= Rnd.Roll(6, 6);
                        Msg("you are hit by the {0}", name);
                        if (Player.Hpt <= 0) { Death('b'); return; }
                    }
                    return;
                }
            }
        }

        // ---- Rings on/off (rings.c) ----

        public void PutOnRing(Thing t)
        {
            if (LeftRing != null && RightRing != null)
            {
                Msg(Terse ? "wearing two" : "you already have a ring on each hand");
                return;
            }
            if (LeftRing == null) LeftRing = t; else RightRing = t;
            switch (t.Which)
            {
                case Items.R_ADDSTR:
                    Player.Str = Math.Max(3, Math.Min(31, Player.Str + t.Arm));
                    break;
                case Items.R_SEEINVIS:
                    SetP(MF.CANSEE);
                    break;
                case Items.R_AGGR:
                    Aggravate();
                    break;
            }
            Msg(Terse ? "wearing {0} ({1})" : "you are now wearing {0} ({1})",
                Items.InvName(this, t, true), t.PackChar);
            OneTurn(true);
        }

        public void RemoveRing(Thing t)
        {
            if (t.Cursed) { Msg("you can't.  It appears to be cursed"); return; }
            if (t == LeftRing) LeftRing = null; else if (t == RightRing) RightRing = null;
            switch (t.Which)
            {
                case Items.R_ADDSTR:
                    Player.Str = Math.Max(3, Math.Min(31, Player.Str - t.Arm));
                    break;
                case Items.R_SEEINVIS:
                    if (!WearingRing(Items.R_SEEINVIS) && !Sched.FuseActive("unsee"))
                        ClearP(MF.CANSEE);
                    break;
            }
            Msg("was wearing {0}) {1}", t.PackChar, Items.InvName(this, t, true));
            OneTurn(true);
        }

        // ---- Eat (misc.c) ----

        public void Eat(Thing t)
        {
            if (t.Kind != ThingKind.Food)
            {
                Msg(Terse ? "that's inedible!" : "ugh, you would get ill if you ate that");
                return;
            }
            int which = t.Which;
            RemoveOne(t);
            if (FoodLeft < 0) FoodLeft = 0;
            FoodLeft += Const.HUNGERTIME - 200 + Rnd.Next(400);
            if (FoodLeft > Const.STOMACHSIZE) FoodLeft = Const.STOMACHSIZE;
            HungryState = 0;
            if (which == 1) Msg("my, that was a yummy {0}", Fruit);
            else if (Rnd.Next(100) > 70)
            {
                Player.Exp++;
                Msg(HasP(MF.ISHALU) ? "bummer, this food tastes awful"
                    : "yuk, this food tastes awful");
                CheckLevel();
            }
            else Msg(HasP(MF.ISHALU) ? "oh, wow, like superb, man" : "yum, that tasted good");
            OneTurn(true);
        }
    }
}
