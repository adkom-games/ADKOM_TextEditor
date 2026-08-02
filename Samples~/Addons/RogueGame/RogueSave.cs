// ATE Rogue 5.4.4 port — domain-reload persistence (AteApi 1.2).
//
// Serializes the WHOLE Game object graph by reflection into a Base64 string
// the host stores (addons never touch disk). What round-trips: every
// instance field of Game and the reachable Thing/Room/Monster/Stats objects
// — maps, rooms, monsters (with their packs), the player's pack with intact
// object identity (CurWeapon stays the same object as its pack entry), the
// scrambled identification names, hunger, levels, everything.
//
// What deliberately does NOT round-trip:
//   - delegates (input continuations, OnQuitRequested) — the input state
//     machine resumes in Play mode, dropping any half-answered prompt;
//   - the Term (rebound to the surviving document on restore);
//   - the Scheduler's actions (slots ride as name+time descriptors and are
//     re-bound by name — Game.ResolveSchedAction);
//   - transient message state (Queue/StringBuilder) and the RNG (re-seeded:
//     future die rolls differ after a resume, which is harmless).
//
// Field values are written name-tagged; any mismatch with the running code
// (e.g. a different addon version) fails the restore cleanly and the addon
// just starts fresh.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace AteRogue
{
    public partial class Game
    {
        /// <summary>Scheduler slot names → their actions, for re-binding
        /// after a restore. Every name used by Daemon()/Fuse() is here.</summary>
        public Action ResolveSchedAction(string name) => name switch
        {
            "doctor" => (Action)Doctor,
            "rollwand" => RollWand,
            "stomach" => Stomach,
            "swander" => StartWanderer,
            "nohaste" => NoHaste,
            "sight" => Sight,
            "unconfuse" => Unconfuse,
            "unsee" => Unsee,
            "turnsee" => () => ClearP(MF.SEEMONST),
            _ => null
        };
    }

    public static class RogueSave
    {
        const uint Magic = 0x41524753; // "ARGS" — Ate Rogue Game State
        const int Version = 1;

        // Class types that participate in the identity graph. The type index
        // is part of the format — append only.
        static readonly Type[] GraphTypes = { typeof(Thing), typeof(Room), typeof(Monster), typeof(Stats) };

        static bool SkipField(FieldInfo f) =>
            typeof(Delegate).IsAssignableFrom(f.FieldType) ||
            f.FieldType == typeof(Term) ||
            f.FieldType == typeof(Scheduler) ||
            f.FieldType == typeof(StringBuilder) ||
            (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Queue<>));

        static FieldInfo[] FieldsOf(Type t)
        {
            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return fields;
        }

        // ---- Serialize ----

        public static string Serialize(Game g)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Magic);
                w.Write(Version);
                var ids = new Dictionary<object, int>();
                WriteFields(w, g, ids);
                var slots = g.Sched.Export();
                w.Write(slots.Count);
                foreach (var s in slots) { w.Write(s.Name); w.Write(s.Time); w.Write(s.Daemon); }
                w.Flush();
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        static void WriteFields(BinaryWriter w, object obj, Dictionary<object, int> ids)
        {
            foreach (var f in FieldsOf(obj.GetType()))
            {
                if (SkipField(f)) continue;
                w.Write(f.Name);
                WriteValue(w, f.FieldType, f.GetValue(obj), ids);
            }
            w.Write(""); // field-list terminator
        }

        static void WriteValue(BinaryWriter w, Type t, object v, Dictionary<object, int> ids)
        {
            if (t == typeof(bool)) w.Write((bool)v);
            else if (t == typeof(int)) w.Write((int)v);
            else if (t == typeof(long)) w.Write((long)v);
            else if (t == typeof(char)) w.Write((char)v);
            else if (t == typeof(string)) { w.Write(v != null); if (v != null) w.Write((string)v); }
            else if (t.IsEnum) w.Write(Convert.ToInt64(v));
            else if (Nullable.GetUnderlyingType(t) is Type ut)
            {
                w.Write(v != null);
                if (v != null) WriteValue(w, ut, v, ids);
            }
            else if (t.IsArray)
            {
                var a = (Array)v;
                w.Write(a != null);
                if (a == null) return;
                int rank = a.Rank;
                w.Write(rank);
                for (int d = 0; d < rank; d++) w.Write(a.GetLength(d));
                var et = t.GetElementType();
                foreach (var item in a) WriteValue(w, et, item, ids);
            }
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)v;
                w.Write(list != null);
                if (list == null) return;
                w.Write(list.Count);
                var et = t.GetGenericArguments()[0];
                foreach (var item in list) WriteValue(w, et, item, ids);
            }
            else if (t.IsClass) WriteRef(w, v, ids);
            else if (t.IsValueType) // generic struct (Coord): recurse fields
            {
                foreach (var f in FieldsOf(t))
                    WriteValue(w, f.FieldType, f.GetValue(v), ids);
            }
            else throw new NotSupportedException("RogueSave: field type " + t.Name);
        }

        static void WriteRef(BinaryWriter w, object v, Dictionary<object, int> ids)
        {
            if (v == null) { w.Write((byte)0); return; }
            if (ids.TryGetValue(v, out int id)) { w.Write((byte)1); w.Write(id); return; }
            int typeIdx = Array.IndexOf(GraphTypes, v.GetType());
            if (typeIdx < 0) throw new NotSupportedException("RogueSave: object type " + v.GetType().Name);
            ids[v] = ids.Count;
            w.Write((byte)2);
            w.Write((byte)typeIdx);
            WriteFields(w, v, ids);
        }

        // ---- Restore ----

        /// <summary>Restores a Serialize string into a freshly constructed
        /// Game (whose Term is already attached). Returns false — leaving the
        /// game unusable — on any mismatch; callers then start fresh.</summary>
        public static bool Restore(string data, Game g)
        {
            try
            {
                var bytes = Convert.FromBase64String(data);
                using (var r = new BinaryReader(new MemoryStream(bytes)))
                {
                    if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) return false;
                    var objs = new List<object>();
                    ReadFields(r, g, objs);
                    int n = r.ReadInt32();
                    var slots = new List<(string, int, bool)>(n);
                    for (int i = 0; i < n; i++)
                        slots.Add((r.ReadString(), r.ReadInt32(), r.ReadBoolean()));
                    g.Sched.Import(slots, g.ResolveSchedAction);
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        static void ReadFields(BinaryReader r, object obj, List<object> objs)
        {
            var fields = new Dictionary<string, FieldInfo>();
            foreach (var f in FieldsOf(obj.GetType()))
                if (!SkipField(f)) fields[f.Name] = f;
            for (string name = r.ReadString(); name.Length > 0; name = r.ReadString())
            {
                if (!fields.TryGetValue(name, out var f))
                    throw new InvalidDataException("RogueSave: unknown field " + name);
                object cur = f.GetValue(obj);
                object val = ReadValue(r, f.FieldType, cur, objs);
                if (!ReferenceEquals(val, cur)) f.SetValue(obj, val);
            }
        }

        static object ReadValue(BinaryReader r, Type t, object existing, List<object> objs)
        {
            if (t == typeof(bool)) return r.ReadBoolean();
            if (t == typeof(int)) return r.ReadInt32();
            if (t == typeof(long)) return r.ReadInt64();
            if (t == typeof(char)) return r.ReadChar();
            if (t == typeof(string)) return r.ReadBoolean() ? r.ReadString() : null;
            if (t.IsEnum) return Enum.ToObject(t, r.ReadInt64());
            if (Nullable.GetUnderlyingType(t) is Type ut)
                return r.ReadBoolean() ? ReadValue(r, ut, null, objs) : null;
            if (t.IsArray)
            {
                if (!r.ReadBoolean()) return null;
                int rank = r.ReadInt32();
                var lens = new int[rank];
                for (int d = 0; d < rank; d++) lens[d] = r.ReadInt32();
                var et = t.GetElementType();
                // Reuse the existing array (readonly fields: Map, flags, …)
                // when the dimensions match; otherwise make a fresh one.
                var a = existing as Array;
                bool fits = a != null && a.Rank == rank;
                for (int d = 0; fits && d < rank; d++) fits = a.GetLength(d) == lens[d];
                if (!fits) a = Array.CreateInstance(et, lens);
                if (rank == 1)
                    for (int i = 0; i < lens[0]; i++) a.SetValue(ReadValue(r, et, null, objs), i);
                else if (rank == 2)
                    for (int i = 0; i < lens[0]; i++)
                        for (int j = 0; j < lens[1]; j++)
                            a.SetValue(ReadValue(r, et, null, objs), i, j);
                else throw new NotSupportedException("RogueSave: array rank " + rank);
                return a;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!r.ReadBoolean()) return null;
                int count = r.ReadInt32();
                var et = t.GetGenericArguments()[0];
                // Refill the existing list in place (readonly fields: Pack,
                // Exits, MonstersOnLevel, …).
                var list = existing as IList ?? (IList)Activator.CreateInstance(t);
                list.Clear();
                for (int i = 0; i < count; i++) list.Add(ReadValue(r, et, null, objs));
                return list;
            }
            if (t.IsClass) return ReadRef(r, objs);
            if (t.IsValueType)
            {
                object boxed = Activator.CreateInstance(t);
                foreach (var f in FieldsOf(t))
                    f.SetValue(boxed, ReadValue(r, f.FieldType, null, objs));
                return boxed;
            }
            throw new NotSupportedException("RogueSave: field type " + t.Name);
        }

        static object ReadRef(BinaryReader r, List<object> objs)
        {
            byte kind = r.ReadByte();
            if (kind == 0) return null;
            if (kind == 1) return objs[r.ReadInt32()];
            var type = GraphTypes[r.ReadByte()];
            object obj = Activator.CreateInstance(type);
            objs.Add(obj);
            ReadFields(r, obj, objs);
            return obj;
        }
    }
}
