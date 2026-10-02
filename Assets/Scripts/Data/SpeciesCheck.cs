using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The species data validator (Docs/data_reference.md 2.7): everything a new or changed species file, its art and its
    /// place on the stages must have. It reads the data through <see cref="SpeciesData.Build"/> (whose findings it keeps)
    /// and adds the cross-checks: value ranges, bait and lure ids, cover types the stage offers, the four sprites, which
    /// stages expose it, its habitat on a generated bed, a legend's encounter, model and palette, and its Blender model.
    /// Errors fail the editor menu (FishingKing/Validate Species Data), its batch method, the Windows build and the player's
    /// <c>-fkauto species</c>; warnings are only logged. The <see cref="Context"/> says where the files come from: the
    /// project's files (the editor: <see cref="FromFiles"/>) or the player's Resources (<see cref="FromResources"/>).
    /// </summary>
    public static class SpeciesCheck
    {
        public sealed class Context
        {
            /// <summary>Where the data came from (for the log).</summary>
            public string where = "";
            public SpeciesData.Source stages;
            public List<SpeciesData.Source> species = new List<SpeciesData.Source>();
            /// <summary>A fish sprite exists (by name: "carp_0", "carp_t1").</summary>
            public Func<string, bool> spriteExists;
            /// <summary>Models/&lt;name&gt; (the FBX) and Models/&lt;name&gt;_palette exist.</summary>
            public Func<string, bool> modelExists, paletteExists;
            /// <summary>A stage's obstacles_&lt;id&gt;.json (null: it has none).</summary>
            public Func<string, ObstacleSet> obstacles;
            /// <summary>A stage's stage_&lt;id&gt;.json (null: missing).</summary>
            public Func<string, StageLayout> layout;
            /// <summary>The music cue ids (music.json; null: not checked).</summary>
            public HashSet<string> musicCues;
            /// <summary>Tools/Blender/fk_fish.py and variants/hybrid/hyb_fish.py (null: the Blender checks are skipped).</summary>
            public string fkFishPy, hybFishPy;
            /// <summary>Tools/Blender/variants/hybrid/legends/&lt;id&gt;.py's text (null: missing).</summary>
            public Func<string, string> legendScript;
            /// <summary>The running game's GameDatabase.LoadErrors (E12; null: not checked).</summary>
            public IReadOnlyList<string> loadErrors;

            /// <summary>A copy with its own species list (the tests break one and keep the rest).</summary>
            public Context Clone()
            {
                var c = (Context)MemberwiseClone();
                c.species = new List<SpeciesData.Source>(species);
                return c;
            }
        }

        // ------------------------------------------------------------------------------------ contexts
        /// <summary>The player's data (Resources); with <paramref name="repoRoot"/> (-fkrepo) also the Blender scripts.</summary>
        public static Context FromResources(string repoRoot = null)
        {
            var c = new Context { where = "Resources" };
            c.stages = SpeciesData.FromResources(out c.species);
            c.spriteExists = n => Resources.Load<Sprite>("Sprites/Fish/" + n) != null;
            c.modelExists = n => Resources.Load<GameObject>("Models/" + n) != null;
            c.paletteExists = n => Resources.Load<TextAsset>("Models/" + n + "_palette") != null;
            c.obstacles = Obstacles.ReadSet;
            c.layout = id =>
            {
                var t = Resources.Load<TextAsset>("Data/stage_" + id);
                return t != null ? JsonUtility.FromJson<StageLayout>(t.text) : null;
            };
            c.musicCues = Cues(Resources.Load<TextAsset>("Data/music")?.text);
            c.loadErrors = GameDatabase.LoadErrors;
            if (!string.IsNullOrEmpty(repoRoot)) Blender(c, repoRoot);
            return c;
        }

        /// <summary>The project's files (the editor reads them directly: fresh after an edit, imported or not).</summary>
        public static Context FromFiles(string projectRoot)
        {
            string res = Path.Combine(projectRoot, "Assets", "Resources");
            string data = Path.Combine(res, "Data");
            var c = new Context { where = projectRoot };
            string st = Path.Combine(data, "stages.json");
            c.stages = File.Exists(st) ? Read(st) : null;
            // (every file in the species folder: Resources would load any text asset there as a species file)
            string dir = Path.Combine(data, "Fish");
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta")).ToList();
                files.Sort(StringComparer.Ordinal);
                foreach (var p in files) c.species.Add(Read(p));
            }
            c.spriteExists = n => File.Exists(Path.Combine(res, "Sprites", "Fish", n + ".png"));
            c.modelExists = n => File.Exists(Path.Combine(res, "Models", n + ".fbx"));
            c.paletteExists = n => File.Exists(Path.Combine(res, "Models", n + "_palette.json"));
            c.obstacles = id =>
            {
                string p = Path.Combine(data, "obstacles_" + id + ".json");
                return File.Exists(p) ? JsonUtility.FromJson<ObstacleSet>(File.ReadAllText(p)) : null;
            };
            c.layout = id =>
            {
                string p = Path.Combine(data, "stage_" + id + ".json");
                return File.Exists(p) ? JsonUtility.FromJson<StageLayout>(File.ReadAllText(p)) : null;
            };
            string mu = Path.Combine(data, "music.json");
            c.musicCues = File.Exists(mu) ? Cues(File.ReadAllText(mu)) : null;
            Blender(c, projectRoot);
            return c;
        }

        static SpeciesData.Source Read(string path)
        {
            var b = File.ReadAllBytes(path);
            bool bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
            return new SpeciesData.Source
            {
                name = Path.GetFileNameWithoutExtension(path), bom = bom,
                text = new UTF8Encoding(false).GetString(b, bom ? 3 : 0, b.Length - (bom ? 3 : 0)),
            };
        }

        static void Blender(Context c, string root)
        {
            string bl = Path.Combine(root, "Tools", "Blender");
            string ReadOrNull(string p) => File.Exists(p) ? File.ReadAllText(p) : null;
            c.fkFishPy = ReadOrNull(Path.Combine(bl, "fk_fish.py"));
            c.hybFishPy = ReadOrNull(Path.Combine(bl, "variants", "hybrid", "hyb_fish.py"));
            // (one of them there, the other missing: check, so the missing one is an error; neither: not a repo)
            if (c.fkFishPy != null || c.hybFishPy != null)
            {
                c.fkFishPy ??= "";
                c.hybFishPy ??= "";
            }
            c.legendScript = id => ReadOrNull(Path.Combine(bl, "variants", "hybrid", "legends", id + ".py"));
        }

        [Serializable]
        class CueIds
        {
            public CueId[] cues;
        }

        [Serializable]
        class CueId
        {
            public string id;
        }

        static HashSet<string> Cues(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = JsonUtility.FromJson<CueIds>(text);
            var s = new HashSet<string>();
            if (m?.cues != null) foreach (var q in m.cues) if (q?.id != null) s.Add(q.id);
            return s;
        }

        // ------------------------------------------------------------------------------------ the checks
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static readonly Regex BaitWeight = new Regex(@"^[0-9]+(\.[0-9]+)?$");

        public static List<DataFinding> Run(Context c) => Run(c, out _);

        /// <summary>Every finding, errors first in rule order, then the warnings; <paramref name="r"/> is the data as loaded.</summary>
        public static List<DataFinding> Run(Context c, out SpeciesData.Result r)
        {
            var list = new List<DataFinding>();
            r = SpeciesData.Build(c.stages, c.species);
            try
            {
                list.AddRange(r.findings);
                Checks(c, r, list);
            }
            catch (Exception e)
            {
                list.Add(new DataFinding { error = true, file = "", rule = "E0", message = "the validator failed: " + e });
            }
            return list.OrderBy(f => f.error ? 0 : 1).ThenBy(f => RuleNo(f.rule)).ToList();
        }

        static int RuleNo(string rule) => rule != null && rule.Length > 1 && int.TryParse(rule.Substring(1), out int n) ? n : 99;

        public static int Errors(List<DataFinding> f) => f.Count(x => x.error);

        public static string Summary(List<DataFinding> f, SpeciesData.Result r) =>
            $"{Errors(f)} errors, {f.Count(x => !x.error)} warnings ({r.fish.Count} species, {r.stages.Count} stages)";

        static void Checks(Context c, SpeciesData.Result r, List<DataFinding> list)
        {
            void E(string rule, string file, string msg) => list.Add(new DataFinding { error = true, rule = rule, file = file, message = msg });
            void W(string file, string msg) => list.Add(new DataFinding { error = false, rule = "W", file = file, message = msg });
            string F(string id) => "Fish/" + id + ".json";
            const string Roster = "stages.json";

            var baitIds = new HashSet<string>(GameDatabase.Baits.Select(b => b.id));
            var setIds = new HashSet<string>(GameDatabase.SetIds);
            var stageIds = new HashSet<string>(r.stages.Select(s => s.id));

            // ---- E8: the stages and who they expose
            if (!stageIds.Contains("lake")) E("E8", Roster, "no \"lake\" stage (a new save starts there)");
            foreach (var s in r.stages)
            {
                string at = $"stage {s.id}: ";
                if (s.difficulty < 1 || s.difficulty > 5) E("E8", Roster, at + $"difficulty {s.difficulty} is not 1..5");
                if (s.reqLevel < 1) E("E8", Roster, at + $"reqLevel {s.reqLevel} < 1");
                if (s.unlockCost < 0) E("E8", Roster, at + $"unlockCost {s.unlockCost} < 0");
                if (!(s.powerMult > 0f)) E("E8", Roster, at + $"powerMult {s.powerMult.ToString(CI)} must be > 0");
                if (!(s.biteMult > 0f)) E("E8", Roster, at + $"biteMult {s.biteMult.ToString(CI)} must be > 0");
                if (s.population < 1) E("E8", Roster, at + $"population {s.population} < 1");
                if (c.layout != null && c.layout(s.id) == null) E("E8", Roster, at + $"Data/stage_{s.id}.json (its layout, from Blender) is missing");
                var twice = new HashSet<string>();
                bool ordinary = false;
                foreach (var kv in s.spawns)
                {
                    if (!twice.Add(kv.Key)) E("E8", Roster, at + $"'{kv.Key}' is listed twice");
                    if (!(kv.Value > 0f)) E("E8", Roster, at + $"'{kv.Key}' weight {kv.Value.ToString(CI)} must be > 0");
                    var sp = r.fish.FirstOrDefault(f => f.id == kv.Key);
                    if (sp != null && sp.encounter == null) ordinary = true;
                }
                if (!ordinary) E("E8", Roster, at + "exposes no ordinary (non-legend) species: nothing could be stocked");
            }
            foreach (var sp in r.fish)
            {
                var on = r.stagesOf.TryGetValue(sp.id, out var l) ? l.Where(stageIds.Contains).Distinct().ToList() : new List<string>();
                if (on.Count == 0) E("E8", F(sp.id), "is exposed on no stage (add it to a stage's \"fish\" in stages.json)");
                else if (on.Count > 1) E("E8", F(sp.id), $"is on more than one stage ({string.Join(", ", on)}): one stage per species for now");
            }

            // ---- obstacles: the cover types each stage offers (a kind "cover" zone's coverFor; "rim" from a kind "rim")
            var offered = new Dictionary<string, HashSet<string>>();
            foreach (var s in r.stages)
            {
                var set = c.obstacles?.Invoke(s.id);
                if (set?.obstacles == null) continue;
                var tags = new HashSet<string>();
                foreach (var o in set.obstacles)
                {
                    if (o == null) continue;
                    if (o.kind == "cover" && o.coverFor != null) foreach (var t in o.coverFor) tags.Add(t);
                    if (o.kind == "rim") tags.Add("rim");
                }
                offered[s.id] = tags;
            }

            foreach (var sp in r.fish)
            {
                string file = F(sp.id);
                var j = r.speciesJson[sp.id];
                var stages = r.stagesOf.TryGetValue(sp.id, out var sl) ? sl.Where(stageIds.Contains).Distinct().ToList() : new List<string>();
                bool legend = sp.encounter != null;

                // ---- E4: ranges
                if (!(sp.minCm > 0f && sp.minCm < sp.maxCm)) E("E4", file, $"needs 0 < minCm < maxCm ({N(sp.minCm)}, {N(sp.maxCm)})");
                foreach (var (k, v) in new[] { ("weightK", sp.weightK), ("power", sp.power), ("stamina", sp.stamina), ("speed", sp.speed) })
                    if (!(v > 0f)) E("E4", file, $"{k} {N(v)} must be > 0");
                if (sp.basePrice <= 0) E("E4", file, $"basePrice {sp.basePrice} must be > 0");
                if (!(sp.aggression >= 0f && sp.aggression <= 1f)) E("E4", file, $"aggression {N(sp.aggression)} is not 0..1");
                if (!(sp.jump >= 0f && sp.jump <= 1f)) E("E4", file, $"jump {N(sp.jump)} is not 0..1");
                if (!(sp.depthMin >= 0f && sp.depthMin <= sp.depthMax)) E("E4", file, $"needs 0 <= depthMin <= depthMax ({N(sp.depthMin)}, {N(sp.depthMax)})");
                if (sp.activity != null)
                {
                    if (sp.activity.Any(a => !(a >= 0f))) E("E4", file, "activity values must be >= 0");
                    if (!sp.activity.Any(a => a > 0f)) E("E4", file, "activity is 0 in every period: it would never come");
                }
                if (sp.coverFor != null)
                {
                    if (!(sp.coverSeek > 0f && sp.coverSeek <= 1f)) E("E4", file, $"cover.seek {N(sp.coverSeek)} is not in (0, 1]");
                    if (!(sp.coverReach >= 0f)) E("E4", file, $"cover.reach {N(sp.coverReach)} must be >= 0");
                    if (!(sp.coverDig > 0f)) E("E4", file, $"cover.dig {N(sp.coverDig)} must be > 0");
                    if (sp.coverReach == 0f && sp.coverFor.Any(t => t != "rim")) W(file, "cover.reach 0 (the ice hole's rim) with a type other than \"rim\"");
                }
                if (!(sp.pocketHold >= 0f && sp.pocketHold <= 1f)) E("E4", file, $"pocketHold {N(sp.pocketHold)} is not 0..1");
                int foods = 0;
                foreach (Diet d in new[] { Diet.Pellet, Diet.Shrimp, Diet.Sardine }) if ((sp.diet & d) != 0) foods++;
                if (foods < 1 || foods > 2) E("E4", file, $"diet.foods: {foods} foods (1 or 2)");
                if (sp.feedStyle == FeedStyle.Surge && (sp.diet & Diet.Pellet) != 0) E("E4", file, "a \"surge\" feeder takes no pellets (pellet + surge)");

                // ---- E5: the bait string, token by token
                string baits = j["baits"]?.text ?? "";
                var keys = new HashSet<string>();
                bool anyBait = false, anyAction = false;
                foreach (var tok in baits.Split(','))
                {
                    var kv = tok.Split(':');
                    if (kv.Length != 2)
                    {
                        E("E5", file, $"baits: '{tok}' is not key:weight");
                        continue;
                    }
                    string key = kv[0].Trim(), val = kv[1].Trim();
                    bool num = BaitWeight.IsMatch(val) && float.TryParse(val, NumberStyles.Float, CI, out float w);
                    float wv = num ? float.Parse(val, CI) : 0f;
                    if (!num) E("E5", file, $"baits: '{tok}': the weight is not a plain number");
                    if (!keys.Add(key)) E("E5", file, $"baits: '{key}' appears twice");
                    if (key.StartsWith("@"))
                    {
                        anyAction = true;
                        if (!SpeciesData.TryEnum(key.Substring(1), out LureAction a) || a == LureAction.None)
                            E("E5", file, $"baits: unknown lure action '{key}' (@steady | @twitch | @topwater | @bottom | @vertical)");
                    }
                    else
                    {
                        if (!baitIds.Contains("bait_" + key)) E("E5", file, $"baits: no bait or lure 'bait_{key}' in the shop (GameDatabase.BuildItems)");
                        else if (wv > 0f) anyBait = true;
                    }
                }
                if (!anyBait) E("E5", file, "baits: no bait or lure with a weight > 0");

                // ---- E6: cover types the stage's covers offer
                if (sp.coverFor != null)
                {
                    foreach (var s in stages)
                        if (!offered.ContainsKey(s)) E("E6", file, $"seeks cover, but stage {s} has no obstacles_{s}.json");
                    foreach (var t in sp.coverFor)
                        if (stages.Count > 0 && !stages.Any(s => offered.TryGetValue(s, out var o) && o.Contains(t)))
                            E("E6", file, $"cover type '{t}': no cover on {string.Join(", ", stages)} offers it ({string.Join(", ", stages.Select(s => s + ": " + (offered.TryGetValue(s, out var o) ? string.Join(" ", o.OrderBy(x => x)) : "none")))})");
                }

                // ---- E9: the habitat on a generated bed
                bool terrain = stages.Any(s => TerrainRecipes.For(s) != null);
                string hab = j["habitat"]?.text;
                if (terrain && !legend)
                {
                    if (string.IsNullOrEmpty(hab)) E("E9", file, $"habitat is required on a generated bed ({string.Join(", ", stages.Where(s => TerrainRecipes.For(s) != null))})");
                    else
                    {
                        // (Docs/lake_phase2_spec.md A3: the depth band relative to the lake, in percentiles; the shifts in points)
                        var parts = hab.Split(',').Select(p => p.Trim()).ToList();
                        var h = sp.habitat;
                        if (!parts.Any(p => p.StartsWith("depth:"))) E("E9", file, "habitat needs its depth (depth:pA-pB, percentiles of the lake's depths)");
                        if (!parts.Any(p => p.StartsWith("col:"))) E("E9", file, "habitat needs its column (col:mid | col:bottom)");
                        if (h != null && (h.depthM || h.shiftM))
                            E("E9", file, "habitat mixes units: on a generated bed the depth is relative (depth:pA-pB) and the shifts in points (@period:Np), not metres");
                        if (h != null && h.depthP && !(h.pa >= 0f && h.pa < h.pb && h.pb <= 100f && h.pb - h.pa >= HabitatModel.MinBand))
                            E("E9", file, $"habitat depth p{N(h.pa)}-p{N(h.pb)}: needs 0 <= A < B <= 100 and B - A >= {N(HabitatModel.MinBand)}");
                        if (h != null && h.shiftPct)
                            for (int p = 0; p < 4; p++)
                                if (!(Mathf.Abs(h.shiftP[p]) <= 50f)) E("E9", file, $"habitat @{SpeciesData.ActivityKeys[p]}:{N(h.shiftP[p])}p: a shift is at most 50 points");
                    }
                }
                else if (!string.IsNullOrEmpty(hab) && !terrain) W(file, "habitat is given but never read (none of its stages has a generated bed)");

                // ---- E10: legends
                if (legend)
                {
                    var e = sp.encounter;
                    var fresh = LegendEncounters.Make(sp.id);
                    if (fresh != null && fresh.keyLures.Count > 0) E("E10", file, "LegendEncounters' factory sets keyLures: the key lures come from the species' baits only");
                    foreach (var k in e.keyRules.Keys)
                        if (!sp.baitPrefs.ContainsKey(k)) E("E10", file, $"keyRules names '{k}', which is not among its baits (its key lures)");
                    if (anyAction) E("E10", file, "a legend's baits are its key lures: no @action entries");
                    if (c.modelExists != null && !c.modelExists(e.model)) E("E10", file, $"no model Models/{e.model}.fbx");
                    if (c.paletteExists != null && !c.paletteExists(e.model)) E("E10", file, $"no palette Models/{e.model}_palette.json");
                    if (!setIds.Contains(e.backdrop)) E("E10", file, $"backdrop '{e.backdrop}' has no encounter set (GameDatabase.BuildSets; it would quietly show the cave)");
                    if (c.musicCues != null && !c.musicCues.Contains("legend_" + sp.id)) W(file, $"no music cue legend_{sp.id} (the generic encounter music plays)");
                }

                // ---- warnings: an ice species that likes nothing usable in the hole
                if (stages.Any(s => c.layout?.Invoke(s)?.IsIce == true) &&
                    !GameDatabase.Baits.Any(b => LureInfo.IceOk(b) && sp.Appeal(b) > 0f))
                    W(file, "on the ice, but likes no bait usable in the hole (natural baits, 바닥 and 수직 lures)");
            }
            // ---- E10: a legend whose species file lost its "encounter" would be stocked as an ordinary fish (FishSpawner.Pick
            // skips only fish with an encounter) and its stage would lose its encounter (LegendWatch)
            foreach (var k in LegendEncounters.Keys)
            {
                var sp = r.fish.FirstOrDefault(f => f.id == k);
                if (sp != null)
                {
                    if (sp.encounter == null)
                        E("E10", F(k), $"LegendEncounters has its encounter, but the species file has no \"encounter\": \"{k}\" (it would swim as an ordinary fish)");
                }
                else if (!r.fileIds.Contains(k)) W("", $"LegendEncounters has a factory '{k}' that no species file names");
            }
            foreach (var sp in r.fish)
                if (sp.rarity == Rarity.Legendary && sp.encounter == null && !LegendEncounters.Keys.Contains(sp.id))
                    W(F(sp.id), "legendary without an \"encounter\": it swims about as an ordinary fish (allowed, but no legend so far does)");

            // ---- E7: the four sprites of every species file
            foreach (var id in r.fileIds.Where(i => SpeciesData.IdPattern.IsMatch(i)).OrderBy(i => i, StringComparer.Ordinal))
                if (c.spriteExists != null)
                    foreach (var s in new[] { "_0", "_1", "_t0", "_t1" })
                        if (!c.spriteExists(id + s)) E("E7", F(id), $"sprite Sprites/Fish/{id}{s}.png is missing (Tools/Blender/variants/hybrid/hyb_fish.py)");

            // ---- E11: the Blender model (fk_fish.py's fish("<id>", ...), rendered by hyb_fish.py; a legend's rig)
            if (c.fkFishPy != null)
            {
                if (!c.hybFishPy.Contains("FF.F.keys()")) E("E11", "", "Tools/Blender/variants/hybrid/hyb_fish.py no longer renders every fk_fish model (FF.F.keys())");
                foreach (var id in r.fileIds.Where(i => SpeciesData.IdPattern.IsMatch(i)).OrderBy(i => i, StringComparer.Ordinal))
                    if (!Regex.IsMatch(c.fkFishPy, "^fish\\(\"" + Regex.Escape(id) + "\"", RegexOptions.Multiline))
                        E("E11", F(id), $"Tools/Blender/fk_fish.py has no model fish(\"{id}\", ...)");
                foreach (Match m in Regex.Matches(c.fkFishPy, "^fish\\(\"(\\w+)\"", RegexOptions.Multiline))
                    if (!r.fileIds.Contains(m.Groups[1].Value)) W("", $"fk_fish.py models '{m.Groups[1].Value}', which has no species file");
                foreach (var sp in r.fish.Where(f => f.encounter != null))
                {
                    string py = c.legendScript?.Invoke(sp.id);
                    if (py == null)
                    {
                        E("E11", F(sp.id), $"a legend needs its rig Tools/Blender/variants/hybrid/legends/{sp.id}.py");
                        continue;
                    }
                    var cm = Regex.Match(py, @"^CM = \(([0-9.]+), ([0-9.]+)\)", RegexOptions.Multiline);
                    if (cm.Success && (float.Parse(cm.Groups[1].Value, CI) != sp.minCm || float.Parse(cm.Groups[2].Value, CI) != sp.maxCm))
                        W(F(sp.id), $"legends/{sp.id}.py has CM = ({cm.Groups[1].Value}, {cm.Groups[2].Value}), the species {N(sp.minCm)}..{N(sp.maxCm)} cm");
                }
            }

            // ---- E12: the running game loaded its data cleanly
            if (c.loadErrors != null)
                foreach (var le in c.loadErrors) E("E12", "", "GameDatabase.LoadErrors: " + le);
        }

        static string N(float v) => v.ToString("R", CI);
    }
}
