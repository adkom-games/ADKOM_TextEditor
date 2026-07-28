// ATE Rogue 5.4.4 port — command dispatch (command.c) and the pack
// commands (pack.c, weapons.c, armor.c fronts).
using System;
using System.Collections.Generic;

namespace AteRogue
{
    public partial class Game
    {
        public void DoCommand(char c, bool ctrl)
        {
            // Count prefix: digits accumulate, next command consumes.
            if (char.IsDigit(c))
            {
                _count = Math.Min(255, _count * 10 + (c - '0'));
                return;
            }
            int count = _count;
            _count = 0;

            if (ctrl && "hjklyubn".IndexOf(char.ToLower(c)) >= 0)
            {
                DoorStop = true;
                FirstMove = true;
                Running = true;
                RunCh = char.ToLower(c);
                return;
            }

            switch (c)
            {
                // Movement.
                case 'h': case 'j': case 'k': case 'l':
                case 'y': case 'u': case 'b': case 'n':
                    if (count > 1) { CountRepeat = count - 1; _countCh = c; }
                    DoMove(RunDelta(c), running: false);
                    break;
                case 'H': case 'J': case 'K': case 'L':
                case 'Y': case 'U': case 'B': case 'N':
                    Running = true;
                    RunCh = char.ToLower(c);
                    break;
                case '.':
                    if (count > 1) { CountRepeat = count - 1; _countCh = '.'; }
                    OneTurn(true); // rest
                    break;
                case ' ':
                    break;
                case (char)27:
                    _count = 0; CountRepeat = 0; _countCh = '\0';
                    StopRunning();
                    break;

                case ',':
                {
                    var obj = ObjectAt(Hero);
                    if (HasP(MF.ISLEVIT)) { Msg("you can't.  You're floating off the ground!"); break; }
                    if (obj == null) Msg(Terse ? "nothing here" : "there is nothing here to pick up");
                    else { PickUp(obj); OneTurn(true); }
                    break;
                }
                case '>': DownLevel(); break;
                case '<': UpLevel(); break;
                case 's':
                    if (count > 1) { CountRepeat = count - 1; _countCh = 's'; }
                    Search(silent: false);
                    OneTurn(true);
                    break;

                case 'i': ShowInventory(null); break;
                case 'q':
                    AskItem("quaff", t => t.Kind == ThingKind.Potion, t => Quaff(t));
                    break;
                case 'r':
                    AskItem("read", t => t.Kind == ThingKind.Scroll, t => ReadScroll(t));
                    break;
                case 'e':
                    AskItem("eat", t => t.Kind == ThingKind.Food, t => Eat(t));
                    break;
                case 'w':
                    AskItem("wield", t => t.Kind == ThingKind.Weapon, Wield);
                    break;
                case 'W':
                    AskItem("wear", t => t.Kind == ThingKind.Armor, Wear);
                    break;
                case 'T': TakeOffArmor(); break;
                case 'P':
                    AskItem("put on", t => t.Kind == ThingKind.Ring && t != LeftRing && t != RightRing,
                        PutOnRing);
                    break;
                case 'R':
                {
                    var worn = new List<Thing>();
                    if (LeftRing != null) worn.Add(LeftRing);
                    if (RightRing != null) worn.Add(RightRing);
                    if (worn.Count == 0) { Msg(Terse ? "no rings" : "you aren't wearing any rings"); break; }
                    if (worn.Count == 1) RemoveRing(worn[0]);
                    else AskItem("remove", t => t == LeftRing || t == RightRing, RemoveRing);
                    break;
                }
                case 'd':
                    AskItem("drop", t => true, Drop);
                    break;
                case 't':
                    AskDirection(delta =>
                        AskItem("throw", t => true, t => Throw(t, delta)));
                    break;
                case 'z':
                    AskDirection(delta =>
                        AskItem("zap with", t => t.Kind == ThingKind.Stick, t => Zap(t, delta)));
                    break;
                case 'f':
                    AskDirection(delta => FightDir(delta));
                    break;
                case '^':
                    AskDirection(delta =>
                    {
                        var c2 = new Coord(Hero.y + delta.y, Hero.x + delta.x);
                        if (Map[c2.y, c2.x] == Const.TRAP)
                        {
                            FSeen[c2.y, c2.x] = true;
                            Msg(HasP(MF.ISHALU) ? TrapNames[Rnd.Next(TrapNames.Length)]
                                : TrapNames[TrapType[c2.y, c2.x]]);
                        }
                        else Msg("no trap there");
                    });
                    break;
                case 'c':
                    AskItem("call", t => t.Kind == ThingKind.Potion || t.Kind == ThingKind.Scroll ||
                        t.Kind == ThingKind.Ring || t.Kind == ThingKind.Stick, t =>
                    {
                        CallIt(t.Kind, t.Which);
                    });
                    break;
                case 'D': ShowDiscovered(); break;
                case ')':
                    Msg(CurWeapon == null ? "you aren't wielding anything"
                        : "wielding " + Items.InvName(this, CurWeapon, true) + " (" + CurWeapon.PackChar + ")");
                    break;
                case ']':
                    Msg(CurArmor == null ? "you aren't wearing any armor"
                        : "wearing " + Items.InvName(this, CurArmor, true) + " (" + CurArmor.PackChar + ")");
                    break;
                case '=':
                {
                    if (LeftRing == null && RightRing == null) { Msg("you aren't wearing any rings"); break; }
                    string s = "";
                    if (LeftRing != null) s += Items.InvName(this, LeftRing, true) + " (L)";
                    if (RightRing != null) s += (s.Length > 0 ? ", " : "") +
                        Items.InvName(this, RightRing, true) + " (R)";
                    Msg(s);
                    break;
                }
                case '@': UpdateStatus(); break;
                case 'v': Msg("ATE Rogue, ported from Rogue version 5.4.4"); break;
                case '?': ShowHelp(); break;
                case 'Q': QuitCommand(); break;
                default:
                    Msg("illegal command '{0}'", c);
                    break;
            }
            PumpMessages();
        }

