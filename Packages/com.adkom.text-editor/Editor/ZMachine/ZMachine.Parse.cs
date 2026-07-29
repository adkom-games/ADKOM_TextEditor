#if UNITY_EDITOR
// ATE Z-Machine (v3) — sread input, tokeniser, and status line.
using System;
using System.Collections.Generic;

namespace AteZMachine
{
    public sealed partial class ZMachine
    {
        // v3 dictionary header: byte n = number of word separators, then n
        // separator bytes, then entry-length byte, then entry-count word,
        // then the sorted entries.
        int _dictEntryLen, _dictCount, _dictEntries;
        char[] _separators;

        void EnsureDict()
        {
            if (_separators != null) return;
            int p = _dict;
            int nsep = _m[p++];
            _separators = new char[nsep];
            for (int i = 0; i < nsep; i++) _separators[i] = (char)_m[p++];
            _dictEntryLen = _m[p++];
            _dictCount = ReadWord(p); p += 2;
            _dictEntries = p;
        }

        void BeginRead(int textBuf, int parseBuf)
        {
            _readText = textBuf;
            _readParse = parseBuf;
            ShowStatus();
            State = ZState.WaitingInput;
            _screen.RequestLine();
        }

        /// <summary>Called by the addon when the player has entered a line.
        /// Fills the text and parse buffers, then resumes execution.</summary>
        public void CompleteInput(string line)
        {
            if (State != ZState.WaitingInput) return;
            line = (line ?? "").ToLowerInvariant();
            int max = _m[_readText];              // v3: byte 0 = max letters
            if (line.Length > max) line = line.Substring(0, max);
            int p = _readText + 1;
            foreach (char c in line) WriteByte(p++, (byte)c);
            WriteByte(p, 0);                       // v3 terminator

            if (_readParse != 0) Tokenise(line);

            State = ZState.Running;
            Run();
        }

        void Tokenise(string line)
        {
            EnsureDict();
            int maxWords = _m[_readParse];
            var tokens = new List<(string w, int pos)>();
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == ' ') { i++; continue; }
                if (Array.IndexOf(_separators, c) >= 0) { tokens.Add((c.ToString(), i)); i++; continue; }
                int start = i;
                var sb = new System.Text.StringBuilder();
                while (i < line.Length && line[i] != ' ' && Array.IndexOf(_separators, line[i]) < 0)
                    sb.Append(line[i++]);
                tokens.Add((sb.ToString(), start));
            }

            int count = Math.Min(tokens.Count, maxWords);
            WriteByte(_readParse + 1, (byte)count);
            int e = _readParse + 2;
            for (int t = 0; t < count; t++)
            {
                int dictAddr = LookupDict(tokens[t].w);
                WriteWord(e, (ushort)dictAddr);
                WriteByte(e + 2, (byte)tokens[t].w.Length);
                WriteByte(e + 3, (byte)(tokens[t].pos + 1)); // 1-based text position
                e += 4;
            }
        }

        int LookupDict(string word)
        {
            EnsureDict();
            byte[] enc = EncodeWord(word);
            // Entries are sorted; a linear scan is plenty fast for one turn.
            for (int k = 0; k < _dictCount; k++)
            {
                int addr = _dictEntries + k * _dictEntryLen;
                if (_m[addr] == enc[0] && _m[addr + 1] == enc[1] &&
                    _m[addr + 2] == enc[2] && _m[addr + 3] == enc[3])
                    return addr;
            }
            return 0; // not in dictionary
        }

        // ---- Status line (v3) ----
        void ShowStatus()
        {
            int locObj = ReadVar(16);            // global 0 → current room object
            string loc = ObjectName(locObj);
            int a = S(ReadVar(17));              // score / hours
            int b = S(ReadVar(18));              // moves / minutes
            _screen.SetStatus(loc, a, b, _timeGame);
        }
    }
}

#endif
