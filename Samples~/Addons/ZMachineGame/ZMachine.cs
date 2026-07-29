// ATE Z-Machine interpreter (version 3) — core CPU.
//
// A CLEAN-ROOM implementation written from The Z-Machine Standards
// Document (Graham Nelson et al., the public IF-community spec). No
// Infocom code and no GPL interpreter source is used or referenced; every
// line here is original. This runs any v3 story file (.z3), the format
// Zork I/II/III compile to. Story files are supplied by the user (Open
// Story File…, or the one-click download of the MIT-licensed Zork trilogy);
// ATE never ships or redistributes a game.
//
// Folder addon: all .cs here compile into one assembly (Multi-File Addons).
using System;
using System.Collections.Generic;

namespace AteZMachine
{
    /// <summary>Screen/input surface the interpreter drives — implemented by
    /// ZScreen over the ATE game document.</summary>
    public interface IZScreen
    {
        void Print(string s);
        void SetStatus(string location, int a, int b, bool timeGame);
        void RequestLine();      // begin collecting a line of input
        void Quit(string message);
    }

    public enum ZState { Running, WaitingInput, Halted }

    public sealed partial class ZMachine
    {
        // ---- Header offsets (v3) ----
        const int H_VERSION = 0x00, H_FLAGS1 = 0x01, H_HIGHMEM = 0x04, H_PC = 0x06,
            H_DICT = 0x08, H_OBJTABLE = 0x0A, H_GLOBALS = 0x0C, H_STATICMEM = 0x0E,
            H_FLAGS2 = 0x10, H_ABBREV = 0x18, H_LENGTH = 0x1A, H_CHECKSUM = 0x1C;

        byte[] _m;
        int _pc;
        int _globals, _objTable, _dict, _abbrev, _staticBase, _highBase;
        bool _timeGame;

        readonly IZScreen _screen;
        public ZState State { get; private set; } = ZState.Halted;

        sealed class Frame
        {
            public ushort[] Locals;
            public readonly List<ushort> Eval = new List<ushort>();
            public int RetPC;
            public int StoreVar;     // -1 = discard result (call_n / interrupt)
            public Frame(int nlocals) { Locals = new ushort[nlocals]; }
        }

        readonly List<Frame> _frames = new List<Frame>();
        Frame Cur => _frames[_frames.Count - 1];

        readonly Random _rng = new Random();
        int _fixedRandom;            // >0 → deterministic "random" per spec seeding

        // sread targets, captured when input is requested.
        int _readText, _readParse;

        public ZMachine(byte[] story, IZScreen screen)
        {
            _m = story;
            _screen = screen;
            _initial = (byte[])story.Clone(); // pristine image for restart
        }

        public byte Version => _m[H_VERSION];

        public void Start()
        {
            if (Version != 3)
                throw new NotSupportedException("This interpreter supports version 3 story files (.z3). This file is version " + Version + ".");
            _globals = ReadWord(H_GLOBALS);
            _objTable = ReadWord(H_OBJTABLE);
            _dict = ReadWord(H_DICT);
            _abbrev = ReadWord(H_ABBREV);
            _staticBase = ReadWord(H_STATICMEM);
            _highBase = ReadWord(H_HIGHMEM);
            _timeGame = (ReadByte(H_FLAGS1) & 0x02) != 0;
            // Set interpreter capability flags: no status line unavailable,
            // fixed-pitch honored; clear "split available" niceties we don't do.
            WriteByte(H_FLAGS1, (byte)(ReadByte(H_FLAGS1) & ~0x10)); // status line IS available
            _pc = ReadWord(H_PC);
            _frames.Clear();
            // A dummy top frame so var 0 (stack) always has an eval stack.
            _frames.Add(new Frame(0));
            State = ZState.Running;
            Run();
        }

        // ---- Memory access (big-endian) ----
        public byte ReadByte(int a) => _m[a];
        public void WriteByte(int a, byte v) => _m[a] = v;
        public ushort ReadWord(int a) => (ushort)((_m[a] << 8) | _m[a + 1]);
        public void WriteWord(int a, ushort v) { _m[a] = (byte)(v >> 8); _m[a + 1] = (byte)v; }

        // ---- PC stream ----
        byte PB() => _m[_pc++];
        ushort PW() { ushort v = (ushort)((_m[_pc] << 8) | _m[_pc + 1]); _pc += 2; return v; }

        static short S(ushort v) => (short)v;

