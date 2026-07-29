// ATE Z-Machine (v3) — ZSCII text decode/encode, from the Standards Document.
using System;
using System.Collections.Generic;
using System.Text;

namespace AteZMachine
{
    public sealed partial class ZMachine
    {
        // Default alphabets, index 0 == Z-char 6.
        const string A0 = "abcdefghijklmnopqrstuvwxyz";
        const string A1 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        // A2 index 0 (Z-char 6) is the 10-bit escape (handled specially),
        // index 1 (Z-char 7) is newline; rest are digits/punctuation.
        const string A2 = "\0\n0123456789.,!?_#'\"/\\-:()";

        /// <summary>Decodes the packed string at a byte address; sets
        /// <paramref name="next"/> to the address just past it.</summary>
        string DecodeString(int addr, out int next)
        {
            var sb = new StringBuilder();
            addr = DecodeInto(addr, sb, allowAbbrev: true);
            next = addr;
            return sb.ToString();
        }

        /// <summary>Decodes the literal string beginning at the PC (print /
        /// print_ret) and advances the PC past it.</summary>
        string DecodeStringInline()
        {
            var sb = new StringBuilder();
            _pc = DecodeInto(_pc, sb, allowAbbrev: true);
            return sb.ToString();
        }

        int DecodeInto(int addr, StringBuilder sb, bool allowAbbrev)
        {
            int alphabet = 0;        // 0=A0, 1=A1, 2=A2 (temporary shift)
            int abbrevPhase = 0;     // 1/2/3 → collecting abbreviation index
            int escapePhase = 0;     // 1 → high 5 bits, 2 → low 5 bits
            int escapeHigh = 0;
            bool end = false;
            while (!end)
            {
                ushort w = ReadWord(addr); addr += 2;
                end = (w & 0x8000) != 0;
                int[] zc = { (w >> 10) & 0x1F, (w >> 5) & 0x1F, w & 0x1F };
                foreach (int z in zc)
                {
                    if (escapePhase == 1) { escapeHigh = z; escapePhase = 2; continue; }
                    if (escapePhase == 2) { sb.Append((char)ZsciiToUnicode((escapeHigh << 5) | z)); escapePhase = 0; alphabet = 0; continue; }
                    if (abbrevPhase != 0)
                    {
                        int index = 32 * (abbrevPhase - 1) + z;
                        int entry = 2 * ReadWord(_abbrev + 2 * index);
                        var tmp = new StringBuilder();
                        DecodeInto(entry, tmp, allowAbbrev: false);
                        sb.Append(tmp);
                        abbrevPhase = 0;
                        continue;
                    }
                    if (z == 0) { sb.Append(' '); alphabet = 0; continue; }
                    if (z >= 1 && z <= 3) { if (allowAbbrev) { abbrevPhase = z; } alphabet = 0; continue; }
                    if (z == 4) { alphabet = 1; continue; }   // shift A1 (next char)
                    if (z == 5) { alphabet = 2; continue; }   // shift A2 (next char)
                    // z 6..31 → alphabet[z-6]
                    if (alphabet == 2 && z == 6) { escapePhase = 1; alphabet = 0; continue; } // 10-bit escape
                    if (alphabet == 2 && z == 7) { sb.Append('\n'); alphabet = 0; continue; }
                    string alpha = alphabet == 0 ? A0 : alphabet == 1 ? A1 : A2;
                    sb.Append(alpha[z - 6]);
                    alphabet = 0;
                }
            }
            return addr;
        }

        static int ZsciiToUnicode(int z)
        {
            if (z == 13 || z == 10) return '\n';
            if (z == 9) return ' ';
            if (z >= 32 && z <= 126) return z;
            // Latin-1 extras (155-251) → approximate to ASCII where common.
            return z >= 155 && z <= 251 ? '?' : z;
        }

        // ---- Encoding for dictionary lookup (sread tokenisation) ----

        /// <summary>Encodes a lowercased word to the v3 dictionary form:
        /// 6 Z-chars packed into 4 bytes (2 words), top bit set on word 2.</summary>
        byte[] EncodeWord(string word)
        {
            var zchars = new List<int>(6);
            foreach (char ch in word)
            {
                if (zchars.Count >= 6) break;
                char c = char.ToLowerInvariant(ch);
                int i;
                if ((i = A0.IndexOf(c)) >= 0) zchars.Add(i + 6);
                else if ((i = A2.IndexOf(c)) >= 1) { zchars.Add(5); if (zchars.Count < 6) zchars.Add(i + 6); }
                else { zchars.Add(5); if (zchars.Count < 6) zchars.Add(6); } // unknown → A2 escape-ish; harmless mismatch
            }
            while (zchars.Count < 6) zchars.Add(5); // pad
            ushort w1 = (ushort)((zchars[0] << 10) | (zchars[1] << 5) | zchars[2]);
            ushort w2 = (ushort)((zchars[3] << 10) | (zchars[4] << 5) | zchars[5] | 0x8000);
            return new byte[] { (byte)(w1 >> 8), (byte)w1, (byte)(w2 >> 8), (byte)w2 };
        }
    }
}
