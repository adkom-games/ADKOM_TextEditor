// ATE Rogue 5.4.4 port — game state, message/status UI, input state
// machine, and the main turn loop (main.c, command.c order, io.c).
//
// Adaptation note: original Rogue blocks on readchar(); ATE is event-
// driven, so prompts (--More--, direction, item selection, text entry)
// are modal input states, and multi-turn actions (running, rest counts,
// no_command paralysis) resolve synchronously with a safety bound.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AteRogue
{
    public enum InputMode { Play, More, Direction, SelectItem, TextEntry, Confirm, EndScreen }

    public partial class Game
    {
        public readonly Term Term;
        public Action OnQuitRequested;

        // Map: chars are the TRUE terrain; flags carry pass/real/seen/trap#.
        public readonly char[,] Map = new char[Const.NUMLINES, Const.NUMCOLS];
        public readonly bool[,] FPass = new bool[Const.NUMLINES, Const.NUMCOLS];
        public readonly bool[,] FReal = new bool[Const.NUMLINES, Const.NUMCOLS];
        public readonly bool[,] FSeen = new bool[Const.NUMLINES, Const.NUMCOLS];
        public readonly int[,] TrapType = new int[Const.NUMLINES, Const.NUMCOLS];
        public readonly int[,] PassNum = new int[Const.NUMLINES, Const.NUMCOLS];

        public readonly Room[] Rooms = new Room[Const.MAXROOMS];
        public readonly List<Room> Passages = new List<Room>();
        public readonly List<Monster> MonstersOnLevel = new List<Monster>();
        public readonly List<Thing> ObjectsOnLevel = new List<Thing>();

        public Stats Player = new Stats();
        public Stats MaxStats = new Stats();
        public MF PlayerFlags;
        public Thing CurWeapon, CurArmor, LeftRing, RightRing;
        public long Purse;
        public int LevelNum = 1, MaxLevel = 1;
        public bool HasAmulet, SeenStairs;
        public Coord Hero, Stairs;
        public Room CurRoom;                 // room or passage the hero is in
        public int FoodLeft, HungryState, NoFood;
        public int NoCommand, NoMove, Quiet, CountRepeat;
        public bool Terse = false, ToDeath, Running, DoorStop, FirstMove;
        public char RunCh;
        public int VfHit;                    // flytrap grip (held escape rolls)
        public readonly Scheduler Sched = new Scheduler();
        public bool GameOver;

        // Input state machine.
        public InputMode Mode = InputMode.Play;
        Action<Coord> _dirCont;              // Direction continuation (delta)
        Action<Thing> _itemCont;             // SelectItem continuation
        Func<Thing, bool> _itemFilter;
        string _itemVerb;
        Action<string> _textCont;
        readonly StringBuilder _textBuf = new StringBuilder();
        string _textPrompt;
        Action _confirmYes;
        readonly Queue<string> _msgQueue = new Queue<string>();
        string _shownMsg = "";
        public string LastMsg = "";
        char _lastComm; Coord _lastDir; Thing _lastPick;

        public Game(Term term) { Term = term; }

        public bool HasP(MF f) => (PlayerFlags & f) != 0;
        public void SetP(MF f) => PlayerFlags |= f;
        public void ClearP(MF f) => PlayerFlags &= ~f;

        // ---- Start / init_player ----

        public void Start()
        {
            Rnd.Seed(Environment.TickCount);
            InitNames();
            MaxStats = new Stats { Str = 16, MaxStr = 16, Exp = 0, Lvl = 1, Arm = 10, Hpt = 12, MaxHp = 12, Dmg = "1x4" };
            Player = MaxStats.Clone();
            FoodLeft = Const.HUNGERTIME;
            Purse = 0;

            var food = new Thing { Kind = ThingKind.Food, Which = 0 };
            AddToPack(food, silent: true);
            CurArmor = new Thing { Kind = ThingKind.Armor, Which = Items.A_RING_MAIL,
                Arm = Items.ArmorClass[Items.A_RING_MAIL] - 1, Known = true };
            AddToPack(CurArmor2(CurArmor), silent: true);
            CurWeapon = new Thing { Known = true };
            Items.InitWeapon(CurWeapon, Items.MACE, ref GroupCounter);
            CurWeapon.HPlus = 1; CurWeapon.DPlus = 1;
            AddToPack(CurWeapon, silent: true);
            var bow = new Thing { Known = true };
            Items.InitWeapon(bow, Items.BOW, ref GroupCounter);
            bow.HPlus = 1;
            AddToPack(bow, silent: true);
            var arrows = new Thing { Known = true };
            Items.InitWeapon(arrows, Items.ARROW, ref GroupCounter);
            arrows.Count = Rnd.Next(15) + 25;
            AddToPack(arrows, silent: true);

            Sched.Daemon("doctor", Doctor);
            Sched.Fuse("swander", StartWanderer, Rnd.Spread(70));
            Sched.Daemon("stomach", Stomach);

            NewLevel();
            Msg("Hello.  Welcome to the Dungeons of Doom.");
            Redraw();
        }

        Thing CurArmor2(Thing t) => t; // keeps AddToPack call shape readable

        // ---- Messages (io.c adapted) ----

        public void Msg(string fmt, params object[] args)
        {
            string s = args.Length > 0 ? string.Format(fmt, args) : fmt;
            if (s.Length > 0) s = char.ToUpper(s[0]) + s.Substring(1);
            LastMsg = s;
            _msgQueue.Enqueue(s);
        }

        void PumpMessages()
        {
            // Terminal screens (tombstone/win/quit) own the display: pending
            // combat messages must not clobber EndScreen mode (the freeze
            // Cary hit: die -> More mode -> every key eaten).
            if (GameOver) { _msgQueue.Clear(); return; }
            if (_msgQueue.Count == 0) return;
            _shownMsg = _msgQueue.Dequeue();
            Term.ClearToEol(0, 0);
            string line = _shownMsg + (_msgQueue.Count > 0 ? "--More--" : "");
            Term.PutStr(0, 0, line.Length > Const.NUMCOLS ? line.Substring(0, Const.NUMCOLS) : line);
            if (_msgQueue.Count > 0) Mode = InputMode.More;
        }

        public void ClearMsg()
        {
            _shownMsg = "";
            Term.ClearToEol(0, 0);
        }

        // ---- Status line (io.c exact format) ----

        public void UpdateStatus()
        {
            int hpw = Player.MaxHp.ToString().Length;
            int arm = 10 - (CurArmor != null ? CurArmor.Arm : Player.Arm);
            string hunger = HungryState switch { 1 => "Hungry", 2 => "Weak", 3 => "Faint", _ => "" };
            string s = string.Format(
                "Level: {0}  Gold: {1,-5}  Hp: {2}({3})  Str: {4,2}({5})  Arm: {6,-2}  Exp: {7}/{8}  {9}",
                LevelNum, Purse,
                Player.Hpt.ToString().PadLeft(hpw), Player.MaxHp.ToString().PadLeft(hpw),
                Player.Str, MaxStats.Str, arm, Player.Lvl, Player.Exp, hunger);
            Term.ClearToEol(Const.STATLINE, 0);
            Term.PutStr(Const.STATLINE, 0, s);
        }

        // ---- Input entry (from the addon key hook) ----

        public void Key(char c, bool ctrl)
        {
            if (GameOver && Mode != InputMode.EndScreen) return;
            switch (Mode)
            {
                case InputMode.More:
                    // Original rogue insisted on space; ANY key acknowledges
                    // here — an ignored keypress reads as a frozen game.
                    Mode = InputMode.Play;
                    RedrawAll(); // clears inventory/help overlays too
                    PumpMessages();
                    if (Mode == InputMode.Play && _pendingTurns) ContinueTurns();
                    Redraw();
                    return;
                case InputMode.Direction:
                    HandleDirectionKey(c);
                    Redraw();
                    return;
                case InputMode.SelectItem:
                    HandleItemKey(c);
                    Redraw();
                    return;
                case InputMode.TextEntry:
                    HandleTextKey(c);
                    Redraw();
                    return;
                case InputMode.Confirm:
                    Mode = InputMode.Play;
                    ClearMsg();
                    if (c == 'y') _confirmYes?.Invoke();
                    else { Msg(""); UpdateStatus(); PumpMessages(); }
                    Redraw();
                    return;
                case InputMode.EndScreen:
                    OnQuitRequested?.Invoke();
                    return;
            }
            if (ctrl && (c == 'r' || c == 'R')) { RedrawAll(); return; }
            ClearMsg();
            DoCommand(c, ctrl);
            RunTurnsAfterCommand();
        }

        bool _pendingTurns;

        /// <summary>After a command: resolve running / repeat counts /
        /// paralysis synchronously (bounded), then flush the screen.</summary>
        void RunTurnsAfterCommand()
        {
            ContinueTurns();
        }

        void ContinueTurns()
        {
            _pendingTurns = false;
            int safety = 500;
            while (Mode == InputMode.Play && !GameOver && safety-- > 0)
            {
                if (NoCommand > 0)
                {
                    NoCommand--;
                    if (NoCommand == 0) { Msg("you can move again"); SetP(MF.ISRUN); }
                    OneTurn(consumed: true);
                    continue;
                }
                if (Running)
                {
                    DoMove(RunDelta(RunCh), running: true);
                    if (Mode != InputMode.Play || GameOver) break;
                    OneTurn(consumed: true);
                    continue;
                }
                if (CountRepeat > 0 && _countCh != '\0')
                {
                    CountRepeat--;
                    char c = _countCh;
                    if (CountRepeat == 0) _countCh = '\0';
                    DoCommand(c, false);
                    continue;
                }
                break;
            }
            if (Mode == InputMode.More) _pendingTurns =
                Running || CountRepeat > 0 || NoCommand > 0;
            // Overlay/prompt modes own the screen — repainting the map here
            // instantly wiped the help/inventory screens ('?' flashed blank).
            if (Mode == InputMode.Play) Redraw();
            else Term.Flush();
        }

        int _count; char _countCh;

        // ---- The per-turn clock (command.c order) ----

        /// <summary>One completed player action: BEFORE daemons already ran
        /// (start-of-command), monsters move AFTER, rings tick.</summary>
        public void OneTurn(bool consumed)
        {
            if (!consumed || GameOver) return;
            Sched.Run();
            Runners();
            if (GameOver) return;
            if (WearingRing(Items.R_SEARCH)) Search(silent: true);
            if (LeftRing != null && LeftRing.Which == Items.R_SEARCH &&
                RightRing != null && RightRing.Which == Items.R_SEARCH)
                Search(silent: true);
            if (WearingRing(Items.R_TELEPORT) && Rnd.Next(50) == 0)
            {
                Teleport();
                Msg("you feel a wrenching sensation in your gut");
            }
            Look(wakeup: true);
            UpdateStatus();
            PumpMessages();
        }

        // ---- Modal prompt helpers ----

        public void AskDirection(Action<Coord> cont)
        {
            Msg(Terse ? "direction: " : "which direction? ");
            PumpMessages();
            Mode = InputMode.Direction;
            _dirCont = cont;
        }

        void HandleDirectionKey(char c)
        {
            Mode = InputMode.Play;
            ClearMsg();
            if (c == (char)27) return;
            Coord d;
            switch (char.ToLower(c))
            {
                case 'h': d = new Coord(0, -1); break;
                case 'j': d = new Coord(1, 0); break;
                case 'k': d = new Coord(-1, 0); break;
                case 'l': d = new Coord(0, 1); break;
                case 'y': d = new Coord(-1, -1); break;
                case 'u': d = new Coord(-1, 1); break;
                case 'b': d = new Coord(1, -1); break;
                case 'n': d = new Coord(1, 1); break;
                default: return;
            }
            if (HasP(MF.ISHUH) && Rnd.Next(5) == 0)
                do { d = new Coord(Rnd.Next(3) - 1, Rnd.Next(3) - 1); }
                while (d.y == 0 && d.x == 0);
            _lastDir = d;
            var cont = _dirCont; _dirCont = null;
            cont?.Invoke(d);
            RunTurnsAfterCommand();
        }

        public void AskItem(string verb, Func<Thing, bool> filter, Action<Thing> cont)
        {
            var candidates = Pack.FindAll(t => filter(t));
            if (candidates.Count == 0)
            {
                Msg(Terse ? "nothing appropriate" : "you don't have anything appropriate");
                PumpMessages();
                return;
            }
            Msg("which object do you want to {0}? (* for list): ", verb);
            PumpMessages();
            Mode = InputMode.SelectItem;
            _itemFilter = filter;
            _itemCont = cont;
            _itemVerb = verb;
        }

        void HandleItemKey(char c)
        {
            if (c == (char)27) { Mode = InputMode.Play; ClearMsg(); Msg(""); return; }
            if (c == '*')
            {
                ShowInventory(_itemFilter);
                return; // stay in select mode; overlay shows letters
            }
            foreach (var t in Pack)
            {
                if (t.PackChar != c) continue;
                if (!_itemFilter(t))
                {
                    Mode = InputMode.Play;
                    ClearMsg();
                    Msg(Terse ? "nothing appropriate" : "you can't {0} that", _itemVerb);
                    PumpMessages();
                    RedrawMap();
                    return;
                }
                Mode = InputMode.Play;
                ClearMsg();
                RedrawMap();
                var cont = _itemCont; _itemCont = null;
                _lastPick = t;
                cont?.Invoke(t);
                RunTurnsAfterCommand();
                return;
            }
            Mode = InputMode.Play;
            ClearMsg();
            Msg("no such item");
            PumpMessages();
            RedrawMap();
        }

        public void AskText(string prompt, Action<string> cont)
        {
            _textPrompt = prompt;
            _textBuf.Clear();
            Mode = InputMode.TextEntry;
            _textCont = cont;
            DrawTextPrompt();
        }

        void DrawTextPrompt()
        {
            Term.ClearToEol(0, 0);
            Term.PutStr(0, 0, _textPrompt + _textBuf);
        }

        void HandleTextKey(char c)
        {
            if (c == (char)27) { Mode = InputMode.Play; ClearMsg(); return; }
            if (c == '\n' || c == '\r')
            {
                Mode = InputMode.Play;
                ClearMsg();
                var cont = _textCont; _textCont = null;
                cont?.Invoke(_textBuf.ToString());
                PumpMessages();
                return;
            }
            if (c == '\b') { if (_textBuf.Length > 0) _textBuf.Length--; }
            else if (c >= ' ' && _textBuf.Length < 30) _textBuf.Append(c);
            DrawTextPrompt();
        }

        public void Confirm(string prompt, Action yes)
        {
            Term.ClearToEol(0, 0);
            Term.PutStr(0, 0, prompt);
            Mode = InputMode.Confirm;
            _confirmYes = yes;
        }

        // ---- Misc shared ----

        public void StopRunning()
        {
            Running = false;
            ToDeath = false;
            CountRepeat = 0;
            _countCh = '\0';
        }

        public void ChgStr(int amt)
        {
            if (amt == 0) return;
            int add = RingStrBonus();
            Player.Str -= add;
            Player.Str = Math.Max(3, Math.Min(31, Player.Str + amt));
            if (Player.Str > MaxStats.Str) MaxStats.Str = Player.Str;
            Player.Str += add;
        }

        int RingStrBonus()
        {
            int add = 0;
            if (LeftRing != null && LeftRing.Which == Items.R_ADDSTR) add += LeftRing.Arm;
            if (RightRing != null && RightRing.Which == Items.R_ADDSTR) add += RightRing.Arm;
            return add;
        }

        public static Coord RunDelta(char runch) => char.ToLower(runch) switch
        {
            'h' => new Coord(0, -1), 'j' => new Coord(1, 0), 'k' => new Coord(-1, 0),
            'l' => new Coord(0, 1), 'y' => new Coord(-1, -1), 'u' => new Coord(-1, 1),
            'b' => new Coord(1, -1), _ => new Coord(1, 1)
        };

        public bool StepOk(char ch) =>
            ch != ' ' && ch != Const.WALL_H && ch != Const.WALL_V && !char.IsLetter(ch);

        public char CharAt(Coord c) => Map[c.y, c.x];

        public Monster MonsterAt(Coord c)
        {
            foreach (var m in MonstersOnLevel) if (m.Pos == c) return m;
            return null;
        }

        public Thing ObjectAt(Coord c)
        {
            foreach (var t in ObjectsOnLevel) if (t.Pos == c) return t;
            return null;
        }

        public Room RoomIn(Coord c)
        {
            foreach (var r in Rooms)
                if (!r.IsGone && r.Contains(c)) return r;
            return null;
        }

        public void RemoveMonster(Monster m, bool silent)
        {
            MonstersOnLevel.Remove(m);
            foreach (var t in m.Pack) { t.Pos = m.Pos; ObjectsOnLevel.Add(t); }
            m.Pack.Clear();
        }

        public void DropGoldAt(Coord pos, int amount)
        {
            var gold = new Thing { Kind = ThingKind.Gold, GoldVal = amount, Pos = pos, Group = 1 };
            ObjectsOnLevel.Add(gold);
            if (Map[pos.y, pos.x] == Const.FLOOR || Map[pos.y, pos.x] == Const.PASSAGE)
                Map[pos.y, pos.x] = Const.GOLD;
        }

        public bool CanSeeMonster(Monster m)
        {
            if (HasP(MF.ISBLIND)) return false;
            if (m.Has(MF.ISINVIS) && !HasP(MF.CANSEE)) return false;
            if (Coord.Dist(m.Pos, Hero) < Const.LAMPDIST)
            {
                if (m.Pos.y != Hero.y && m.Pos.x != Hero.x &&
                    !StepOk(Map[m.Pos.y, Hero.x]) && !StepOk(Map[Hero.y, m.Pos.x]))
                    return false;
                return true;
            }
            var r = RoomIn(m.Pos);
            return r != null && r == RoomIn(Hero) && !r.IsDark;
        }

        // ---- Death / quit / win entry (details in RogueRip.cs) ----

        public void Death(char cause)
        {
            Purse -= Purse / 10;
            GameOver = true;
            ShowTombstone(cause);
            Mode = InputMode.EndScreen;
        }

        public void QuitCommand()
        {
            Confirm("really quit?", () =>
            {
                GameOver = true;
                ShowQuitScreen();
                Mode = InputMode.EndScreen;
            });
        }
    }
}
