// ATE Rogue 5.4.4 port — monster table and creation (monsters.c).
// Every number from the original monsters[26]; HP is roll(lvl, 8) — the
// table's hp-dice column was dead data in 5.4.4.
using System;
using System.Collections.Generic;

namespace AteRogue
{
    public sealed class MonsterInfo
    {
        public string Name;
        public int Carry;
        public MF Flags;
        public int Exp, Lvl, Arm;
        public string Dmg;
        public MonsterInfo(string name, int carry, MF flags, int exp, int lvl, int arm, string dmg)
        { Name = name; Carry = carry; Flags = flags; Exp = exp; Lvl = lvl; Arm = arm; Dmg = dmg; }
    }

    public static class Monsters
    {
        public static readonly MonsterInfo[] Table =
        {
            new MonsterInfo("aquator",        0, MF.ISMEAN,                        20,  5,  2, "0x0/0x0"),
            new MonsterInfo("bat",            0, MF.ISFLY,                          1,  1,  3, "1x2"),
            new MonsterInfo("centaur",       15, MF.None,                          17,  4,  4, "1x2/1x5/1x5"),
            new MonsterInfo("dragon",       100, MF.ISMEAN,                      5000, 10, -1, "1x8/1x8/3x10"),
            new MonsterInfo("emu",            0, MF.ISMEAN,                         2,  1,  7, "1x2"),
            new MonsterInfo("venus flytrap",  0, MF.ISMEAN,                        80,  8,  3, "0x1"), // dmg is per-instance (VfDamage)
            new MonsterInfo("griffin",       20, MF.ISMEAN | MF.ISFLY | MF.ISREGEN, 2000, 13, 2, "4x3/3x5"),
            new MonsterInfo("hobgoblin",      0, MF.ISMEAN,                         3,  1,  5, "1x8"),
            new MonsterInfo("ice monster",    0, MF.None,                           5,  1,  9, "0x0"),
            new MonsterInfo("jabberwock",    70, MF.None,                        3000, 15,  6, "2x12/2x4"),
            new MonsterInfo("kestrel",        0, MF.ISMEAN | MF.ISFLY,              1,  1,  7, "1x4"),
            new MonsterInfo("leprechaun",     0, MF.None,                          10,  3,  8, "1x1"),
            new MonsterInfo("medusa",        40, MF.ISMEAN,                       200,  8,  2, "3x4/3x4/2x5"),
            new MonsterInfo("nymph",        100, MF.None,                          37,  3,  9, "0x0"),
            new MonsterInfo("orc",           15, MF.ISGREED,                        5,  1,  6, "1x8"),
            new MonsterInfo("phantom",        0, MF.ISINVIS,                      120,  8,  3, "4x4"),
            new MonsterInfo("quagga",         0, MF.ISMEAN,                        15,  3,  3, "1x5/1x5"),
            new MonsterInfo("rattlesnake",    0, MF.ISMEAN,                         9,  2,  3, "1x6"),
            new MonsterInfo("snake",          0, MF.ISMEAN,                         2,  1,  5, "1x3"),
            new MonsterInfo("troll",         50, MF.ISREGEN | MF.ISMEAN,          120,  6,  4, "1x8/1x8/2x6"),
            new MonsterInfo("black unicorn",  0, MF.ISMEAN,                       190,  7, -2, "1x9/1x9/2x9"),
            new MonsterInfo("vampire",       20, MF.ISREGEN | MF.ISMEAN,          350,  8,  1, "1x10"),
            new MonsterInfo("wraith",         0, MF.None,                          55,  5,  4, "1x6"),
            new MonsterInfo("xeroc",         30, MF.None,                         100,  7,  7, "4x4"),
            new MonsterInfo("yeti",          30, MF.None,                          50,  4,  6, "1x6/1x6"),
            new MonsterInfo("zombie",         0, MF.ISMEAN,                         6,  2,  8, "1x8"),
        };

        // Level-ordered spawn strings (monsters.c). '\0' in wand = never wanders.
        const string LvlMons = "KEBSHIROZLCQANYFTWPXUMVGJD";
        static readonly bool[] Wanders = BuildWanders();

        static bool[] BuildWanders()
        {
            var w = new bool[26];
            for (int i = 0; i < 26; i++) w[i] = true;
            foreach (char c in "ILNFXD") w[LvlMons.IndexOf(c)] = false;
            return w;
        }

        public static MonsterInfo Info(char type) => Table[type - 'A'];

        /// <summary>randmonster(): pick a type for this dungeon level.</summary>
        public static char RandMonster(bool wander, int level)
        {
            while (true)
            {
                int d = level + Rnd.Next(10) - 6;
                if (d < 0) d = Rnd.Next(5);
                if (d > 25) d = Rnd.Next(5) + 21;
                if (!wander || Wanders[d]) return LvlMons[d];
            }
        }

        /// <summary>new_monster(): stats scale past the amulet level.</summary>
        public static Monster New(char type, Coord pos, int level)
        {
            var info = Info(type);
            int levAdd = Math.Max(0, level - Const.AMULETLEVEL);
            var m = new Monster { Type = type, Disguise = type, Pos = pos, Flags = info.Flags };
            m.Stats.Lvl = info.Lvl + levAdd;
            m.Stats.MaxHp = m.Stats.Hpt = Rnd.Roll(m.Stats.Lvl, 8);
            m.Stats.Arm = info.Arm - levAdd;
            m.Stats.Dmg = info.Dmg;
            m.Stats.Str = 10;
            m.Stats.Exp = info.Exp + levAdd * 10 + ExpAdd(m.Stats.Lvl, m.Stats.MaxHp);
            if (level > 29) m.Set(MF.ISHASTE);
            return m;
        }

        static int ExpAdd(int lvl, int maxHp)
        {
            int mod = lvl == 1 ? maxHp / 8 : maxHp / 6;
            if (lvl > 9) mod *= 20;
            else if (lvl > 6) mod *= 4;
            return mod;
        }
    }
}