        void FightDir(Coord delta)
        {
            var c = new Coord(Hero.y + delta.y, Hero.x + delta.x);
            var m = MonsterAt(c);
            if (m == null || !CanSeeMonster(m)) { Msg("no monster there"); return; }
            ToDeath = true;
            m.Set(MF.ISTARGET);
            Fight(m, CurWeapon, thrown: false);
            ToDeath = false;
            OneTurn(true);
        }

        // ---- Pack fronts ----

        void Wield(Thing t)
        {
            if (CurWeapon != null && CurWeapon.Cursed)
            { Msg("you can't.  It appears to be cursed"); return; }
            if (t == CurWeapon) { Msg("that's already in your hand"); return; }
            if (t == CurArmor || t == LeftRing || t == RightRing)
            { Msg("you have to take it off first"); return; }
            CurWeapon = t;
            Msg(Terse ? "wielding {0} ({1})" : "you are now wielding {0} ({1})",
                Items.InvName(this, t, true), t.PackChar);
            OneTurn(true);
        }

        void Wear(Thing t)
        {
            if (CurArmor != null)
            {
                Msg(Terse ? "you are already wearing some"
                    : "you are already wearing some.  You'll have to take it off first");
                return;
            }
            CurArmor = t;
            t.Known = true;
            Msg(Terse ? "wearing {0}" : "you are now wearing {0}", Items.InvName(this, t, true));
            OneTurn(true);
        }

        void TakeOffArmor()
        {
            if (CurArmor == null) { Msg(Terse ? "not wearing armor" : "you aren't wearing any armor"); return; }
            if (CurArmor.Cursed) { Msg("you can't.  It appears to be cursed"); return; }
            var t = CurArmor;
            CurArmor = null;
            Msg("was wearing {0}) {1}", t.PackChar, Items.InvName(this, t, true));
            OneTurn(true);
        }

        void Drop(Thing t)
        {
            char ch = Map[Hero.y, Hero.x];
            if (ch != Const.FLOOR && ch != Const.PASSAGE)
            { Msg(Terse ? "nothing here" : "there is something there already"); return; }
            if (ObjectAt(Hero) != null)
            { Msg(Terse ? "something there" : "there is something there already"); return; }
            if ((t == CurArmor || t == CurWeapon || t == LeftRing || t == RightRing) && t.Cursed)
            { Msg("you can't.  It appears to be cursed"); return; }
            var dropped = LeavePack(t);
            dropped.Pos = Hero;
            if (dropped.Kind == ThingKind.Scroll && dropped.Which == Items.S_SCARE)
                dropped.ScareFloor = true;
            ObjectsOnLevel.Add(dropped);
            Msg("dropped {0}", Items.InvName(this, dropped, true));
            OneTurn(true);
        }

