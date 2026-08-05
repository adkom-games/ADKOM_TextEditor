// ATE Rogue 5.4.4 port — death, quit, and victory screens (rip.c).
using System;
using UnityEngine;

namespace AteRogue
{
    public partial class Game
    {
        static readonly string[] Tombstone =
        {
            @"                       __________",
            @"                      /          \",
            @"                     /    REST    \",
            @"                    /      IN      \",
            @"                   /     PEACE      \",
            @"                  /                  \",
            @"                  |                  |",
            @"                  |                  |",
            @"                  |   killed by a    |",
            @"                  |                  |",
            @"                  |       1980       |",
            @"                 *|     *  *  *      | *",
            @"         ________)/\\_//(\/(/\)/\//\/|_)_______",
        };

        public void ShowTombstone(char cause)
        {
            Term.Clear();
            Term.ClearToEol(0, 0);
            for (int i = 0; i < Tombstone.Length; i++)
                Term.PutStr(8 - 8 + i + 8, 0, Tombstone[i]); // rows 8..20
            CenterOn(14, "rogue");
            CenterOn(15, Purse + " Au");
            string killer = KillName(cause);
            bool noArticle = cause == 's' || cause == 'h';
            if (noArticle)
                Term.PutStr(16, 26, "        "); // blank the "a" in "killed by a"
            else if (IsVowel(killer))
                Term.Put(16, 33, 'n');
            CenterOn(17, killer);
            Term.PutStr(18, 26, DateTime.Now.Year.ToString());
            Term.PutStr(22, 0, "[Press any key to leave the dungeon]");
            UpdateStatus();
            Term.Flush();
        }

        void CenterOn(int row, string s)
        {
            int col = Math.Max(0, 28 - (s.Length + 1) / 2);
            Term.PutStr(row, col, s);
        }

        static bool IsVowel(string s) =>
            s.Length > 0 && "aeiou".IndexOf(char.ToLower(s[0])) >= 0;

        public string KillName(char cause)
        {
            if (cause >= 'A' && cause <= 'Z') return Monsters.Info(cause).Name;
            return cause switch
            {
                'a' => "arrow",
                'b' => "bolt",
                'd' => "dart",
                'h' => "hypothermia",
                's' => "starvation",
                'F' => "venus flytrap",
                'W' => "wraith",
                _ => "Wally the Wonder Badger"
            };
        }

        public void ShowQuitScreen()
        {
            Term.Clear();
            Term.ClearToEol(0, 0);
            Term.PutStr(10, 20, string.Format("You quit with {0} gold pieces", Purse));
            Term.PutStr(22, 0, "[Press any key to leave the dungeon]");
            Term.Flush();
        }

        /// <summary>total_winner(): escaped with the Amulet — value the loot.</summary>
        public void TotalWinner()
        {
            GameOver = true;
            Term.Clear();
            Term.ClearToEol(0, 0);
            Term.PutStr(1, 10, @"                                _______________");
            Term.PutStr(2, 10, @"You made it!  \_______________/");
            Term.PutStr(4, 0, "Congratulations, you have made it to the light of day!");
            Term.PutStr(6, 0, "You have joined the elite ranks of those who have escaped the");
            Term.PutStr(7, 0, "Dungeons of Doom alive.  You journey home and sell all your loot at");
            Term.PutStr(8, 0, "a great profit and are admitted to the Fighters' Guild.");
            int row = 10;
            Term.PutStr(row++, 0, "   Worth  Item");
            long total = 0;
            foreach (var t in Pack)
            {
                long worth = 0;
                switch (t.Kind)
                {
                    case ThingKind.Food: worth = 2 * t.Count; break;
                    case ThingKind.Weapon:
                        worth = Items.Weapons[t.Which].Worth * (3 * (t.HPlus + t.DPlus) + t.Count);
                        t.Known = true;
                        break;
                    case ThingKind.Armor:
                        worth = Items.Armors[t.Which].Worth + (9 - t.Arm) * 100 +
                            10 * (Items.ArmorClass[t.Which] - t.Arm);
                        t.Known = true;
                        break;
                    case ThingKind.Scroll:
                        worth = Items.Scrolls[t.Which].Worth * t.Count;
                        if (!ScrollKnown[t.Which]) worth /= 2;
                        break;
                    case ThingKind.Potion:
                        worth = Items.Potions[t.Which].Worth * t.Count;
                        if (!PotionKnown[t.Which]) worth /= 2;
                        break;
                    case ThingKind.Ring:
                        worth = RingWorth[t.Which];
                        if ((t.Which == Items.R_ADDSTR || t.Which == Items.R_ADDDAM ||
                             t.Which == Items.R_PROTECT || t.Which == Items.R_ADDHIT))
                        {
                            if (t.Arm > 0) worth += t.Arm * 100;
                            else worth = 10;
                        }
                        if (!RingKnown[t.Which]) worth /= 2;
                        break;
                    case ThingKind.Stick:
                        worth = Items.Sticks[t.Which].Worth + 20 * t.Charges;
                        if (!StickKnown[t.Which]) worth /= 2;
                        break;
                    case ThingKind.Amulet: worth = 1000; break;
                }
                if (worth < 0) worth = 0;
                total += worth;
                if (row < Const.STATLINE - 2)
                    Term.PutStr(row++, 0, string.Format("{0}) {1,5}  {2}",
                        t.PackChar, worth, Items.InvName(this, t, false)));
            }
            Purse += total;
            Term.PutStr(row++, 0, string.Format("   {0,5}  Gold Pieces", Purse));
            Term.PutStr(Const.STATLINE - 1, 0, "[Press any key to leave the dungeon]");
            Term.Flush();
            Mode = InputMode.EndScreen;
        }
    }
}
