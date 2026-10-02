using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FishingKing
{
    /// <summary>One problem found in the species / stage data: an error (fails the validator and the build) or a warning.</summary>
    public sealed class DataFinding
    {
        public bool error;
        /// <summary>The file it is in (relative to Resources/Data, e.g. "Fish/carp.json"), or "" for the whole set.</summary>
        public string file;
        /// <summary>The validator rule (E1 .. E12 for errors, W for warnings; Docs/data_reference.md 2.7).</summary>
        public string rule;
        public string message;

        public override string ToString() => $"{rule} {file}: {message}";
    }

    /// <summary>
    /// The species and stage data (Docs/data_reference.md 1, 2): one file per species, Resources/Data/Fish/&lt;id&gt;.json, and
    /// the stage roster Resources/Data/stages.json, which lists each stage's fish (the species exposed there, with their
    /// spawn weights). <see cref="Build"/> turns the texts into the game's FishSpecies and StageDef objects, the same ones
    /// the old C# tables made, in the same order; it never throws and reports what it could not read as findings (a
    /// species with a broken file is left out). GameDatabase loads it at start-up; the validator (SpeciesCheck) and the
    /// tests run it on their own texts. Files are plain JSON (UTF-8 without a BOM), read by the small reader below rather
    /// than JsonUtility: the loader must tell an absent key from a default value and see every key (JsonUtility drops
    /// unknown ones silently), and numbers keep their text.
    /// </summary>
    public static class SpeciesData
    {
        /// <summary>Resources paths: the species folder (nothing else may live in it) and the roster.</summary>
        public const string FishDir = "Data/Fish", StagesPath = "Data/stages";

        /// <summary>A data file's text: its name without the extension (a species file is named after its id) and whether it starts with a BOM.</summary>
        public sealed class Source
        {
            public string name, text;
            public bool bom;
        }

        public sealed class Result
        {
            public readonly List<FishSpecies> fish = new List<FishSpecies>();
            public readonly List<StageDef> stages = new List<StageDef>();
            public readonly List<DataFinding> findings = new List<DataFinding>();
            /// <summary>For the validator: the parsed species files by id (only those that loaded) and their file names.</summary>
            public readonly Dictionary<string, JNode> speciesJson = new Dictionary<string, JNode>();
            /// <summary>Every species id that has a file (loaded or not).</summary>
            public readonly HashSet<string> fileIds = new HashSet<string>();
            /// <summary>For the validator: species id -> the stages that list it (once per listing, in roster order).</summary>
            public readonly Dictionary<string, List<string>> stagesOf = new Dictionary<string, List<string>>();
            public JNode stagesJson;

            public int ErrorCount
            {
                get
                {
                    int n = 0;
                    foreach (var f in findings) if (f.error) n++;
                    return n;
                }
            }
        }

        // ---- the keys each object may carry (anything else is an error: E2); the species' "art" block is free (reserved
        // for the Blender kwargs), "note" is free text
        public static readonly string[] SpeciesKeys =
        {
            "id", "name", "desc", "rarity", "minCm", "maxCm", "weightK", "basePrice", "power", "stamina", "speed", "aggression", "jump",
            "jumpStyle", "depthMin", "depthMax", "baits", "activity", "cover", "diet", "habitat", "pocketHold", "encounter", "note", "art",
        };
        public static readonly string[] ActivityKeys = { "dawn", "day", "evening", "night" };
        public static readonly string[] CoverKeys = { "seek", "reach", "dig", "types", "note" };
        public static readonly string[] DietKeys = { "foods", "style", "note" };
        public static readonly string[] RosterKeys = { "stages" };
        public static readonly string[] StageKeys =
        {
            "id", "name", "subtitle", "difficulty", "reqLevel", "unlockCost", "powerMult", "biteMult", "population", "fish", "note",
        };
        public static readonly string[] StageFishKeys = { "id", "weight" };

        public static readonly Regex IdPattern = new Regex("^[a-z][a-z0-9_]*$");

        /// <summary>The roster and the species files as the game ships them (Resources).</summary>
        public static Source FromResources(out List<Source> fish)
        {
            fish = new List<Source>();
            foreach (var t in Resources.LoadAll<TextAsset>(FishDir)) fish.Add(Src(t));
            var st = Resources.Load<TextAsset>(StagesPath);
            return st != null ? Src(st) : null;
        }

        static Source Src(TextAsset t)
        {
            var b = t.bytes;
            return new Source { name = t.name, text = t.text, bom = b != null && b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF };
        }

        /// <summary>
        /// The species and stages from the roster and the species files. The species come in roster order (each stage's fish
        /// in turn, a species where it is first listed), then any species no stage lists in id order; the stages in roster
        /// order, each stage's spawns in its list's order (FishSpawner.Pick's draw walks them in it).
        /// </summary>
        public static Result Build(Source stages, IEnumerable<Source> files)
        {
            var r = new Result();
            var byId = new Dictionary<string, FishSpecies>();
            try
            {
                foreach (var src in files ?? new Source[0])
                {
                    if (src == null) continue;
                    r.fileIds.Add(src.name);
                    try
                    {
                        var sp = MapSpecies(src, r);
                        if (sp == null) continue;
                        if (byId.ContainsKey(sp.id))
                        {
                            Err(r, "Fish/" + src.name + ".json", "E1", $"duplicate species id '{sp.id}'");
                            continue;
                        }
                        byId[sp.id] = sp;
                    }
                    catch (Exception e)
                    {
                        Err(r, "Fish/" + src.name + ".json", "E1", "could not be read: " + e.Message);
                    }
                }
                MapStages(stages, r, byId);
                // the species in roster order, then the ones no stage lists (in id order, so saves keep them)
                var placed = new HashSet<string>();
                foreach (var st in r.stages)
                foreach (var kv in st.spawns)
                    if (byId.TryGetValue(kv.Key, out var sp) && placed.Add(sp.id)) r.fish.Add(sp);
                var rest = new List<string>();
                foreach (var id in byId.Keys) if (!placed.Contains(id)) rest.Add(id);
                rest.Sort(StringComparer.Ordinal);
                foreach (var id in rest) r.fish.Add(byId[id]);
            }
            catch (Exception e)
            {
                Err(r, "", "E1", "the species data could not be read: " + e);
            }
            return r;
        }

        static void Err(Result r, string file, string rule, string msg) =>
            r.findings.Add(new DataFinding { error = true, file = file, rule = rule, message = msg });

        // ------------------------------------------------------------------------------------ species
        static FishSpecies MapSpecies(Source src, Result r)
        {
            string file = "Fish/" + src.name + ".json";
            // (E3 missing / bad values and E10 a bad legend link leave the species out; the rest is reported and it loads)
            bool hard = false;
            void E(string rule, string msg)
            {
                Err(r, file, rule, msg);
                if (rule == "E3" || rule == "E10") hard = true;
            }
            if (src.bom) E("E1", "starts with a BOM (save as UTF-8 without a BOM)");
            var root = DataJson.Parse(src.text, out string perr);
            if (root == null)
            {
                E("E1", "not valid JSON: " + perr);
                return null;
            }
            if (root.kind != JNode.Kind.Object)
            {
                E("E1", "must be one JSON object");
                return null;
            }
            CheckKeys(root, SpeciesKeys, "", E);
            string id = ReqStr(root, "id", "", E);
            if (id == null) return null;
            if (id != src.name) E("E1", $"id '{id}' differs from the file name '{src.name}'");
            if (!IdPattern.IsMatch(id)) E("E1", $"id '{id}' must be lower case letters, digits and _ (starting with a letter)");
            var f = new FishSpecies
            {
                id = id,
                name = ReqStr(root, "name", "", E),
                desc = ReqStr(root, "desc", "", E),
                rarity = ReqEnum(root, "rarity", "", E, Rarity.Common),
                minCm = ReqNum(root, "minCm", "", E),
                maxCm = ReqNum(root, "maxCm", "", E),
                weightK = ReqNum(root, "weightK", "", E),
                basePrice = ReqInt(root, "basePrice", "", E),
                power = ReqNum(root, "power", "", E),
                stamina = ReqNum(root, "stamina", "", E),
                speed = ReqNum(root, "speed", "", E),
                aggression = ReqNum(root, "aggression", "", E),
                jump = ReqNum(root, "jump", "", E),
                depthMin = ReqNum(root, "depthMin", "", E),
                depthMax = ReqNum(root, "depthMax", "", E),
            };
            // how it behaves in the air: required for a jumper (jump > 0); a non-jumper keeps the default (Hopper)
            if (root.Has("jumpStyle")) f.jumpStyle = ReqEnum(root, "jumpStyle", "", E, JumpStyle.Hopper);
            else if (f.jump > 0f) E("E3", "jumpStyle is required when jump > 0 (hopper | shaker | tailWalker)");
            string baits = ReqStr(root, "baits", "", E);
            if (baits != null)
            {
                try
                {
                    ParsePrefs(f, baits, m => E("E5", m));
                }
                catch (Exception e)
                {
                    E("E5", $"baits '{baits}' could not be read ({e.Message})");
                }
            }
            // the activity by period (Docs/time_currents_spec.md 6), in Period order
            var act = ReqObj(root, "activity", "", E);
            if (act != null)
            {
                CheckKeys(act, ActivityKeys, "activity.", E);
                f.activity = new float[4];
                for (int p = 0; p < 4; p++) f.activity[p] = ReqNum(act, ActivityKeys[p], "activity.", E);
            }
            // cover (Docs/obstacles_spec.md 7.1): no types = never seeks cover (coverFor stays null, coverDig 1)
            var cov = ReqObj(root, "cover", "", E);
            if (cov != null)
            {
                CheckKeys(cov, CoverKeys, "cover.", E);
                var types = ReqStrArray(cov, "types", "cover.", E);
                if (types != null && types.Length > 0)
                {
                    f.coverSeek = ReqNum(cov, "seek", "cover.", E);
                    f.coverReach = ReqNum(cov, "reach", "cover.", E);
                    f.coverDig = ReqNum(cov, "dig", "cover.", E);
                    f.coverFor = types;
                }
                else if (types != null)
                {
                    foreach (var k in new[] { "seek", "reach", "dig" })
                        if (cov.Has(k)) E("E3", $"cover.{k} without cover types (give the types, or leave seek, reach and dig out)");
                }
            }
            // the aquarium (AquaCare): the foods it eats and how it takes a dropped piece
            var diet = ReqObj(root, "diet", "", E);
            if (diet != null)
            {
                CheckKeys(diet, DietKeys, "diet.", E);
                var foods = ReqStrArray(diet, "foods", "diet.", E);
                if (foods != null)
                {
                    var d = Diet.None;
                    foreach (var s in foods)
                    {
                        if (TryEnum(s, out Diet one) && one != Diet.None && IsSingleFlag((int)one)) d |= one;
                        else E("E3", $"diet.foods: unknown food '{s}' (pellet | shrimp | sardine)");
                    }
                    if (d != Diet.None) f.diet = d;
                    else if (foods.Length == 0) E("E3", "diet.foods is empty");
                }
                f.feedStyle = ReqEnum(diet, "style", "diet.", E, FeedStyle.Grab);
            }
            // the generated bed (Docs/terrain_depth_spec.md 7.1): absent or "" = none (h 1 everywhere)
            if (root.Has("habitat"))
            {
                string hab = OptStr(root, "habitat", "", E);
                if (!string.IsNullOrEmpty(hab)) f.habitat = HabitatDef.Parse(id, hab, m => E("E9", "habitat: " + m));
            }
            f.pocketHold = OptNum(root, "pocketHold", "", E, 0f);
            // a legend: its encounter row from LegendEncounters, its key lures = its baits (in their order)
            if (root.Has("encounter"))
            {
                string key = OptStr(root, "encounter", "", E);
                if (key != id) E("E10", $"encounter '{key}' must equal the id '{id}'");
                var e = key != null ? LegendEncounters.Make(key) : null;
                if (e == null)
                    E("E10", $"no legend encounter '{key}' in LegendEncounters (left out: it would swim as an ordinary fish)");
                else
                {
                    foreach (var kv in f.baitPrefs) e.keyLures[kv.Key] = kv.Value;
                    f.encounter = e;
                }
            }
            if (hard) return null;
            r.speciesJson[id] = root;
            return f;
        }

        static bool IsSingleFlag(int v) => v != 0 && (v & (v - 1)) == 0;

        /// <summary>
        /// The bait string ("worm:1,minnow:0.8,@twitch:0.8": bait ids without the bait_ prefix, lure actions after an @) into
        /// baitPrefs / actionPrefs, in its order. (GameDatabase.F's loop, moved here unchanged.)
        /// </summary>
        public static void ParsePrefs(FishSpecies f, string baits, Action<string> warn)
        {
            // "worm:1,minnow:0.8,@twitch:0.8": bait ids without the bait_ prefix, lure actions after an @
            foreach (var part in baits.Split(','))
            {
                var kv = part.Split(':');
                string key = kv[0].Trim();
                float v = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                if (key.StartsWith("@"))
                {
                    if (System.Enum.TryParse(key.Substring(1), true, out LureAction a) && a != LureAction.None) f.actionPrefs[a] = v;
                    else warn?.Invoke($"{f.id}: unknown lure action '{key}'");
                }
                else f.baitPrefs["bait_" + key] = v;
            }
        }

        // ------------------------------------------------------------------------------------ stages
        static void MapStages(Source src, Result r, Dictionary<string, FishSpecies> species)
        {
            const string file = "stages.json";
            void E(string rule, string msg) => Err(r, file, rule, msg);
            if (src == null)
            {
                E("E1", "Resources/Data/stages.json is missing");
                return;
            }
            if (src.bom) E("E1", "starts with a BOM (save as UTF-8 without a BOM)");
            var root = DataJson.Parse(src.text, out string perr);
            if (root == null)
            {
                E("E1", "not valid JSON: " + perr);
                return;
            }
            if (root.kind != JNode.Kind.Object)
            {
                E("E1", "must be one JSON object {\"stages\": [...]}");
                return;
            }
            r.stagesJson = root;
            CheckKeys(root, RosterKeys, "", E);
            var list = root["stages"];
            if (list == null || list.kind != JNode.Kind.Array)
            {
                E("E3", "\"stages\" must be an array");
                return;
            }
            var seen = new HashSet<string>();
            for (int i = 0; i < list.items.Count; i++)
            {
                var o = list.items[i];
                string at = $"stages[{i}].";
                if (o.kind != JNode.Kind.Object)
                {
                    E("E3", at + " must be an object");
                    continue;
                }
                // (E3: a missing or bad value leaves the stage out; a bad fish entry is reported and skipped)
                bool hard = false;
                void SE(string rule, string msg)
                {
                    E(rule, msg);
                    if (rule == "E3") hard = true;
                }
                CheckKeys(o, StageKeys, at, SE);
                var s = new StageDef
                {
                    id = ReqStr(o, "id", at, SE),
                    name = ReqStr(o, "name", at, SE),
                    subtitle = ReqStr(o, "subtitle", at, SE),
                    difficulty = ReqInt(o, "difficulty", at, SE),
                    reqLevel = ReqInt(o, "reqLevel", at, SE),
                    unlockCost = ReqInt(o, "unlockCost", at, SE),
                    powerMult = ReqNum(o, "powerMult", at, SE),
                    biteMult = ReqNum(o, "biteMult", at, SE),
                    population = ReqInt(o, "population", at, SE),
                };
                if (s.id != null) at = $"stage {s.id}: ";
                var fl = o["fish"];
                if (fl == null || fl.kind != JNode.Kind.Array) SE("E3", at + "\"fish\" (the species exposed here) must be an array");
                else
                {
                    for (int j = 0; j < fl.items.Count; j++)
                    {
                        var e = fl.items[j];
                        string fat = at + $"fish[{j}].";
                        if (e.kind != JNode.Kind.Object)
                        {
                            E("E8", fat + " must be {\"id\": ..., \"weight\": ...}");
                            continue;
                        }
                        CheckKeys(e, StageFishKeys, fat, SE);
                        // (step 1: every weight is given; NaN stays the "derive it" mark for later)
                        string fid = ReqStr(e, "id", fat, SE, "E8");
                        float w = ReqNum(e, "weight", fat, SE, "E8");
                        if (fid == null) continue;
                        if (s.id != null)
                        {
                            if (!r.stagesOf.TryGetValue(fid, out var on)) r.stagesOf[fid] = on = new List<string>();
                            on.Add(s.id);
                        }
                        if (!species.ContainsKey(fid))
                        {
                            // (a species whose own file failed to load has its errors there already)
                            if (!r.fileIds.Contains(fid)) E("E8", $"{fat}id: unknown species '{fid}' (no Fish/{fid}.json)");
                            continue;
                        }
                        if (float.IsNaN(w)) continue;
                        s.spawns.Add(new KeyValuePair<string, float>(fid, w));
                    }
                }
                if (s.id == null) continue;
                if (!seen.Add(s.id))
                {
                    E("E1", $"duplicate stage id '{s.id}'");
                    continue;
                }
                // (a stage with a missing value is left out: its own errors say why)
                if (hard) continue;
                r.stages.Add(s);
            }
        }

        // ------------------------------------------------------------------------------------ field helpers
        /// <summary>Unknown and repeated keys of one object (E2).</summary>
        public static void CheckKeys(JNode o, string[] allowed, string at, Action<string, string> E)
        {
            var seen = new HashSet<string>();
            foreach (var kv in o.members)
            {
                if (!seen.Add(kv.Key)) E("E2", $"key '{at}{kv.Key}' appears twice");
                if (Array.IndexOf(allowed, kv.Key) < 0) E("E2", $"unknown key '{at}{kv.Key}' (allowed: {string.Join(", ", allowed)})");
            }
        }

        static string ReqStr(JNode o, string key, string at, Action<string, string> E, string rule = "E3")
        {
            var n = o[key];
            if (n == null) E(rule, $"{at}{key} is required");
            else if (n.kind != JNode.Kind.String) E(rule, $"{at}{key} must be a string");
            else if (string.IsNullOrWhiteSpace(n.text)) E(rule, $"{at}{key} is blank");
            else return n.text;
            return null;
        }

        static string OptStr(JNode o, string key, string at, Action<string, string> E)
        {
            var n = o[key];
            if (n == null || n.kind == JNode.Kind.Null) return null;
            if (n.kind != JNode.Kind.String)
            {
                E("E3", $"{at}{key} must be a string");
                return null;
            }
            return n.text;
        }

        static JNode ReqObj(JNode o, string key, string at, Action<string, string> E)
        {
            var n = o[key];
            if (n == null) E("E3", $"{at}{key} is required");
            else if (n.kind != JNode.Kind.Object) E("E3", $"{at}{key} must be an object");
            else return n;
            return null;
        }

        static string[] ReqStrArray(JNode o, string key, string at, Action<string, string> E)
        {
            var n = o[key];
            if (n == null)
            {
                E("E3", $"{at}{key} is required");
                return null;
            }
            if (n.kind != JNode.Kind.Array)
            {
                E("E3", $"{at}{key} must be an array of strings");
                return null;
            }
            var a = new string[n.items.Count];
            for (int i = 0; i < a.Length; i++)
            {
                if (n.items[i].kind != JNode.Kind.String || string.IsNullOrWhiteSpace(n.items[i].text))
                {
                    E("E3", $"{at}{key}[{i}] must be a non-blank string");
                    return null;
                }
                a[i] = n.items[i].text;
            }
            return a;
        }

        /// <summary>A required number (NaN when it is missing: an error).</summary>
        static float ReqNum(JNode o, string key, string at, Action<string, string> E, string rule = "E3")
        {
            var n = o[key];
            if (n == null) E(rule, $"{at}{key} is required");
            else if (n.kind != JNode.Kind.Number) E(rule, $"{at}{key} must be a number");
            else if (float.TryParse(n.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && !float.IsInfinity(v)) return v;
            else E(rule, $"{at}{key}: '{n.text}' is out of range");
            return float.NaN;
        }

        static float OptNum(JNode o, string key, string at, Action<string, string> E, float def)
        {
            if (!o.Has(key)) return def;
            float v = ReqNum(o, key, at, E);
            return float.IsNaN(v) ? def : v;
        }

        /// <summary>A required whole number (int.MinValue when it is missing: an error).</summary>
        static int ReqInt(JNode o, string key, string at, Action<string, string> E)
        {
            var n = o[key];
            if (n == null) E("E3", $"{at}{key} is required");
            else if (n.kind != JNode.Kind.Number || !Regex.IsMatch(n.text, "^-?[0-9]+$")) E("E3", $"{at}{key} must be a whole number");
            else if (int.TryParse(n.text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v)) return v;
            else E("E3", $"{at}{key}: '{n.text}' is out of range");
            return int.MinValue;
        }

        static T ReqEnum<T>(JNode o, string key, string at, Action<string, string> E, T def) where T : struct
        {
            string s = ReqStr(o, key, at, E);
            if (s == null) return def;
            if (TryEnum(s, out T v)) return v;
            E("E3", $"{at}{key}: unknown value '{s}' ({string.Join(" | ", Names<T>())})");
            return def;
        }

        /// <summary>An enum value by its name (any case); digits are not names.</summary>
        public static bool TryEnum<T>(string s, out T v) where T : struct
        {
            v = default;
            if (string.IsNullOrWhiteSpace(s) || !char.IsLetter(s.Trim()[0])) return false;
            return Enum.TryParse(s.Trim(), true, out v) && Enum.IsDefined(typeof(T), v);
        }

        static IEnumerable<string> Names<T>()
        {
            foreach (var n in Enum.GetNames(typeof(T)))
                yield return char.ToLowerInvariant(n[0]) + n.Substring(1);
        }
    }

    /// <summary>A parsed JSON value (<see cref="DataJson"/>): objects keep their keys in order, numbers their text.</summary>
    public sealed class JNode
    {
        public enum Kind { Object, Array, String, Number, Bool, Null }

        public Kind kind;
        /// <summary>A string's value or a number's token as written.</summary>
        public string text;
        public bool flag;
        public List<KeyValuePair<string, JNode>> members;
        public List<JNode> items;
        public int line;

        /// <summary>An object's member (the first with that key), or null.</summary>
        public JNode this[string key]
        {
            get
            {
                if (members == null) return null;
                foreach (var kv in members) if (kv.Key == key) return kv.Value;
                return null;
            }
        }

        public bool Has(string key) => this[key] != null;

        public static JNode Str(string s) => new JNode { kind = Kind.String, text = s };
        public static JNode Num(string token) => new JNode { kind = Kind.Number, text = token };

        /// <summary>Sets (or adds at the end) an object's member; null removes it.</summary>
        public void Set(string key, JNode v)
        {
            for (int i = 0; i < members.Count; i++)
                if (members[i].Key == key)
                {
                    if (v == null) members.RemoveAt(i);
                    else members[i] = new KeyValuePair<string, JNode>(key, v);
                    return;
                }
            if (v != null) members.Add(new KeyValuePair<string, JNode>(key, v));
        }
    }

    /// <summary>A small strict JSON reader and writer for the data files (no comments, no trailing commas).</summary>
    public static class DataJson
    {
        public static JNode Parse(string text, out string error)
        {
            error = null;
            if (text == null)
            {
                error = "no text";
                return null;
            }
            var p = new P { s = text, i = text.Length > 0 && text[0] == '﻿' ? 1 : 0, line = 1 };
            try
            {
                p.Ws();
                var v = p.Value();
                p.Ws();
                if (p.i < p.s.Length) throw p.Fail("text after the end of the JSON value");
                return v;
            }
            catch (FormatException e)
            {
                error = e.Message;
                return null;
            }
        }

        sealed class P
        {
            public string s;
            public int i, line;

            public FormatException Fail(string msg) => new FormatException($"line {line}: {msg}");

            public void Ws()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '\n') line++;
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                    else break;
                }
            }

            public JNode Value()
            {
                if (i >= s.Length) throw Fail("unexpected end");
                char c = s[i];
                int at = line;
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return new JNode { kind = JNode.Kind.String, text = Str(), line = at };
                if (c == '-' || (c >= '0' && c <= '9')) return new JNode { kind = JNode.Kind.Number, text = Num(), line = at };
                if (Word("true")) return new JNode { kind = JNode.Kind.Bool, flag = true, line = at };
                if (Word("false")) return new JNode { kind = JNode.Kind.Bool, flag = false, line = at };
                if (Word("null")) return new JNode { kind = JNode.Kind.Null, line = at };
                throw Fail($"unexpected '{c}'");
            }

            bool Word(string w)
            {
                if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
                i += w.Length;
                return true;
            }

            JNode Obj()
            {
                var n = new JNode { kind = JNode.Kind.Object, members = new List<KeyValuePair<string, JNode>>(), line = line };
                i++;
                Ws();
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return n;
                }
                while (true)
                {
                    Ws();
                    if (i >= s.Length || s[i] != '"') throw Fail("expected a key in quotes");
                    string k = Str();
                    Ws();
                    if (i >= s.Length || s[i] != ':') throw Fail($"expected ':' after \"{k}\"");
                    i++;
                    Ws();
                    n.members.Add(new KeyValuePair<string, JNode>(k, Value()));
                    Ws();
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == '}')
                    {
                        i++;
                        return n;
                    }
                    throw Fail("expected ',' or '}'");
                }
            }

            JNode Arr()
            {
                var n = new JNode { kind = JNode.Kind.Array, items = new List<JNode>(), line = line };
                i++;
                Ws();
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return n;
                }
                while (true)
                {
                    Ws();
                    n.items.Add(Value());
                    Ws();
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == ']')
                    {
                        i++;
                        return n;
                    }
                    throw Fail("expected ',' or ']'");
                }
            }

            string Str()
            {
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Fail("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\n' || c == '\r') throw Fail("line break inside a string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length) throw Fail("unterminated string");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u))
                                throw Fail("bad \\u escape");
                            sb.Append((char)u);
                            i += 4;
                            break;
                        default: throw Fail($"bad escape '\\{e}'");
                    }
                }
            }

            static bool D(char c) => c >= '0' && c <= '9';

            string Num()
            {
                int a = i;
                if (s[i] == '-') i++;
                if (i >= s.Length || !D(s[i])) throw Fail("bad number");
                if (s[i] == '0') i++;
                else while (i < s.Length && D(s[i])) i++;
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    if (i >= s.Length || !D(s[i])) throw Fail("bad number");
                    while (i < s.Length && D(s[i])) i++;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    if (i >= s.Length || !D(s[i])) throw Fail("bad number");
                    while (i < s.Length && D(s[i])) i++;
                }
                return s.Substring(a, i - a);
            }
        }

        /// <summary>The value as JSON text (2-space indent; for the tests' broken fixtures, never to re-save a data file).</summary>
        public static string Write(JNode n)
        {
            var sb = new StringBuilder();
            W(n, sb, 0);
            return sb.ToString();
        }

        static void W(JNode n, StringBuilder sb, int ind)
        {
            switch (n.kind)
            {
                case JNode.Kind.Object:
                    if (n.members.Count == 0)
                    {
                        sb.Append("{}");
                        return;
                    }
                    sb.Append("{\n");
                    for (int k = 0; k < n.members.Count; k++)
                    {
                        sb.Append(' ', ind + 2).Append(Quote(n.members[k].Key)).Append(": ");
                        W(n.members[k].Value, sb, ind + 2);
                        sb.Append(k + 1 < n.members.Count ? ",\n" : "\n");
                    }
                    sb.Append(' ', ind).Append('}');
                    return;
                case JNode.Kind.Array:
                    sb.Append('[');
                    for (int k = 0; k < n.items.Count; k++)
                    {
                        if (k > 0) sb.Append(", ");
                        W(n.items[k], sb, ind);
                    }
                    sb.Append(']');
                    return;
                case JNode.Kind.String: sb.Append(Quote(n.text)); return;
                case JNode.Kind.Number: sb.Append(n.text); return;
                case JNode.Kind.Bool: sb.Append(n.flag ? "true" : "false"); return;
                default: sb.Append("null"); return;
            }
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
