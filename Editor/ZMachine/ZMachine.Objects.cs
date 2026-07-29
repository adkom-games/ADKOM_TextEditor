#if UNITY_EDITOR
// ATE Z-Machine (v3) — object table & properties, from the Standards Document.
namespace AteZMachine
{
    public sealed partial class ZMachine
    {
        // v3: 31 property defaults (2 bytes each) precede the object entries;
        // each object entry is 9 bytes (4 attr + parent + sibling + child + 2 prop-addr).
        const int PROP_DEFAULTS = 31, OBJ_ENTRY = 9;

        int ObjAddr(int obj) => _objTable + 2 * PROP_DEFAULTS + (obj - 1) * OBJ_ENTRY;

        ushort GetParent(int obj) => _m[ObjAddr(obj) + 4];
        ushort GetSibling(int obj) => _m[ObjAddr(obj) + 5];
        ushort GetChild(int obj) => _m[ObjAddr(obj) + 6];
        void SetParent(int obj, int v) => _m[ObjAddr(obj) + 4] = (byte)v;
        void SetSibling(int obj, int v) => _m[ObjAddr(obj) + 5] = (byte)v;
        void SetChild(int obj, int v) => _m[ObjAddr(obj) + 6] = (byte)v;

        bool TestAttr(int obj, int attr)
        {
            if (obj == 0) return false;
            int b = ObjAddr(obj) + attr / 8;
            return (_m[b] & (0x80 >> (attr % 8))) != 0;
        }
        void SetAttr(int obj, int attr, bool set)
        {
            if (obj == 0) return;
            int b = ObjAddr(obj) + attr / 8;
            int mask = 0x80 >> (attr % 8);
            _m[b] = (byte)(set ? _m[b] | mask : _m[b] & ~mask);
        }

        void RemoveObj(int obj)
        {
            if (obj == 0) return;
            int parent = GetParent(obj);
            if (parent == 0) return;
            int child = GetChild(parent);
            if (child == obj) SetChild(parent, GetSibling(obj));
            else
            {
                int c = child;
                while (c != 0 && GetSibling(c) != obj) c = GetSibling(c);
                if (c != 0) SetSibling(c, GetSibling(obj));
            }
            SetParent(obj, 0);
            SetSibling(obj, 0);
        }

        void InsertObj(int obj, int dest)
        {
            if (obj == 0) return;
            RemoveObj(obj);
            SetParent(obj, dest);
            SetSibling(obj, GetChild(dest));
            SetChild(dest, obj);
        }

        int PropTableAddr(int obj) => ReadWord(ObjAddr(obj) + 7);

        string ObjectName(int obj)
        {
            if (obj == 0) return "";
            int p = PropTableAddr(obj);
            int textLen = _m[p]; // words in the short name
            if (textLen == 0) return "";
            return DecodeString(p + 1, out _);
        }

        // Property entries in v3: a size byte (32*(len-1) + prop#), then data,
        // in descending property-number order, terminated by a 0 size byte.
        int FirstPropAddr(int obj)
        {
            int p = PropTableAddr(obj);
            return p + 1 + 2 * _m[p]; // skip text-length byte + short name
        }

        static int PropNum(byte size) => size & 0x1F;
        static int PropSize(byte size) => (size >> 5) + 1;

        int GetPropAddr(int obj, int prop)
        {
            if (obj == 0) return 0;
            int p = FirstPropAddr(obj);
            while (_m[p] != 0)
            {
                byte size = _m[p];
                if (PropNum(size) == prop) return p + 1;
                if (PropNum(size) < prop) break; // descending order
                p += 1 + PropSize(size);
            }
            return 0;
        }

        ushort GetProp(int obj, int prop)
        {
            int a = GetPropAddr(obj, prop);
            if (a == 0) return ReadWord(_objTable + 2 * (prop - 1)); // default
            byte size = _m[a - 1];
            return PropSize(size) == 1 ? _m[a] : ReadWord(a);
        }

        void PutProp(int obj, int prop, ushort val)
        {
            int a = GetPropAddr(obj, prop);
            if (a == 0) return;
            byte size = _m[a - 1];
            if (PropSize(size) == 1) _m[a] = (byte)val; else WriteWord(a, val);
        }

        ushort GetNextProp(int obj, int prop)
        {
            if (obj == 0) return 0;
            int p = FirstPropAddr(obj);
            if (prop == 0) return (ushort)(_m[p] == 0 ? 0 : PropNum(_m[p]));
            while (_m[p] != 0)
            {
                byte size = _m[p];
                if (PropNum(size) == prop)
                {
                    int np = p + 1 + PropSize(size);
                    return (ushort)(_m[np] == 0 ? 0 : PropNum(_m[np]));
                }
                p += 1 + PropSize(size);
            }
            return 0;
        }

        int GetPropLen(int propAddr)
        {
            if (propAddr == 0) return 0;
            return PropSize(_m[propAddr - 1]);
        }
    }
}

#endif