        void Throw(Thing t, Coord delta)
        {
            var missile = LeavePack(t);
            // Trace flight until blocked (weapons.c do_motion).
            var c = Hero;
            Monster hitMonster = null;
            while (true)
            {
                var next = new Coord(c.y + delta.y, c.x + delta.x);
                if (next.y <= 0 || next.y >= Const.NUMLINES - 1 || next.x < 0 || next.x >= Const.NUMCOLS)
                    break;
                var m = MonsterAt(next);
                if (m != null) { hitMonster = m; c = next; break; }
                if (!StepOk(Map[next.y, next.x])) break;
                c = next;
                if (Map[c.y, c.x] == Const.DOOR) break;
            }
            if (hitMonster != null)
            {
                Fight(hitMonster, missile, thrown: true);
                if (MonstersOnLevel.Contains(hitMonster) || true)
                    FallAt(missile, c);
            }
            else FallAt(missile, c);
            OneTurn(true);
        }

        // ---- Inventory / discovered / help displays ----
        // Rendered over the map; any key press redraws the map (the item
        // selector stays modal until a letter/ESC).

        public void ShowInventory(Func<Thing, bool> filter)
        {
            int row = 1;
            for (int y = 1; y < Const.STATLINE; y++) Term.ClearToEol(y, 0);
            foreach (var t in Pack)
            {
                if (filter != null && !filter(t)) continue;
                if (row >= Const.STATLINE - 1) break;
                string mark = t == CurWeapon ? " (weapon in hand)"
                    : t == CurArmor ? " (being worn)"
                    : t == LeftRing ? " (on left hand)"
                    : t == RightRing ? " (on right hand)" : "";
                Term.PutStr(row++, 0, t.PackChar + ") " + Items.InvName(this, t, false) + mark);
            }
            if (row == 1) Term.PutStr(row++, 0, "You are empty handed.");
            Term.PutStr(row, 0, "--Press space to continue--");
            Term.Flush();
            if (Mode != InputMode.SelectItem)
            {
                Mode = InputMode.More;
                _msgQueue.Clear();
            }
        }

        void ShowDiscovered()
        {
            int row = 1;
            for (int y = 1; y < Const.STATLINE; y++) Term.ClearToEol(y, 0);
            for (int i = 0; i < 14; i++)
                if (PotionKnown[i] && row < Const.STATLINE - 1)
                    Term.PutStr(row++, 0, "A potion of " + Items.Potions[i].Name +
                        " (" + PotionColor(i) + ")");
            for (int i = 0; i < 18; i++)
                if (ScrollKnown[i] && row < Const.STATLINE - 1)
                    Term.PutStr(row++, 0, "A scroll of " + Items.Scrolls[i].Name);
            for (int i = 0; i < 14; i++)
                if (RingKnown[i] && row < Const.STATLINE - 1)
                    Term.PutStr(row++, 0, "A ring of " + Items.Rings[i].Name +
                        " (" + RingStone(i) + ")");
            for (int i = 0; i < 14; i++)
                if (StickKnown[i] && row < Const.STATLINE - 1)
                    Term.PutStr(row++, 0, "A " + (StickIsStaff(i) ? "staff" : "wand") +
                        " of " + Items.Sticks[i].Name);
            if (row == 1) Term.PutStr(row++, 0, "Nothing discovered yet.");
            Term.PutStr(row, 0, "--Press space to continue--");
            Term.Flush();
            Mode = InputMode.More;
            _msgQueue.Clear();
        }

        void ShowHelp()
        {
            string[] help =
            {
                "hjkl yubn  move (arrows too)     HJKL YUBN  run",
                ",  pick up        >  go down stairs   <  go up (with amulet)",
                "s  search         .  rest             i  inventory",
                "q  quaff potion   r  read scroll      e  eat food",
                "w  wield weapon   W  wear armor       T  take armor off",
                "P  put on ring    R  remove ring      d  drop object",
                "t  throw <dir>    z  zap <dir>        f  fight <dir>",
                "^  identify trap  c  call item        D  discoveries",
                ")  weapon  ]  armor  =  rings  @  status  v  version",
                "Q  quit (Escape closes prompts)",
            };
            for (int y = 1; y < Const.STATLINE; y++) Term.ClearToEol(y, 0);
            for (int i = 0; i < help.Length; i++) Term.PutStr(i + 1, 0, help[i]);
            Term.PutStr(help.Length + 2, 0, "--Press space to continue--");
            Term.Flush();
            Mode = InputMode.More;
            _msgQueue.Clear();
        }
    }
}
