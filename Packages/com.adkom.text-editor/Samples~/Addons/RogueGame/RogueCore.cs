// ATE Rogue 5.4.4 port — core types and game state.
// Faithful structures: coordinates, rooms, things (objects), monsters,
// player stats, the daemon/fuse scheduler, and shared constants from rogue.h.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AteRogue
{
    public static class Const
    {
        public const int MAXROOMS = 9, MAXTHINGS = 9, MAXOBJ = 9, MAXPACK = 23,
            MAXTRAPS = 10, AMULETLEVEL = 26, NUMLINES = 24, NUMCOLS = 80,
            STATLINE = 23, BORE_LEVEL = 50, HEALTIME = 30, HUHDURATION = 20,
            SEEDURATION = 850, HUNGERTIME = 1300, MORETIME = 150,
            STOMACHSIZE = 2000, STARVETIME = 850, BOLT_LENGTH = 6,
            LAMPDIST = 3, DRAGONSHOT = 5;
        public const int VS_POISON = 0, VS_PARALYZATION = 0, VS_DEATH = 0,
            VS_BREATH = 2, VS_MAGIC = 3;

        public const char PASSAGE = '#', DOOR = '+', FLOOR = '.', PLAYER = '@',
            TRAP = '^', STAIRS = '%', GOLD = '*', POTION = '!', SCROLL = '?',
            FOOD = ':', WEAPON = ')', ARMOR = ']', AMULET = ',', RING = '=',
            STICK = '/', WALL_H = '-', WALL_V = '|', EMPTY = ' ';
    }

    [Flags]
    public enum MF
    {
        None = 0,
        CANHUH = 0x0001, CANSEE = 0x0002, ISBLIND = 0x0004,
        ISCANC = 0x0008, ISLEVIT = 0x0008, ISFOUND = 0x0010,
        ISGREED = 0x0020, ISHASTE = 0x0040, ISTARGET = 0x0080,
        ISHELD = 0x0100, ISHUH = 0x0200, ISINVIS = 0x0400,
        ISMEAN = 0x0800, ISHALU = 0x0800, ISREGEN = 0x1000,
        ISRUN = 0x2000, ISFLY = 0x4000, SEEMONST = 0x4000,
        ISSLOW = 0x8000
    }

    public struct Coord : IEquatable<Coord>
    {
        public int y, x;
        public Coord(int y, int x) { this.y = y; this.x = x; }
        public bool Equals(Coord o) => y == o.y && x == o.x;
        public override bool Equals(object o) => o is Coord c && Equals(c);
        public override int GetHashCode() => y * 131 + x;
        public static bool operator ==(Coord a, Coord b) => a.Equals(b);
        public static bool operator !=(Coord a, Coord b) => !a.Equals(b);
        /// <summary>Rogue's dist(): SQUARED distance.</summary>
        public static int Dist(Coord a, Coord b)
        { int dy = a.y - b.y, dx = a.x - b.x; return dy * dy + dx * dx; }
    }

    [Flags]
    public enum RoomFlags { None = 0, ISDARK = 1, ISGONE = 2, ISMAZE = 4 }

    public sealed class Room
    {
        public Coord Pos;              // upper-left
        public Coord Max;              // size
        public Coord GoldPos;
        public int GoldVal;
        public RoomFlags Flags;
        public readonly List<Coord> Exits = new List<Coord>();
        public bool IsDark => (Flags & RoomFlags.ISDARK) != 0;
        public bool IsGone => (Flags & RoomFlags.ISGONE) != 0;
        public bool IsMaze => (Flags & RoomFlags.ISMAZE) != 0;
        public bool Contains(Coord c) =>
            c.x >= Pos.x && c.x < Pos.x + Max.x && c.y >= Pos.y && c.y < Pos.y + Max.y;
        /// <summary>Strictly inside (not on the wall ring) — "in room" for
        /// lighting; doors are on the wall and do not count.</summary>
        public bool Inside(Coord c) =>
            c.x > Pos.x && c.x < Pos.x + Max.x - 1 && c.y > Pos.y && c.y < Pos.y + Max.y - 1;
    }

    public enum ThingKind { Potion, Scroll, Food, Weapon, Armor, Ring, Stick, Gold, Amulet }

    /// <summary>An object (rogue's THING as object). Which indexes the kind's
    /// info table.</summary>
    public sealed class Thing
    {
        public ThingKind Kind;
        public int Which;
        public int Count = 1;
        public int HPlus, DPlus;         // weapon enchant / ring o_arm reuse
        public int Arm;                  // armor class / ring power / stick? charges below
        public int Charges;              // sticks
        public int GoldVal;              // gold piles
        public string Damage = "0x0", Hurl = "0x0";
        public int Launch = -1;          // weapon launcher index
        public int Group;                // grouped missiles
        public bool Known;               // plusses known
        public bool Cursed;
        public bool IsProtected;         // scroll of protect armor
        public bool ScareFloor;          // scare-monster scroll picked up once
        public Coord Pos;
        public char PackChar;
        /// <summary>Map display char.</summary>
        public char Ch => Kind switch
        {
            ThingKind.Potion => Const.POTION,
            ThingKind.Scroll => Const.SCROLL,
            ThingKind.Food => Const.FOOD,
            ThingKind.Weapon => Const.WEAPON,
            ThingKind.Armor => Const.ARMOR,
            ThingKind.Ring => Const.RING,
            ThingKind.Stick => Const.STICK,
            ThingKind.Gold => Const.GOLD,
            _ => Const.AMULET
        };
    }

    public sealed class Stats
    {
        public int Str;
        public long Exp;
        public int Lvl;
        public int Arm;
        public int Hpt, MaxHp;
        public string Dmg;
        public int MaxStr;
        public Stats Clone() => (Stats)MemberwiseClone();
    }

    public sealed class Monster
    {
        public char Type;
        public char Disguise;
        public Coord Pos;
        public MF Flags;
        public Stats Stats = new Stats();
        public bool TTurn = true;
        public Coord? DestObj;           // chasing an object at this pos
        public bool DestIsHero = true;
        public char OldCh = ' ';         // what the monster stands on
        public readonly List<Thing> Pack = new List<Thing>();
        public int VfDamage;             // venus flytrap accumulating grip

        public bool Has(MF f) => (Flags & f) != 0;
        public void Set(MF f) => Flags |= f;
        public void Clear(MF f) => Flags &= ~f;
    }

    /// <summary>Rogue's daemon/fuse scheduler: daemons run every turn,
    /// fuses fire once after N turns.</summary>
    public sealed class Scheduler
    {
        sealed class Slot { public string Name; public Action Act; public int Time; public bool Daemon; }
        readonly List<Slot> _slots = new List<Slot>();

        public void Daemon(string name, Action act) =>
            _slots.Add(new Slot { Name = name, Act = act, Daemon = true });
        public void Fuse(string name, Action act, int time) =>
            _slots.Add(new Slot { Name = name, Act = act, Time = time, Daemon = false });
        public void Extinguish(string name) => _slots.RemoveAll(s => s.Name == name && !s.Daemon);
        public void Lengthen(string name, int extra)
        { foreach (var s in _slots) if (s.Name == name && !s.Daemon) s.Time += extra; }
        public bool FuseActive(string name)
        { foreach (var s in _slots) if (s.Name == name && !s.Daemon) return true; return false; }

        public void Run()
        {
            // Snapshot: acts may add/remove slots.
            var snap = _slots.ToArray();
            foreach (var s in snap)
            {
                if (s.Daemon) { s.Act(); continue; }
                if (--s.Time <= 0)
                {
                    _slots.Remove(s);
                    s.Act();
                }
            }
        }

        // ---- Persistence (AteApi 1.2 stateful lifecycle) ----
        // Actions cannot serialize; slots round-trip as (name, time, daemon)
        // descriptors and Import re-binds the actions by name.

        public List<(string Name, int Time, bool Daemon)> Export()
        {
            var res = new List<(string, int, bool)>();
            foreach (var s in _slots) res.Add((s.Name, s.Time, s.Daemon));
            return res;
        }

        public void Import(List<(string Name, int Time, bool Daemon)> slots, Func<string, Action> resolve)
        {
            _slots.Clear();
            foreach (var s in slots)
            {
                var act = resolve(s.Name);
                if (act == null) continue; // unknown slot from another version — drop
                _slots.Add(new Slot { Name = s.Name, Act = act, Time = s.Time, Daemon = s.Daemon });
            }
        }
    }
}
