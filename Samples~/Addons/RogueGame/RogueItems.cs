// ATE Rogue 5.4.4 port — item tables, identification, pack, naming
// (things.c, init.c, pack.c, weapons.c, armor.c). All numbers original.
using System;
using System.Collections.Generic;
using System.Text;

namespace AteRogue
{
    public sealed class ItemInfo
    {
        public string Name;
        public int Prob, Worth;
        public ItemInfo(string name, int prob, int worth) { Name = name; Prob = prob; Worth = worth; }
    }

    public static class Items
    {
        // Ring indexes
        public const int R_PROTECT = 0, R_ADDSTR = 1, R_SUSTSTR = 2, R_SEARCH = 3,
            R_SEEINVIS = 4, R_NOP = 5, R_AGGR = 6, R_ADDHIT = 7, R_ADDDAM = 8,
            R_REGEN = 9, R_DIGEST = 10, R_TELEPORT = 11, R_STEALTH = 12, R_SUSTARM = 13;
        // Potion indexes
        public const int P_CONFUSE = 0, P_LSD = 1, P_POISON = 2, P_STRENGTH = 3,
            P_SEEINVIS = 4, P_HEALING = 5, P_MFIND = 6, P_TFIND = 7, P_RAISE = 8,
            P_XHEAL = 9, P_HASTE = 10, P_RESTORE = 11, P_BLIND = 12, P_LEVIT = 13;
        // Scroll indexes
        public const int S_CONFUSE = 0, S_MAP = 1, S_HOLD = 2, S_SLEEP = 3,
            S_ARMOR = 4, S_ID_POTION = 5, S_ID_SCROLL = 6, S_ID_WEAPON = 7,
            S_ID_ARMOR = 8, S_ID_R_OR_S = 9, S_SCARE = 10, S_FDET = 11,
            S_TELEP = 12, S_ENCH = 13, S_CREATE = 14, S_REMOVE = 15,
            S_AGGR = 16, S_PROTECT = 17;
        // Stick indexes
        public const int WS_LIGHT = 0, WS_INVIS = 1, WS_ELECT = 2, WS_FIRE = 3,
            WS_COLD = 4, WS_POLYMORPH = 5, WS_MISSILE = 6, WS_HASTE_M = 7,
            WS_SLOW_M = 8, WS_DRAIN = 9, WS_NOP = 10, WS_TELAWAY = 11,
            WS_TELTO = 12, WS_CANCEL = 13;
        // Weapon indexes
        public const int MACE = 0, SWORD = 1, BOW = 2, ARROW = 3, DAGGER = 4,
            TWOSWORD = 5, DART = 6, SHURIKEN = 7, SPEAR = 8, FLAME = 9;
        // Armor indexes
        public const int A_LEATHER = 0, A_RING_MAIL = 1, A_STUDDED = 2, A_SCALE = 3,
            A_CHAIN = 4, A_SPLINT = 5, A_BANDED = 6, A_PLATE = 7;

        // Category pickup percentages (things[]).
        public static readonly int[] ThingProbs = { 26, 36, 16, 7, 7, 4, 4 };
        // Category order matches ThingKind: Potion, Scroll, Food, Weapon, Armor, Ring, Stick.

        public static readonly ItemInfo[] Potions =
        {
            new ItemInfo("confusion", 7, 5), new ItemInfo("hallucination", 8, 5),
            new ItemInfo("poison", 8, 5), new ItemInfo("gain strength", 13, 150),
            new ItemInfo("see invisible", 3, 100), new ItemInfo("healing", 13, 130),
            new ItemInfo("monster detection", 6, 130), new ItemInfo("magic detection", 6, 105),
            new ItemInfo("raise level", 2, 250), new ItemInfo("extra healing", 5, 200),
            new ItemInfo("haste self", 5, 190), new ItemInfo("restore strength", 13, 130),
            new ItemInfo("blindness", 5, 5), new ItemInfo("levitation", 6, 75),
        };

