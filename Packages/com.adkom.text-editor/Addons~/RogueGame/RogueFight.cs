// ATE Rogue 5.4.4 port — combat (fight.c). Exact tables and formulas.
using System;
using System.Collections.Generic;

namespace AteRogue
{
    public partial class Game
    {
        // str_plus[str] hit bonus, add_dam[str] damage bonus (fight.c).
        static readonly int[] StrPlus =
        { -7,-6,-5,-4,-3,-2,-1,0,0,0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2,2,2,2,2,2,2,3 };
        static readonly int[] AddDam =
        { -7,-6,-5,-4,-3,-2,-1,0,0,0,0,0,0,0,0,0,1,1,2,3,3,4,5,5,5,5,5,5,5,5,5,6 };

        public static readonly long[] ELevels =
        { 10,20,40,80,160,320,640,1300,2600,5200,13000,26000,50000,100000,
          200000,400000,800000,2000000,4000000,8000000 };

        /// <summary>swing(): does an attack at this level with this bonus hit
        /// a defender of this armor class?</summary>
        static bool Swing(int atLvl, int opArm, int wplus)
        {
            int res = Rnd.Next(20);
            int need = (20 - atLvl) - opArm;
            return res + wplus >= need;
        }

        /// <summary>save_throw for the player (VS_MAGIC reduced by worn
        /// protection rings).</summary>
        public bool Save(int which)
        {
            if (which == Const.VS_MAGIC)
            {
                if (LeftRing != null && LeftRing.Which == Items.R_PROTECT) which -= LeftRing.Arm;
                if (RightRing != null && RightRing.Which == Items.R_PROTECT) which -= RightRing.Arm;
            }
            return SaveThrow(which, Player.Lvl);
        }

        public static bool SaveThrow(int which, int level) =>
            Rnd.Roll(1, 20) >= 14 + which - level / 2;

        /// <summary>roll_em(): the paired attack roll; att/def are stats,
        /// weap the attacker's weapon (null = natural attack).</summary>
        bool RollEm(Stats att, Stats def, Thing weap, bool hurl, bool defIsPlayer, bool defAsleep)
        {
            string dmgString;
            int hplus = 0, dplus = 0;
            if (weap == null) dmgString = att.Dmg;
            else
            {
                hplus = weap.HPlus; dplus = weap.DPlus;
                if (weap == CurWeapon)
                {
                    if (LeftRing != null && LeftRing.Which == Items.R_ADDDAM) dplus += LeftRing.Arm;
                    else if (LeftRing != null && LeftRing.Which == Items.R_ADDHIT) hplus += LeftRing.Arm;
                    if (RightRing != null && RightRing.Which == Items.R_ADDDAM) dplus += RightRing.Arm;
                    else if (RightRing != null && RightRing.Which == Items.R_ADDHIT) hplus += RightRing.Arm;
                }
                dmgString = weap.Damage;
                if (hurl)
                {
                    if (weap.Launch >= 0 && CurWeapon != null && CurWeapon.Kind == ThingKind.Weapon
                        && CurWeapon.Which == weap.Launch)
                    {
                        dmgString = weap.Hurl;
                        hplus += CurWeapon.HPlus;
                        dplus += CurWeapon.DPlus;
                    }
                    else if (weap.Launch < 0) dmgString = weap.Hurl;
                }
            }
            if (defAsleep) hplus += 4;
            int defArm = def.Arm;
            if (defIsPlayer)
            {
                if (CurArmor != null) defArm = CurArmor.Arm;
                if (LeftRing != null && LeftRing.Which == Items.R_PROTECT) defArm -= LeftRing.Arm;
                if (RightRing != null && RightRing.Which == Items.R_PROTECT) defArm -= RightRing.Arm;
            }
            bool didHit = false;
            foreach (var term in dmgString.Split('/'))
            {
                int xi = term.IndexOf('x');
                if (xi < 0) continue;
                int n = int.Parse(term.Substring(0, xi));
                int s = int.Parse(term.Substring(xi + 1));
                if (Swing(att.Lvl, defArm, hplus + StrPlus[Clamp031(att.Str)]))
                {
                    int damage = dplus + Rnd.Roll(n, s) + AddDam[Clamp031(att.Str)];
                    def.Hpt -= Math.Max(0, damage);
                    didHit = true;
                }
            }
            return didHit;
        }

        static int Clamp031(int str) => Math.Max(0, Math.Min(31, str));

        // ---- Player attacks a monster ----