        // ---- Variables ----
        ushort ReadVar(int v)
        {
            if (v == 0) { var st = Cur.Eval; ushort r = st[st.Count - 1]; st.RemoveAt(st.Count - 1); return r; }
            if (v < 16) return Cur.Locals[v - 1];
            return ReadWord(_globals + 2 * (v - 16));
        }
        void WriteVar(int v, ushort val)
        {
            if (v == 0) Cur.Eval.Add(val);
            else if (v < 16) Cur.Locals[v - 1] = val;
            else WriteWord(_globals + 2 * (v - 16), val);
        }
        // Indirect (peek/poke) form for the variable-number operand of
        // load/store/inc/dec/inc_chk/dec_chk/pull (spec §6.3.4).
        ushort ReadVarInd(int v)
        {
            if (v == 0) { var st = Cur.Eval; return st[st.Count - 1]; }
            return ReadVar(v);
        }
        void WriteVarInd(int v, ushort val)
        {
            if (v == 0) { var st = Cur.Eval; st[st.Count - 1] = val; }
            else WriteVar(v, val);
        }

        // ---- Call / return ----
        void DoCall(ushort packed, ushort[] args, int argc, int storeVar)
        {
            if (packed == 0) { if (storeVar >= 0) WriteVar(storeVar, 0); return; }
            int addr = packed * 2; // v3 packed address
            int nloc = _m[addr++];
            var f = new Frame(nloc) { RetPC = _pc, StoreVar = storeVar };
            for (int i = 0; i < nloc; i++) { f.Locals[i] = (ushort)((_m[addr] << 8) | _m[addr + 1]); addr += 2; }
            for (int i = 0; i < argc && i < nloc; i++) f.Locals[i] = args[i];
            _frames.Add(f);
            _pc = addr;
        }

        void ReturnValue(ushort val)
        {
            var f = _frames[_frames.Count - 1];
            _frames.RemoveAt(_frames.Count - 1);
            _pc = f.RetPC;
            if (f.StoreVar >= 0) WriteVar(f.StoreVar, val);
        }

        // ---- Store / branch ----
        void Store(ushort val) => WriteVar(PB(), val);

        void ReadBranch(out bool on, out int offset)
        {
            byte b = PB();
            on = (b & 0x80) != 0;
            if ((b & 0x40) != 0) offset = b & 0x3F;
            else
            {
                int hi = b & 0x3F; byte b2 = PB();
                offset = (hi << 8) | b2;
                if (offset >= 0x2000) offset -= 0x4000; // 14-bit signed
            }
        }

        void ApplyBranch(bool cond, bool on, int offset)
        {
            if (cond != on) return;
            if (offset == 0) ReturnValue(0);
            else if (offset == 1) ReturnValue(1);
            else _pc = _pc + offset - 2;
        }

        void Branch(bool cond) { ReadBranch(out bool on, out int off); ApplyBranch(cond, on, off); }

        // ---- Main loop ----
        public void Run()
        {
            try
            {
                while (State == ZState.Running)
                    Step();
            }
            catch (QuitSignal q)
            {
                State = ZState.Halted;
                _screen.Quit(q.Message);
            }
            catch (Exception ex)
            {
                State = ZState.Halted;
                _screen.Print("\n[interpreter error: " + ex.Message + "]\n");
                _screen.Quit(null);
            }
        }

        sealed class QuitSignal : Exception { public string Message; public QuitSignal(string m) { Message = m; } }

        void Step()
        {
            byte op = PB();
            if (op < 0x80)
            {
                // Long form, 2OP. Types from bits 6,5 (0=small const,1=var).
                ushort a = (op & 0x40) != 0 ? ReadVar(PB()) : PB();
                ushort b = (op & 0x20) != 0 ? ReadVar(PB()) : PB();
                Exec2OP((byte)(op & 0x1F), new ushort[] { a, b }, 2);
            }
            else if (op < 0xC0)
            {
                // Short form. Bits 5,4 = operand type; 3 → 0OP.
                int t = (op >> 4) & 3;
                byte opcode = (byte)(op & 0x0F);
                if (t == 3) Exec0OP(opcode);
                else { ushort v = DecodeOperand(t); Exec1OP(opcode, v); }
            }
            else
            {
                // Variable form. Bit5=0 → 2OP dispatch, else VAR.
                bool is2 = (op & 0x20) == 0;
                byte opcode = (byte)(op & 0x1F);
                byte types = PB();
                var ops = new ushort[4]; int n = 0;
                for (int i = 0; i < 4; i++)
                {
                    int t = (types >> (6 - 2 * i)) & 3;
                    if (t == 3) break;
                    ops[n++] = DecodeOperand(t);
                }
                if (is2) Exec2OP(opcode, ops, n);
                else ExecVAR(opcode, ops, n);
            }
        }

        ushort DecodeOperand(int type)
        {
            switch (type)
            {
                case 0: return PW();            // large constant
                case 1: return PB();            // small constant
                default: return ReadVar(PB());  // variable
            }
        }
    }
}
