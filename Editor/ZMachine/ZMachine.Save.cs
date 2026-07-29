#if UNITY_EDITOR
// ATE Z-Machine (v3) — save / restore / restart.
//
// Save format is ATE-internal (not Quetzal): dynamic memory, the whole call
// stack, and the PC. It only has to round-trip with this interpreter.
using System;
using System.IO;

namespace AteZMachine
{
    public sealed partial class ZMachine
    {
        /// <summary>Set by the addon so the interpreter can pop a file panel
        /// at save/restore time (returns a path, or null to cancel).</summary>
        public Func<bool, string> ChooseSaveFile;

        readonly byte[] _initial; // captured for restart

        void DoSave()
        {
            ReadBranch(out bool on, out int off);
            int target = ComputeTarget(on, off);
            bool ok = false;
            try
            {
                string path = ChooseSaveFile?.Invoke(true);
                if (!string.IsNullOrEmpty(path))
                {
                    using (var w = new BinaryWriter(File.Create(path)))
                    {
                        w.Write(new byte[] { (byte)'A', (byte)'Z', (byte)'S', 3 });
                        w.Write(_staticBase);
                        w.Write(_m, 0, _staticBase);        // dynamic memory
                        w.Write(target);                    // resume PC
                        w.Write(_frames.Count);
                        foreach (var f in _frames)
                        {
                            w.Write(f.RetPC); w.Write(f.StoreVar);
                            w.Write(f.Locals.Length);
                            foreach (var l in f.Locals) w.Write(l);
                            w.Write(f.Eval.Count);
                            foreach (var e in f.Eval) w.Write(e);
                        }
                    }
                    ok = true;
                }
            }
            catch (Exception ex) { _screen.Print("\n[save failed: " + ex.Message + "]\n"); }
            ApplyTarget(ok, on, off, target);
        }

        void DoRestore()
        {
            ReadBranch(out bool on, out int off);
            try
            {
                string path = ChooseSaveFile?.Invoke(false);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    using (var r = new BinaryReader(File.OpenRead(path)))
                    {
                        var magic = r.ReadBytes(4);
                        if (magic.Length != 4 || magic[0] != 'A' || magic[1] != 'Z' || magic[2] != 'S')
                            throw new Exception("not an ATE Z-machine save");
                        int dyn = r.ReadInt32();
                        var mem = r.ReadBytes(dyn);
                        Array.Copy(mem, 0, _m, 0, Math.Min(dyn, _staticBase));
                        _pc = r.ReadInt32();
                        int nf = r.ReadInt32();
                        _frames.Clear();
                        for (int i = 0; i < nf; i++)
                        {
                            int retpc = r.ReadInt32(), sv = r.ReadInt32(), nl = r.ReadInt32();
                            var f = new Frame(nl) { RetPC = retpc, StoreVar = sv };
                            for (int j = 0; j < nl; j++) f.Locals[j] = r.ReadUInt16();
                            int ne = r.ReadInt32();
                            for (int j = 0; j < ne; j++) f.Eval.Add(r.ReadUInt16());
                            _frames.Add(f);
                        }
                        return; // resumed at saved PC; do NOT take restore's branch
                    }
                }
            }
            catch (Exception ex) { _screen.Print("\n[restore failed: " + ex.Message + "]\n"); }
            // Cancelled or failed → fall through (branch on true, cond false).
            ApplyBranch(false, on, off);
        }

        void Restart()
        {
            Array.Copy(_initial, _m, _initial.Length);
            _frames.Clear();
            _frames.Add(new Frame(0));
            _separators = null;
            _stream3Addr = -1;
            Start();
        }

        int ComputeTarget(bool on, int off)
        {
            // Where a taken branch would land (used to serialise the resume PC).
            if (off == 0 || off == 1) return _pc;
            return _pc + off - 2;
        }

        void ApplyTarget(bool cond, bool on, int off, int target)
        {
            if (cond == on) _pc = target;
        }
    }
}

#endif