        /// <summary>fight(): player attacks the monster at mp. Returns
        /// whether the turn was consumed by a xeroc reveal.</summary>
        public bool Fight(Monster m, Thing weap, bool thrown)
        {
            CountRepeat = 0; Quiet = 0;
            RunTo(m);
            if (m.Type == 'X' && m.Disguise != 'X' && !HasP(MF.ISBLIND))
            {
                m.Disguise = 'X';
                Msg(HasP(MF.ISHALU) ? "heavy!  That's a nasty critter!"
                                    : "wait!  That's a xeroc!");
                if (!thrown) return true; // the reveal eats the melee turn
            }
            string mname = MonsterName(m);
            if (RollEm(Player, m.Stats, weap, thrown, defIsPlayer: false, defAsleep: !m.Has(MF.ISRUN)))
            {
                if (thrown) Msg(Terse ? "the {0} hits" : "the {0} hits {1}",
                    weap != null ? Items.WeaponName(weap) : "missile", mname);
                else HitMsg(true, "you", mname);
                if (m.Stats.Hpt <= 0) { Killed(m, true); return false; }
                if (HasP(MF.CANHUH))
                {
                    m.Set(MF.ISHUH);
                    ClearP(MF.CANHUH);
                    Msg("your hands stop glowing red");
                    if (!HasP(MF.ISBLIND)) Msg("the {0} appears confused", mname);
                }
            }
            else
            {
                if (thrown) Msg(Terse ? "the {0} misses" : "the {0} misses {1}",
                    weap != null ? Items.WeaponName(weap) : "missile", mname);
                else MissMsg(true, "you", mname);
            }
            return false;
        }

        // ---- Monster attacks the player ----

        public void Attack(Monster m)
        {
            CountRepeat = 0; Quiet = 0;
            StopRunning();
            if (m.Type == 'X' && m.Disguise != 'X') m.Disguise = 'X';
            string mname = MonsterName(m);
            var mstats = m.Stats;
            if (m.Type == 'F')
                mstats = new Stats { Lvl = m.Stats.Lvl, Str = 10, Arm = m.Stats.Arm,
                    Dmg = m.VfDamage + "x1" };
            if (RollEm(mstats, Player, null, false, defIsPlayer: true, defAsleep: false))
            {
                if (m.Type != 'I') HitMsg(false, "the " + mname, "you");
                if (Player.Hpt <= 0) { Death(m.Type); return; }
                if (!m.Has(MF.ISCANC)) SpecialAttack(m, mname);
            }
            else
            {
                if (m.Type == 'F')
                {
                    Player.Hpt -= m.VfDamage;
                    if (Player.Hpt <= 0) { Death('F'); return; }
                }
                if (m.Type != 'I') MissMsg(false, "the " + mname, "you");
            }
            UpdateStatus();
        }

        void SpecialAttack(Monster m, string mname)
        {
            switch (m.Type)
            {
                case 'A': // aquator rusts armor
                    if (CurArmor == null || CurArmor.Kind != ThingKind.Armor) break;
                    if (CurArmor.Which == Items.A_LEATHER || CurArmor.Arm >= 9) break;
                    if (CurArmor.IsProtected || WearingRing(Items.R_SUSTARM))
                    { if (!ToDeath) Msg("the rust vanishes instantly"); }
                    else { CurArmor.Arm++; Msg(Terse ? "your armor weakens"
                        : "your armor appears to be weaker now. Oh my!"); }
                    break;
                case 'I': // ice monster freeze
                    StopRunning();
                    if (NoCommand == 0)
                        Msg(HasP(MF.ISBLIND) ? "you are frozen" : "you are frozen by the " + mname);
                    NoCommand += Rnd.Next(2) + 2;
                    if (NoCommand > Const.BORE_LEVEL) Death('h'); // 'h' = hypothermia
                    break;
                case 'R': // rattlesnake str drain
                    if (!Save(Const.VS_POISON))
                    {
                        if (!WearingRing(Items.R_SUSTSTR))
                        { ChgStr(-1); Msg(Terse ? "a bite has weakened you"
                            : "you feel a bite in your leg and now feel weaker"); }
                        else if (!ToDeath) Msg(Terse ? "bite has no effect"
                            : "a bite momentarily weakens you");
                    }
                    break;
                case 'W': case 'V': // wraith / vampire drain
                    if (Rnd.Next(100) < (m.Type == 'W' ? 15 : 30))
                    {
                        int fewer;
                        if (m.Type == 'W')
                        {
                            if (Player.Exp == 0) { Death('W'); return; }
                            Player.Lvl--;
                            if (Player.Lvl == 0) { Player.Exp = 0; Player.Lvl = 1; }
                            else Player.Exp = ELevels[Player.Lvl - 1] + 1;
                            fewer = Rnd.Roll(1, 10);
                        }
                        else fewer = Rnd.Roll(1, 3);
                        Player.Hpt -= fewer;
                        Player.MaxHp -= fewer;
                        if (Player.Hpt < 1) Player.Hpt = 1;
                        if (Player.MaxHp <= 0) { Death(m.Type); return; }
                        Msg("you suddenly feel weaker");
                    }
                    break;
                case 'F': // flytrap grips
                    SetP(MF.ISHELD);
                    m.VfDamage++;
                    Player.Hpt--;
                    if (Player.Hpt <= 0) Death('F');
                    break;
                case 'L': // leprechaun steals gold
                {
                    long lastPurse = Purse;
                    Purse -= GoldCalc();
                    if (!Save(Const.VS_MAGIC))
                        Purse -= GoldCalc() + GoldCalc() + GoldCalc() + GoldCalc();
                    if (Purse < 0) Purse = 0;
                    RemoveMonster(m, silent: true);
                    if (Purse != lastPurse) Msg("your purse feels lighter");
                    break;
                }
                case 'N': // nymph steals a magic item
                {
                    var candidates = new List<Thing>();
                    foreach (var t in Pack)
                    {
                        if (t == CurArmor || t == CurWeapon || t == LeftRing || t == RightRing) continue;
                        if (t.Kind == ThingKind.Potion || t.Kind == ThingKind.Scroll ||
                            t.Kind == ThingKind.Ring || t.Kind == ThingKind.Stick ||
                            t.Kind == ThingKind.Amulet) candidates.Add(t);
                    }
                    if (candidates.Count > 0)
                    {
                        var steal = candidates[Rnd.Next(candidates.Count)];
                        RemoveMonster(m, silent: true);
                        RemoveFromPack(steal);
                        Msg("she stole {0}!", Items.InvName(this, steal, false));
                    }
                    break;
                }
            }
        }