        public static readonly ItemInfo[] Scrolls =
        {
            new ItemInfo("monster confusion", 7, 140), new ItemInfo("magic mapping", 4, 150),
            new ItemInfo("hold monster", 2, 180), new ItemInfo("sleep", 3, 5),
            new ItemInfo("enchant armor", 7, 160), new ItemInfo("identify potion", 10, 80),
            new ItemInfo("identify scroll", 10, 80), new ItemInfo("identify weapon", 6, 80),
            new ItemInfo("identify armor", 7, 100), new ItemInfo("identify ring, wand or staff", 10, 115),
            new ItemInfo("scare monster", 3, 200), new ItemInfo("food detection", 2, 60),
            new ItemInfo("teleportation", 5, 165), new ItemInfo("enchant weapon", 8, 150),
            new ItemInfo("create monster", 4, 75), new ItemInfo("remove curse", 7, 105),
            new ItemInfo("aggravate monsters", 3, 20), new ItemInfo("protect armor", 2, 250),
        };

        public static readonly ItemInfo[] Rings =
        {
            new ItemInfo("protection", 9, 400), new ItemInfo("add strength", 9, 400),
            new ItemInfo("sustain strength", 5, 280), new ItemInfo("searching", 10, 420),
            new ItemInfo("see invisible", 10, 310), new ItemInfo("adornment", 1, 10),
            new ItemInfo("aggravate monster", 10, 10), new ItemInfo("dexterity", 8, 440),
            new ItemInfo("increase damage", 8, 400), new ItemInfo("regeneration", 4, 460),
            new ItemInfo("slow digestion", 9, 240), new ItemInfo("teleportation", 5, 30),
            new ItemInfo("stealth", 7, 470), new ItemInfo("maintain armor", 5, 380),
        };

        public static readonly ItemInfo[] Sticks =
        {
            new ItemInfo("light", 12, 250), new ItemInfo("invisibility", 6, 5),
            new ItemInfo("lightning", 3, 330), new ItemInfo("fire", 3, 330),
            new ItemInfo("cold", 3, 330), new ItemInfo("polymorph", 15, 310),
            new ItemInfo("magic missile", 10, 170), new ItemInfo("haste monster", 10, 5),
            new ItemInfo("slow monster", 11, 350), new ItemInfo("drain life", 9, 300),
            new ItemInfo("nothing", 1, 5), new ItemInfo("teleport away", 6, 340),
            new ItemInfo("teleport to", 6, 50), new ItemInfo("cancellation", 5, 280),
        };

        public static readonly ItemInfo[] Armors =
        {
            new ItemInfo("leather armor", 20, 20), new ItemInfo("ring mail", 15, 25),
            new ItemInfo("studded leather armor", 15, 20), new ItemInfo("scale mail", 13, 30),
            new ItemInfo("chain mail", 12, 75), new ItemInfo("splint mail", 10, 80),
            new ItemInfo("banded mail", 10, 90), new ItemInfo("plate mail", 5, 150),
        };
        public static readonly int[] ArmorClass = { 8, 7, 7, 6, 5, 4, 4, 3 };

        public sealed class WeaponInfo
        {
            public string Name, Damage, Hurl;
            public int Launch = -1, Prob, Worth;
            public bool Many, Missile;
        }

        public static readonly WeaponInfo[] Weapons =
        {
            new WeaponInfo { Name = "mace", Damage = "2x4", Hurl = "1x3", Prob = 11, Worth = 8 },
            new WeaponInfo { Name = "long sword", Damage = "3x4", Hurl = "1x2", Prob = 11, Worth = 15 },
            new WeaponInfo { Name = "short bow", Damage = "1x1", Hurl = "1x1", Prob = 12, Worth = 15 },
            new WeaponInfo { Name = "arrow", Damage = "1x1", Hurl = "2x3", Launch = BOW, Prob = 12, Worth = 1, Many = true, Missile = true },
            new WeaponInfo { Name = "dagger", Damage = "1x6", Hurl = "1x4", Prob = 8, Worth = 3, Missile = true },
            new WeaponInfo { Name = "two handed sword", Damage = "4x4", Hurl = "1x2", Prob = 10, Worth = 75 },
            new WeaponInfo { Name = "dart", Damage = "1x1", Hurl = "1x3", Prob = 12, Worth = 2, Many = true, Missile = true },
            new WeaponInfo { Name = "shuriken", Damage = "1x2", Hurl = "2x4", Prob = 12, Worth = 5, Many = true, Missile = true },
            new WeaponInfo { Name = "spear", Damage = "2x3", Hurl = "1x6", Prob = 12, Worth = 5, Missile = true },
        };

