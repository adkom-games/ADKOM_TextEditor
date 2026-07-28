// ATE Rogue 5.4.4 port — terminal layer.
// A faithful port of the original BSD Rogue (rogue.rogueforge.net) to the
// ADKOM Text Editor game API. Original game code is BSD-licensed; see
// LICENSE note in Rogue.cs. This file: an 80x24 curses-like screen over an
// AteDocument, with per-cell color and a diffing Flush so each turn writes
// only what changed.
using System;
using System.Collections.Generic;
using ADKOM.TextEditor.Scripting;
using UnityEngine;

namespace AteRogue
{
    /// <summary>80x24 character terminal over a game-mode AteDocument.
    /// Draw with At/Put, then Flush() once per turn: rows are diffed and
    /// written with WriteAt (overwrite) + SetColor runs.</summary>
    public sealed class Term
    {
        public const int W = 80, H = 24;

        readonly char[,] _ch = new char[H, W];
        readonly Color[,] _fg = new Color[H, W];
        readonly char[,] _shownCh = new char[H, W];
        readonly Color[,] _shownFg = new Color[H, W];
        bool _forceAll = true;

        public static readonly Color Default = new Color(0.85f, 0.85f, 0.85f);

        AteDocument _doc;

        public AteDocument Doc => _doc;
        public bool IsValid => _doc != null && _doc.IsValid;

        public void Attach(AteDocument doc)
        {
            _doc = doc;
            _forceAll = true;
        }

        public static AteDocument NewScreenDocument()
        {
            var blank = new System.Text.StringBuilder();
            for (int y = 0; y < H; y++)
                blank.Append(new string(' ', W)).Append(y < H - 1 ? "\n" : "");
            var doc = AteApi.NewDocument(blank.ToString());
            doc.GameMode = true;
            return doc;
        }

        public void Clear()
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                { _ch[y, x] = ' '; _fg[y, x] = Default; }
        }

        public void Put(int y, int x, char c) => Put(y, x, c, Default);

        public void Put(int y, int x, char c, Color fg)
        {
            if (y < 0 || y >= H || x < 0 || x >= W) return;
            _ch[y, x] = c;
            _fg[y, x] = fg;
        }

        public void PutStr(int y, int x, string s) => PutStr(y, x, s, Default);

        public void PutStr(int y, int x, string s, Color fg)
        {
            if (s == null) return;
            for (int i = 0; i < s.Length; i++) Put(y, x + i, s[i], fg);
        }

        /// <summary>Clears columns [x, W) of a row (message/status lines).</summary>
        public void ClearToEol(int y, int x)
        {
            for (int i = x; i < W; i++) Put(y, i, ' ');
        }

        public char CharAt(int y, int x) =>
            y >= 0 && y < H && x >= 0 && x < W ? _ch[y, x] : ' ';

        /// <summary>Writes the differences since the last Flush into the
        /// document, row by row, then repaints color runs for dirty rows.</summary>
        public void Flush()
        {
            if (!IsValid) return;
            for (int y = 0; y < H; y++)
            {
                int lo = -1, hi = -1;
                for (int x = 0; x < W; x++)
                {
                    if (!_forceAll && _ch[y, x] == _shownCh[y, x] && _fg[y, x] == _shownFg[y, x])
                        continue;
                    if (lo < 0) lo = x;
                    hi = x;
                }
                if (lo < 0) continue;

                var sb = new System.Text.StringBuilder(hi - lo + 1);
                for (int x = lo; x <= hi; x++) sb.Append(_ch[y, x]);
                _doc.WriteAt(y + 1, lo + 1, sb.ToString());

                // Color: repaint the dirty span as runs of equal color.
                int runStart = lo;
                for (int x = lo; x <= hi + 1; x++)
                {
                    if (x <= hi && _fg[y, x] == _fg[y, runStart]) continue;
                    var c = _fg[y, runStart];
                    if (c == Default)
                        _doc.SetColor(y + 1, runStart + 1, x + 1, null, null);
                    else
                        _doc.SetColor(y + 1, runStart + 1, x + 1, c);
                    runStart = x;
                }

                for (int x = lo; x <= hi; x++)
                { _shownCh[y, x] = _ch[y, x]; _shownFg[y, x] = _fg[y, x]; }
            }
            _forceAll = false;
        }

        /// <summary>Places the editor caret at the rogue's position — the
        /// classic curses cursor-on-@ feel.</summary>
        public void CursorAt(int y, int x)
        {
            if (IsValid) _doc.GoTo(y + 1, x + 1);
        }
    }

    /// <summary>Rogue's RNG (faithful shape: rnd(range) in [0, range),
    /// roll(number, sides) sums dice).</summary>
    public static class Rnd
    {
        static System.Random _r = new System.Random();

        public static void Seed(int s) => _r = new System.Random(s);
        public static int Next(int range) => range <= 0 ? 0 : _r.Next(range);
        public static int Roll(int number, int sides)
        {
            int total = 0;
            for (int i = 0; i < number; i++) total += Next(sides) + 1;
            return total;
        }
        /// <summary>Rogue's spread(): nu ± 10%.</summary>
        public static int Spread(int nu) => nu - nu / 10 + Next(nu / 5);
        /// <summary>Dice string "1d8" / "3d6/2d4" first term roll.</summary>
        public static int RollDice(string dice)
        {
            int slash = dice.IndexOf('/');
            if (slash >= 0) dice = dice.Substring(0, slash);
            int d = dice.IndexOf('d');
            int n = int.Parse(dice.Substring(0, d));
            int s = int.Parse(dice.Substring(d + 1));
            return Roll(n, s);
        }
    }
}