        public int GoldCalc() => Rnd.Next(50 + 10 * LevelNum) + 2;

        /// <summary>killed(): exp, flytrap release, leprechaun drop, message,
        /// level check.</summary>
        public void Killed(Monster m, bool byPlayer)
        {
            if (byPlayer) Player.Exp += m.Stats.Exp;
            switch (m.Type)
            {
                case 'F':
                    ClearP(MF.ISHELD);
                    break;
                case 'L':
                    if (LevelNum >= MaxLevel)
                    {
                        int gold = GoldCalc();
                        if (Save(Const.VS_MAGIC)) gold += GoldCalc() + GoldCalc() + GoldCalc() + GoldCalc();
                        DropGoldAt(m.Pos, gold);
                    }
                    break;
            }
            string mname = MonsterName(m);
            RemoveMonster(m, silent: true);
            if (byPlayer)
            {
                Msg(Terse ? "defeated the {0}" : "you have defeated the {0}", mname);
                CheckLevel();
            }
        }

        public void CheckLevel()
        {
            int i;
            for (i = 0; i < ELevels.Length; i++)
                if (ELevels[i] > Player.Exp) break;
            i++; // levels are 1-based
            if (i > Player.Lvl)
            {
                int add = Rnd.Roll(i - Player.Lvl, 10);
                Player.MaxHp += add;
                Player.Hpt += add;
                Player.Lvl = i;
                Msg("welcome to level {0}", i);
            }
            else Player.Lvl = i;
        }

        public void RaiseLevel()
        {
            Player.Exp = Player.Lvl >= 1 && Player.Lvl <= ELevels.Length
                ? ELevels[Player.Lvl - 1] + 1 : Player.Exp + 1;
            CheckLevel();
        }

        // ---- Combat messages (fight.c h_names/m_names) ----

        static readonly string[] HitP = { " scored an excellent hit on ", " hit ", " have injured ", " swing and hit " };
        static readonly string[] HitM = { " scored an excellent hit on ", " hit ", " has injured ", " swings and hits " };
        static readonly string[] MissP = { " miss", " swing and miss", " barely miss", " don't hit" };
        static readonly string[] MissM = { " misses", " swings and misses", " barely misses", " doesn't hit" };

        void HitMsg(bool byPlayer, string attacker, string defender)
        {
            string verb = (byPlayer ? HitP : HitM)[Terse ? 1 : Rnd.Next(4)];
            Msg(Cap(attacker) + verb.TrimEnd() + (Terse ? "" : " " + defender));
        }

        void MissMsg(bool byPlayer, string attacker, string defender)
        {
            string verb = (byPlayer ? MissP : MissM)[Terse ? 0 : Rnd.Next(4)];
            Msg(Cap(attacker) + verb + (Terse ? "" : " " + defender));
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        /// <summary>The monster's display name ("it" when unseeable).</summary>
        public string MonsterName(Monster m)
        {
            if (!CanSeeMonster(m)) return "it";
            if (HasP(MF.ISHALU)) return Monsters.Table[Rnd.Next(26)].Name;
            return Monsters.Info(m.Type).Name;
        }

        public bool WearingRing(int which) =>
            (LeftRing != null && LeftRing.Which == which) ||
            (RightRing != null && RightRing.Which == which);
    }
}