        // Unidentified-name pools (init.c).
        public static readonly string[] Rainbow =
        {
            "amber","aquamarine","black","blue","brown","clear","crimson","cyan",
            "ecru","gold","green","grey","magenta","orange","pink","plaid","purple",
            "red","silver","tan","tangerine","topaz","turquoise","vermilion","violet",
            "white","yellow"
        };
        public static readonly (string name, int value)[] Stones =
        {
            ("agate",25),("alexandrite",40),("amethyst",50),("carnelian",40),
            ("diamond",300),("emerald",300),("germanium",225),("granite",5),
            ("garnet",50),("jade",150),("kryptonite",300),("lapis lazuli",50),
            ("moonstone",50),("obsidian",15),("onyx",60),("opal",200),("pearl",220),
            ("peridot",63),("ruby",350),("sapphire",285),("stibotantalite",200),
            ("tiger eye",50),("topaz",60),("turquoise",70),("taaffeite",300),("zircon",80)
        };
        public static readonly string[] Metal =
        {
            "aluminum","beryllium","bone","brass","bronze","copper","electrum","gold",
            "iron","lead","magnesium","mercury","nickel","pewter","platinum","steel",
            "silver","silicon","tin","titanium","tungsten","zinc"
        };
        public static readonly string[] Wood =
        {
            "avocado wood","balsa","bamboo","banyan","birch","cedar","cherry","cinnibar",
            "cypress","dogwood","driftwood","ebony","elm","eucalyptus","fall","hemlock",
            "holly","ironwood","kukui wood","mahogany","manzanita","maple","oaken",
            "persimmon wood","pecan","pine","poplar","redwood","rosewood","spruce",
            "teak","walnut","zebrawood"
        };
        public static readonly string[] Sylls =
        {
            "a","ab","ag","aks","ala","an","app","arg","arze","ash","bek","bie","bit",
            "bjor","blu","bot","bu","byt","comp","con","cos","cre","dalf","dan","den",
            "do","e","eep","el","eng","er","ere","erk","esh","evs","fa","fid","fri",
            "fu","gan","gar","glen","gop","gre","ha","hyd","i","ing","ip","ish","it",
            "ite","iv","jo","kho","kli","klis","la","lech","mar","me","mi","mic","mik",
            "mon","mung","mur","nej","nelg","nep","ner","nes","nes","nih","nin","o",
            "od","ood","org","orn","ox","oxy","pay","ple","plu","po","pot","prok","re",
            "rea","rhov","ri","ro","rog","rok","rol","sa","san","sat","sef","seh","shu",
            "ski","sna","sne","snik","sno","so","sol","sri","sta","sun","ta","tab",
            "tem","ther","ti","tox","trol","tue","turs","u","ulk","um","un","uni","ur",
            "val","viv","vly","vom","wah","wed","werg","wex","whon","wun","xo","y",
            "yot","yu","zant","zeb","zim","zok","zon","zum"
        };

        /// <summary>pick_one(): percentage table roll.</summary>
        public static int PickOne(ItemInfo[] table)
        {
            int roll = Rnd.Next(100), cum = 0;
            for (int i = 0; i < table.Length; i++)
            {
                cum += table[i].Prob;
                if (roll < cum) return i;
            }
            return 0;
        }

        public static int PickWeapon()
        {
            int roll = Rnd.Next(100), cum = 0;
            for (int i = 0; i < Weapons.Length; i++)
            {
                cum += Weapons[i].Prob;
                if (roll < cum) return i;
            }
            return 0;
        }

        public static int PickCategory()
        {
            int roll = Rnd.Next(100), cum = 0;
            for (int i = 0; i < ThingProbs.Length; i++)
            {
                cum += ThingProbs[i];
                if (roll < cum) return i;
            }
            return 0;
        }

        public static string WeaponName(Thing t) => Weapons[t.Which].Name;

        public static void InitWeapon(Thing t, int which, ref int groupCounter)
        {
            var w = Weapons[which];
            t.Kind = ThingKind.Weapon;
            t.Which = which;
            t.Damage = w.Damage;
            t.Hurl = w.Hurl;
            t.Launch = w.Launch;
            t.HPlus = t.DPlus = 0;
            if (which == DAGGER) { t.Count = Rnd.Next(4) + 2; t.Group = ++groupCounter; }
            else if (w.Many) { t.Count = Rnd.Next(8) + 8; t.Group = ++groupCounter; }
            else { t.Count = 1; t.Group = 0; }
        }

