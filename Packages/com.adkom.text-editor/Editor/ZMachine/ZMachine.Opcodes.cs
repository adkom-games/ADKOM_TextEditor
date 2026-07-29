#if UNITY_EDITOR
// ATE Z-Machine (v3) — opcode implementations, from the Standards Document.
using System;

namespace AteZMachine
{
    public sealed partial class ZMachine
    {
        void Exec2OP(byte opcode, ushort[] o, int n)
        {
            switch (opcode)
            {
                case 0x01: // je
                {
                    bool eq = false;
                    for (int i = 1; i < n; i++) if (o[0] == o[i]) eq = true;
                    Branch(eq);
                    break;
                }
                case 0x02: Branch(S(o[0]) < S(o[1])); break;   // jl
                case 0x03: Branch(S(o[0]) > S(o[1])); break;   // jg
                case 0x04: // dec_chk
                {
                    short v = (short)(S(ReadVarInd(o[0])) - 1);
                    WriteVarInd(o[0], (ushort)v);
                    Branch(v < S(o[1]));
                    break;
                }
                case 0x05: // inc_chk
                {
                    short v = (short)(S(ReadVarInd(o[0])) + 1);
                    WriteVarInd(o[0], (ushort)v);
                    Branch(v > S(o[1]));
                    break;
                }
                case 0x06: Branch(GetParent(o[0]) == o[1]); break;             // jin
                case 0x07: Branch((o[0] & o[1]) == o[1]); break;              // test
                case 0x08: Store((ushort)(o[0] | o[1])); break;               // or
                case 0x09: Store((ushort)(o[0] & o[1])); break;               // and
                case 0x0A: Branch(TestAttr(o[0], o[1])); break;              // test_attr
                case 0x0B: SetAttr(o[0], o[1], true); break;                  // set_attr
                case 0x0C: SetAttr(o[0], o[1], false); break;                 // clear_attr
                case 0x0D: WriteVarInd(o[0], o[1]); break;                    // store
                case 0x0E: InsertObj(o[0], o[1]); break;                      // insert_obj
                case 0x0F: Store(ReadWord(o[0] + 2 * o[1])); break;          // loadw
                case 0x10: Store(_m[o[0] + o[1]]); break;                     // loadb
                case 0x11: Store(GetProp(o[0], o[1])); break;                // get_prop
                case 0x12: Store((ushort)GetPropAddr(o[0], o[1])); break;    // get_prop_addr
                case 0x13: Store(GetNextProp(o[0], o[1])); break;            // get_next_prop
                case 0x14: Store((ushort)(S(o[0]) + S(o[1]))); break;         // add
                case 0x15: Store((ushort)(S(o[0]) - S(o[1]))); break;         // sub
                case 0x16: Store((ushort)(S(o[0]) * S(o[1]))); break;         // mul
                case 0x17: Store((ushort)(S(o[1]) == 0 ? 0 : S(o[0]) / S(o[1]))); break; // div
                case 0x18: Store((ushort)(S(o[1]) == 0 ? 0 : S(o[0]) % S(o[1]))); break; // mod
                default: throw new Exception("bad 2OP opcode 0x" + opcode.ToString("x2"));
            }
        }

        void Exec1OP(byte opcode, ushort a)
        {
            switch (opcode)
            {
                case 0x00: Branch(a == 0); break;                             // jz
                case 0x01: // get_sibling
                {
                    ushort sib = GetSibling(a);
                    Store(sib); Branch(sib != 0);
                    break;
                }
                case 0x02: // get_child
                {
                    ushort ch = GetChild(a);
                    Store(ch); Branch(ch != 0);
                    break;
                }
                case 0x03: Store(GetParent(a)); break;                        // get_parent
                case 0x04: Store((ushort)GetPropLen(a)); break;              // get_prop_len
                case 0x05: WriteVarInd(a, (ushort)(S(ReadVarInd(a)) + 1)); break; // inc
                case 0x06: WriteVarInd(a, (ushort)(S(ReadVarInd(a)) - 1)); break; // dec
                case 0x07: PrintString(DecodeString(a, out _)); break;        // print_addr
                case 0x09: RemoveObj(a); break;                              // remove_obj
                case 0x0A: PrintString(ObjectName(a)); break;                // print_obj
                case 0x0B: ReturnValue(a); break;                            // ret
                case 0x0C: // jump (signed offset, not a branch)
                {
                    short off = S(a);
                    _pc = _pc + off - 2;
                    break;
                }
                case 0x0D: PrintString(DecodeString(a * 2, out _)); break;    // print_paddr
                case 0x0E: Store(ReadVarInd(a)); break;                      // load
                case 0x0F: Store((ushort)~a); break;                         // not (v1-4)
                default: throw new Exception("bad 1OP opcode 0x" + opcode.ToString("x2"));
            }
        }

