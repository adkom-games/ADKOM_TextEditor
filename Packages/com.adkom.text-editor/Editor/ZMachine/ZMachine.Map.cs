#if UNITY_EDITOR
// ATE Z-Machine — internal accessors the auto-mapper reads (current room,
// object tree, object count). Observation only — nothing here changes
// execution.
namespace AteZMachine
{
    public sealed partial class ZMachine
    {
        /// <summary>True once Start() has set up the memory pointers — the
        /// mapper must not read the object table before that.</summary>
        internal bool MapReady() => _globals != 0 && _objTable != 0;

        /// <summary>The current room object (global variable 0 in v3 — the
        /// same value the status line names).</summary>
        internal int MapCurrentRoom() => ReadVar(16);

        /// <summary>Is this object the value of any global variable? The
        /// player/actor object is typically held in a global, which lets the
        /// mapper identify it on turn 1 (so it is never shown as an item).</summary>
        internal bool MapReferencedByGlobal(int obj)
        {
            for (int v = 16; v <= 255; v++) if (ReadVar(v) == obj) return true;
            return false;
        }

        internal string MapObjectName(int obj) => obj <= 0 || obj > MapMaxObject() ? "" : ObjectName(obj);
        internal int MapParent(int obj) => obj <= 0 ? 0 : GetParent(obj);
        internal int MapChild(int obj) => obj <= 0 ? 0 : GetChild(obj);
        internal int MapSibling(int obj) => obj <= 0 ? 0 : GetSibling(obj);
        internal bool MapAttr(int obj, int attr) => TestAttr(obj, attr);

        int _maxObjCache = -1;

        /// <summary>Object count (v3): the object entries run from object 1 up
        /// to the first property table, 9 bytes each.</summary>
        internal int MapMaxObject()
        {
            if (_maxObjCache > 0) return _maxObjCache;
            int first = ObjAddr(1);
            int firstProp = ReadWord(first + 7);
            int n = (firstProp - first) / OBJ_ENTRY;
            _maxObjCache = n > 0 && n <= 255 ? n : 255;
            return _maxObjCache;
        }
    }
}
#endif