        public static void FixStick(Thing t, bool isStaff)
        {
            t.Damage = isStaff ? "2x3" : "1x1";
            t.Hurl = "1x1";
            t.Charges = t.Which == WS_LIGHT ? Rnd.Next(10) + 10 : Rnd.Next(5) + 3;
        }

        /// <summary>inv_name(): how an item reads, identified or not.</summary>
        public static string InvName(Game g, Thing t, bool drop)
        {
            string s = BaseName(g, t);
            if (drop && s.Length > 0) s = char.ToLower(s[0]) + s.Substring(1);
            else if (!drop && s.Length > 0) s = char.ToUpper(s[0]) + s.Substring(1);
            return s;
        }

        static string Plus(int n) => (n < 0 ? "" : "+") + n;

        static string BaseName(Game g, Thing t)
        {
            switch (t.Kind)
            {
                case ThingKind.Amulet: return "the Amulet of Yendor";
                case ThingKind.Food:
                    if (t.Which == 1)
                        return t.Count == 1 ? "a " + g.Fruit : t.Count + " " + g.Fruit + "s";
                    return t.Count == 1 ? "some food" : t.Count + " rations of food";
                case ThingKind.Gold: return t.GoldVal + " gold pieces";
                case ThingKind.Weapon:
                {
                    var w = Weapons[t.Which];
                    string nm = t.Count > 1 ? w.Name + "s" : w.Name;
                    string cnt = t.Count > 1 ? t.Count + " " : Article(w.Name) + " ";
                    if (t.Known) return cnt + Plus(t.HPlus) + "," + Plus(t.DPlus) + " " + nm;
                    return cnt + nm;
                }
                case ThingKind.Armor:
                {
                    string nm = Armors[t.Which].Name;
                    if (t.Known)
                        return Plus(ArmorClass[t.Which] - t.Arm) + " " + nm +
                            " [protection " + (10 - t.Arm) + "]";
                    return Article(nm) + " " + nm;
                }
                case ThingKind.Potion:
                    return NameIt(g, t, "potion", g.PotionColor(t.Which),
                        g.PotionKnown, g.PotionGuess);
                case ThingKind.Scroll:
                    if (t.Count == 1)
                        return g.ScrollKnown[t.Which] ? "A scroll of " + Scrolls[t.Which].Name
                            : g.ScrollGuess[t.Which] != null ? "A scroll called " + g.ScrollGuess[t.Which]
                            : "A scroll titled '" + g.ScrollName(t.Which) + "'";
                    return g.ScrollKnown[t.Which] ? t.Count + " scrolls of " + Scrolls[t.Which].Name
                        : g.ScrollGuess[t.Which] != null ? t.Count + " scrolls called " + g.ScrollGuess[t.Which]
                        : t.Count + " scrolls titled '" + g.ScrollName(t.Which) + "'";
                case ThingKind.Ring:
                {
                    string stone = g.RingStone(t.Which);
                    if (g.RingKnown[t.Which])
                        return "A" + (t.Known ? " " + Plus(t.Arm) : "") + " ring of " +
                            Rings[t.Which].Name + " (" + stone + ")";
                    if (g.RingGuess[t.Which] != null)
                        return "A ring called " + g.RingGuess[t.Which] + " (" + stone + ")";
                    return Article(stone) + " " + stone + " ring";
                }
                case ThingKind.Stick:
                {
                    string material = g.StickMaterial(t.Which, out bool isStaff);
                    string kind = isStaff ? "staff" : "wand";
                    if (g.StickKnown[t.Which])
                        return "A " + kind + " of " + Sticks[t.Which].Name +
                            (t.Known ? " [" + t.Charges + " charges]" : "") + " (" + material + ")";
                    if (g.StickGuess[t.Which] != null)
                        return "A " + kind + " called " + g.StickGuess[t.Which] + " (" + material + ")";
                    return Article(material) + " " + material + " " + kind;
                }
            }
            return "something";
        }

        static string NameIt(Game g, Thing t, string type, string unident,
            bool[] known, string[] guess)
        {
            if (known[t.Which])
                return t.Count == 1 ? "A " + type + " of " + Potions[t.Which].Name
                    : t.Count + " " + type + "s of " + Potions[t.Which].Name;
            if (guess[t.Which] != null)
                return t.Count == 1 ? "A " + type + " called " + guess[t.Which]
                    : t.Count + " " + type + "s called " + guess[t.Which];
            return t.Count == 1 ? Article(unident) + " " + unident + " " + type
                : t.Count + " " + unident + " " + type + "s";
        }