        void Exec0OP(byte opcode)
        {
            switch (opcode)
            {
                case 0x00: ReturnValue(1); break;                            // rtrue
                case 0x01: ReturnValue(0); break;                            // rfalse
                case 0x02: PrintString(DecodeStringInline()); break;         // print
                case 0x03: PrintString(DecodeStringInline()); PrintString("\n"); ReturnValue(1); break; // print_ret
                case 0x04: break;                                            // nop
                case 0x05: DoSave(); break;                                  // save (branch)
                case 0x06: DoRestore(); break;                              // restore (branch)
                case 0x07: Restart(); break;                                 // restart
                case 0x08: ReturnValue(PopStack()); break;                   // ret_popped
                case 0x09: PopStack(); break;                                // pop
                case 0x0A: throw new QuitSignal(null);                       // quit
                case 0x0B: PrintString("\n"); break;                         // new_line
                case 0x0C: ShowStatus(); break;                              // show_status (v3)
                case 0x0D: Branch(Verify()); break;                          // verify
                default: throw new Exception("bad 0OP opcode 0x" + opcode.ToString("x2"));
            }
        }

        void ExecVAR(byte opcode, ushort[] o, int n)
        {
            switch (opcode)
            {
                case 0x00: // call (call_vs)
                {
                    var args = new ushort[Math.Max(0, n - 1)];
                    for (int i = 1; i < n; i++) args[i - 1] = o[i];
                    DoCall(o[0], args, n - 1, PB());
                    break;
                }
                case 0x01: WriteWord(o[0] + 2 * o[1], o[2]); break;          // storew
                case 0x02: WriteByte(o[0] + o[1], (byte)o[2]); break;         // storeb
                case 0x03: PutProp(o[0], o[1], o[2]); break;                 // put_prop
                case 0x04: BeginRead(o[0], n > 1 ? o[1] : 0); break;         // sread (v3)
                case 0x05: PrintZChar(o[0]); break;                         // print_char
                case 0x06: PrintString(S(o[0]).ToString()); break;           // print_num
                case 0x07: Store(DoRandom(S(o[0]))); break;                  // random
                case 0x08: Cur.Eval.Add(o[0]); break;                        // push
                case 0x09: WriteVarInd(o[0], PopStack()); break;             // pull
                case 0x0A: _screen.Print(""); break;                         // split_window (ignored: single window)
                case 0x0B: break;                                            // set_window (ignored)
                case 0x13: OutputStream(S(o[0]), n > 1 ? o[1] : (ushort)0); break; // output_stream
                case 0x14: break;                                            // input_stream (ignored)
                case 0x15: break;                                            // sound_effect (ignored)
                default: throw new Exception("bad VAR opcode 0x" + opcode.ToString("x2"));
            }
        }

        ushort PopStack()
        {
            var st = Cur.Eval;
            ushort v = st[st.Count - 1];
            st.RemoveAt(st.Count - 1);
            return v;
        }

        ushort DoRandom(short range)
        {
            if (range == 0) { _fixedRandom = 0; return 0; }               // seed randomly
            if (range < 0) { _fixedRandom = -range; _fixedSeq = 0; return 0; } // predictable seed
            if (_fixedRandom > 0)
            {
                _fixedSeq = (_fixedSeq % _fixedRandom) + 1;
                return (ushort)_fixedSeq;
            }
            return (ushort)(_rng.Next(range) + 1);
        }
        int _fixedSeq;

        bool Verify()
        {
            int len = ReadWord(H_LENGTH) * 2; // v3: length field is /2
            if (len == 0 || len > _m.Length) len = _m.Length;
            int sum = 0;
            for (int i = 0x40; i < len; i++) sum = (sum + _m[i]) & 0xFFFF;
            return sum == ReadWord(H_CHECKSUM);
        }

        // ---- Output streams (screen + memory table) ----
        int _stream3Addr = -1;

        void OutputStream(short which, ushort table)
        {
            if (which == 3) _stream3Addr = table;      // select memory stream
            else if (which == -3) FinishStream3();     // deselect
            // streams 1/2/others: screen stays on; transcript not implemented
        }

        void FinishStream3()
        {
            if (_stream3Addr < 0) return;
            // length word already maintained by PrintRaw; just close.
            _stream3Addr = -1;
        }

        void PrintString(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (_stream3Addr >= 0)
            {
                int len = ReadWord(_stream3Addr);
                int p = _stream3Addr + 2 + len;
                foreach (char c in s) { WriteByte(p++, (byte)ZsciiOut(c)); len++; }
                WriteWord(_stream3Addr, (ushort)len);
                return;
            }
            _screen.Print(s);
        }

        void PrintZChar(ushort z) => PrintString(((char)ZsciiToUnicode(z)).ToString());
        int ZsciiOut(char c) => c == '\n' ? 13 : c;
    }
}

#endif
