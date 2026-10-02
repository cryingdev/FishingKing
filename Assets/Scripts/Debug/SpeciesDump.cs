using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace FishingKing
{
    /// <summary>
    /// Every value of the species and stages as text, one "path = value" line each (-fkauto species writes it as
    /// species_dump.txt; diff two dumps to see what a data change did). It walks the public instance fields by reflection,
    /// into the encounter rows (moods, choreography, top view, key rules), the habitats' arrays and every dictionary in its
    /// enumeration order; a float prints as its bit pattern and its round-trip text, so equal lines mean equal bits.
    /// </summary>
    public static class SpeciesDump
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public static List<string> Lines(IList<FishSpecies> fish, IList<StageDef> stages)
        {
            var o = new List<string>();
            var ids = new List<string>();
            foreach (var f in fish) ids.Add(f.id);
            o.Add("fish.order = " + string.Join(",", ids));
            foreach (var f in fish) Walk($"fish[{f.id}]", f, o, 0);
            var sids = new List<string>();
            foreach (var s in stages) sids.Add(s.id);
            o.Add("stages.order = " + string.Join(",", sids));
            foreach (var s in stages) Walk($"stage[{s.id}]", s, o, 0);
            return o;
        }

        public static string Write(IList<FishSpecies> fish, IList<StageDef> stages) => string.Join("\n", Lines(fish, stages)) + "\n";

        static string F(float v) => $"{BitConverter.ToInt32(BitConverter.GetBytes(v), 0):x8} ({v.ToString("R", CI)})";

        static void Walk(string path, object v, List<string> o, int depth)
        {
            if (depth > 12)
            {
                o.Add(path + " = <too deep>");
                return;
            }
            if (v == null)
            {
                o.Add(path + " = null");
                return;
            }
            var t = v.GetType();
            switch (v)
            {
                case float f: o.Add(path + " = " + F(f)); return;
                case double d: o.Add(path + " = " + d.ToString("R", CI)); return;
                case string s: o.Add(path + " = \"" + s + "\""); return;
                case bool b: o.Add(path + " = " + (b ? "true" : "false")); return;
                case Enum e: o.Add(path + " = " + t.Name + "." + e); return;
            }
            if (t.IsPrimitive)
            {
                o.Add(path + " = " + Convert.ToString(v, CI));
                return;
            }
            if (v is Array arr)
            {
                if (arr.Rank == 2)
                {
                    o.Add($"{path}.size = {arr.GetLength(0)}x{arr.GetLength(1)}");
                    for (int i = 0; i < arr.GetLength(0); i++)
                    for (int j = 0; j < arr.GetLength(1); j++)
                        Walk($"{path}[{i},{j}]", arr.GetValue(i, j), o, depth + 1);
                    return;
                }
                o.Add($"{path}.count = {arr.Length}");
                for (int i = 0; i < arr.Length; i++) Walk($"{path}[{i}]", arr.GetValue(i), o, depth + 1);
                return;
            }
            if (v is IDictionary dict)
            {
                o.Add($"{path}.count = {dict.Count}");
                int i = 0;
                foreach (DictionaryEntry kv in dict)
                {
                    o.Add($"{path}#{i}.key = {Convert.ToString(kv.Key, CI)}");
                    Walk($"{path}[{Convert.ToString(kv.Key, CI)}]", kv.Value, o, depth + 1);
                    i++;
                }
                return;
            }
            if (v is IList list)
            {
                o.Add($"{path}.count = {list.Count}");
                for (int i = 0; i < list.Count; i++) Walk($"{path}[{i}]", list[i], o, depth + 1);
                return;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Walk(path + ".key", t.GetProperty("Key").GetValue(v), o, depth + 1);
                Walk(path + ".value", t.GetProperty("Value").GetValue(v), o, depth + 1);
                return;
            }
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
            // (a struct that keeps its values private, e.g. Vector2Int: its text)
            if (fields.Length == 0 && t.IsValueType)
            {
                o.Add(path + " = " + Convert.ToString(v, CI));
                return;
            }
            foreach (var fi in fields)
                Walk(path + "." + fi.Name, fi.GetValue(v), o, depth + 1);
        }
    }
}