        static string Article(string noun) =>
            "aeiou".IndexOf(char.ToLower(noun[0])) >= 0 ? "An" : "A";
    }

    // Identification state + pack live on the Game (per run).
    public partial class Game
    {
        public string Fruit = "slime-mold";

        string[] _potColors, _scrollNames, _stickMaterial;
        bool[] _stickIsStaff;
        string[] _ringStones;
        public int[] RingWorth = new int[14];
        public bool[] PotionKnown = new bool[14];
        public bool[] ScrollKnown = new bool[18];
        public bool[] RingKnown = new bool[14];
        public bool[] StickKnown = new bool[14];
        public string[] PotionGuess = new string[14];
        public string[] ScrollGuess = new string[18];
        public string[] RingGuess = new string[14];
        public string[] StickGuess = new string[14];

        public string PotionColor(int which) => _potColors[which];
        public string ScrollName(int which) => _scrollNames[which];
        public string RingStone(int which) => _ringStones[which];
        public string StickMaterial(int which, out bool isStaff)
        { isStaff = _stickIsStaff[which]; return _stickMaterial[which]; }
        public bool StickIsStaff(int which) => _stickIsStaff[which];

        void InitNames()
        {
            _potColors = PickDistinct(Items.Rainbow, 14);
            _ringStones = new string[14];
            var usedStones = new HashSet<int>();
            for (int i = 0; i < 14; i++)
            {
                int s;
                do { s = Rnd.Next(Items.Stones.Length); } while (!usedStones.Add(s));
                _ringStones[i] = Items.Stones[s].name;
                RingWorth[i] = Items.Rings[i].Worth + Items.Stones[s].value;
            }
            _stickMaterial = new string[14];
            _stickIsStaff = new bool[14];
            var usedMetal = new HashSet<int>();
            var usedWood = new HashSet<int>();
            for (int i = 0; i < 14; i++)
            {
                if (Rnd.Next(2) == 0)
                {
                    int m; do { m = Rnd.Next(Items.Metal.Length); } while (!usedMetal.Add(m));
                    _stickMaterial[i] = Items.Metal[m];
                    _stickIsStaff[i] = false;
                }
                else
                {
                    int w; do { w = Rnd.Next(Items.Wood.Length); } while (!usedWood.Add(w));
                    _stickMaterial[i] = Items.Wood[w];
                    _stickIsStaff[i] = true;
                }
            }
            _scrollNames = new string[18];
            for (int i = 0; i < 18; i++)
            {
                var sb = new StringBuilder();
                int nwords = Rnd.Next(3) + 2;
                for (int w = 0; w < nwords; w++)
                {
                    int nsyl = Rnd.Next(3) + 1;
                    var word = new StringBuilder();
                    for (int s = 0; s < nsyl; s++)
                        word.Append(Items.Sylls[Rnd.Next(Items.Sylls.Length)]);
                    if (sb.Length + word.Length + 1 > 40) break;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(word);
                }
                _scrollNames[i] = sb.ToString();
            }
        }

        static string[] PickDistinct(string[] pool, int n)
        {
            var res = new string[n];
            var used = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int p;
                do { p = Rnd.Next(pool.Length); } while (!used.Add(p));
                res[i] = pool[p];
            }
            return res;
        }

        // ---- Pack (pack.c) ----

        public readonly List<Thing> Pack = new List<Thing>();
        readonly bool[] _packUsed = new bool[26];
        public int GroupCounter = 1; // GOLDGRP = 1; groups start at 2

        public int PackCount()
        {
            int n = 0;
            foreach (var t in Pack) n += t.Kind == ThingKind.Food ? t.Count : 1;
            return n;
        }

        public bool AddToPack(Thing t, bool silent)
        {
            // Scare-monster dust rule: a found scare scroll turns to dust.
            if (t.Kind == ThingKind.Scroll && t.Which == Items.S_SCARE && t.ScareFloor)
            {
                Msg("the scroll turns to dust as you pick it up");
                return true; // consumed (removed from floor, not added)
            }
            // Merge groups / food stacks.
            foreach (var p in Pack)
            {
                bool sameGroup = t.Group != 0 && p.Group == t.Group &&
                    p.Kind == t.Kind && p.Which == t.Which;
                bool foodStack = t.Kind == ThingKind.Food && p.Kind == ThingKind.Food
                    && p.Which == t.Which;
                bool potionStack = (t.Kind == ThingKind.Potion || t.Kind == ThingKind.Scroll)
                    && p.Kind == t.Kind && p.Which == t.Which && t.Group == 0 && p.Group == 0;
                if (sameGroup || foodStack || potionStack)
                {
                    p.Count += t.Count;
                    if (!silent) Msg("you now have {0} ({1})", Items.InvName(this, p, true), p.PackChar);
                    return true;
                }
            }
            if (PackCount() >= Const.MAXPACK)
            {
                Msg(Terse ? "no room" : "there's no room in your pack");
                return false;
            }
            for (int i = 0; i < 26; i++)
                if (!_packUsed[i]) { _packUsed[i] = true; t.PackChar = (char)('a' + i); break; }
            Pack.Add(t);
            Pack.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind)
                : a.PackChar.CompareTo(b.PackChar));
            if (t.Kind == ThingKind.Amulet) HasAmulet = true;
            if (!silent) Msg("you now have {0} ({1})", Items.InvName(this, t, true), t.PackChar);
            return true;
        }

        public void RemoveFromPack(Thing t)
        {
            Pack.Remove(t);
            if (t.PackChar >= 'a' && t.PackChar <= 'z') _packUsed[t.PackChar - 'a'] = false;
            if (t == CurWeapon) CurWeapon = null;
            if (t == CurArmor) CurArmor = null;
            if (t == LeftRing) LeftRing = null;
            if (t == RightRing) RightRing = null;
        }

        /// <summary>Splits one unit off a stacked/grouped item (throwing).</summary>
        public Thing LeavePack(Thing t)
        {
            if (t.Count > 1)
            {
                var one = new Thing
                {
                    Kind = t.Kind, Which = t.Which, Count = 1, HPlus = t.HPlus,
                    DPlus = t.DPlus, Arm = t.Arm, Charges = t.Charges,
                    Damage = t.Damage, Hurl = t.Hurl, Launch = t.Launch,
                    Group = t.Group, Known = t.Known, Cursed = t.Cursed
                };
                t.Count--;
                return one;
            }
            RemoveFromPack(t);
            return t;
        }

        // ---- new_thing (things.c) ----

        public Thing NewThing()
        {
            var t = new Thing { Arm = 11 };
            int cat = NoFood > 3 ? 2 : Items.PickCategory();
            switch (cat)
            {
                case 0:
                    t.Kind = ThingKind.Potion;
                    t.Which = Items.PickOne(Items.Potions);
                    break;
                case 1:
                    t.Kind = ThingKind.Scroll;
                    t.Which = Items.PickOne(Items.Scrolls);
                    break;
                case 2:
                    t.Kind = ThingKind.Food;
                    NoFood = 0;
                    t.Which = Rnd.Next(10) == 0 ? 1 : 0; // 10% fruit
                    break;
                case 3:
                {
                    Items.InitWeapon(t, Items.PickWeapon(), ref GroupCounter);
                    int r = Rnd.Next(100);
                    if (r < 10) { t.Cursed = true; t.HPlus -= Rnd.Next(3) + 1; }
                    else if (r < 15) t.HPlus += Rnd.Next(3) + 1;
                    break;
                }
                case 4:
                {
                    t.Kind = ThingKind.Armor;
                    t.Which = Items.PickOne(Items.Armors);
                    t.Arm = Items.ArmorClass[t.Which];
                    int r = Rnd.Next(100);
                    if (r < 20) { t.Cursed = true; t.Arm += Rnd.Next(3) + 1; }
                    else if (r < 28) t.Arm -= Rnd.Next(3) + 1;
                    break;
                }
                case 5:
                    t.Kind = ThingKind.Ring;
                    t.Which = Items.PickOne(Items.Rings);
                    switch (t.Which)
                    {
                        case Items.R_PROTECT: case Items.R_ADDSTR:
                        case Items.R_ADDHIT: case Items.R_ADDDAM:
                            t.Arm = Rnd.Next(3);
                            if (t.Arm == 0) { t.Arm = -1; t.Cursed = true; }
                            break;
                        case Items.R_AGGR: case Items.R_TELEPORT:
                            t.Cursed = true;
                            break;
                    }
                    break;
                case 6:
                    t.Kind = ThingKind.Stick;
                    t.Which = Items.PickOne(Items.Sticks);
                    Items.FixStick(t, StickIsStaff(t.Which));
                    break;
            }
            return t;
        }
    }
}
