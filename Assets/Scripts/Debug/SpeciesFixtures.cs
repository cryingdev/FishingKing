using System;
using System.Collections.Generic;
using System.Linq;

namespace FishingKing
{
    /// <summary>
    /// The validator's negative tests (-fkauto species, and the editor's batch validator with -fkspeciesfixtures): each
    /// fixture breaks one thing in a copy of the real data (a species file, the roster, an art lookup or a Blender script)
    /// and expects an error of its rule naming it. The real data must pass first.
    /// </summary>
    public static class SpeciesFixtures
    {
        sealed class Fx
        {
            public string name, rule, file, contains;
            public int min = 1;
            public bool blender;
            public Func<SpeciesCheck.Context> make;
        }

        /// <summary>Runs every fixture on copies of <paramref name="real"/>; returns how many failed.</summary>
        public static int Run(SpeciesCheck.Context real, Action<string> log)
        {
            int fails = 0, n = 0;
            foreach (var fx in All(real))
            {
                n++;
                if (fx.blender && real.fkFishPy == null)
                {
                    log($"[SPECIES] CHECK SKIP fixture {fx.name}: no Blender scripts (-fkrepo)");
                    continue;
                }
                List<DataFinding> got;
                try
                {
                    got = SpeciesCheck.Run(fx.make());
                }
                catch (Exception e)
                {
                    fails++;
                    log($"[SPECIES] CHECK FAIL fixture {fx.name}: the validator threw {e.GetType().Name}: {e.Message}");
                    continue;
                }
                var hits = got.Where(f => f.error && f.rule == fx.rule && (fx.file == null || f.file == fx.file) &&
                                          (fx.contains == null || f.message.Contains(fx.contains))).ToList();
                bool ok = hits.Count >= fx.min;
                if (!ok) fails++;
                string what = ok ? $"{hits.Count} x {hits[0]}"
                    : "got " + (got.Any(f => f.error) ? string.Join(" | ", got.Where(f => f.error).Take(4)) : "no errors");
                log($"[SPECIES] CHECK {(ok ? "PASS" : "FAIL")} fixture {fx.name}: expects {fx.rule}{(fx.file != null ? " in " + fx.file : "")}" +
                    $"{(fx.contains != null ? " '" + fx.contains + "'" : "")}{(fx.min > 1 ? " x" + fx.min : "")}: {what}");
            }
            log($"[SPECIES] fixtures: {n - fails} of {n} caught");
            return fails;
        }

        // ------------------------------------------------------------------------------------ edits on copies
        static SpeciesCheck.Context Species(SpeciesCheck.Context real, string id, Action<JNode> edit)
        {
            var c = real.Clone();
            int i = c.species.FindIndex(s => s.name == id);
            var src = c.species[i];
            var j = DataJson.Parse(src.text, out _);
            edit(j);
            c.species[i] = new SpeciesData.Source { name = src.name, bom = src.bom, text = DataJson.Write(j) };
            return c;
        }

        static SpeciesCheck.Context Roster(SpeciesCheck.Context real, Action<JNode> edit)
        {
            var c = real.Clone();
            var j = DataJson.Parse(c.stages.text, out _);
            edit(j);
            c.stages = new SpeciesData.Source { name = c.stages.name, bom = c.stages.bom, text = DataJson.Write(j) };
            return c;
        }

        static JNode Stage(JNode roster, string id) => roster["stages"].items.First(s => s["id"].text == id);

        static JNode FishList(JNode roster, string id) => Stage(roster, id)["fish"];

        static JNode Entry(string id, string weight)
        {
            var e = new JNode { kind = JNode.Kind.Object, members = new List<KeyValuePair<string, JNode>>() };
            e.Set("id", JNode.Str(id));
            e.Set("weight", JNode.Num(weight));
            return e;
        }

        static JNode Strs(params string[] s) => new JNode { kind = JNode.Kind.Array, items = s.Select(JNode.Str).ToList() };

        static string F(string id) => "Fish/" + id + ".json";

        static IEnumerable<Fx> All(SpeciesCheck.Context real)
        {
            // ---- E1 files
            yield return new Fx { name = "file name differs from the id", rule = "E1", contains = "differs from the file name", make = () =>
            {
                var c = real.Clone();
                int i = c.species.FindIndex(s => s.name == "flounder");
                c.species[i] = new SpeciesData.Source { name = "flounder2", text = c.species[i].text };
                return c;
            } };
            yield return new Fx { name = "a BOM", rule = "E1", file = F("mackerel"), contains = "BOM", make = () =>
            {
                var c = real.Clone();
                int i = c.species.FindIndex(s => s.name == "mackerel");
                c.species[i] = new SpeciesData.Source { name = "mackerel", text = "﻿" + c.species[i].text, bom = true };
                return c;
            } };
            yield return new Fx { name = "broken JSON", rule = "E1", file = F("catfish"), contains = "not valid JSON", make = () =>
            {
                var c = real.Clone();
                int i = c.species.FindIndex(s => s.name == "catfish");
                c.species[i] = new SpeciesData.Source { name = "catfish", text = c.species[i].text.Replace("\"rarity\":", "\"rarity\"") };
                return c;
            } };
            yield return new Fx { name = "a stray file in Data/Fish", rule = "E1", file = F("README"), make = () =>
            {
                var c = real.Clone();
                c.species.Add(new SpeciesData.Source { name = "README", text = "# notes\nnot a species\n" });
                return c;
            } };
            // ---- E2 unknown keys
            yield return new Fx { name = "a typo'd key (coverSeak)", rule = "E2", file = F("bluegill"), contains = "coverSeak",
                make = () => Species(real, "bluegill", j => j.Set("coverSeak", JNode.Num("0.5"))) };
            yield return new Fx { name = "a typo'd key in cover (seak)", rule = "E2", file = F("carp"), contains = "cover.seak",
                make = () => Species(real, "carp", j => j["cover"].Set("seak", JNode.Num("0.5"))) };
            // ---- E3 required values
            yield return new Fx { name = "missing power", rule = "E3", file = F("crucian_carp"), contains = "power is required",
                make = () => Species(real, "crucian_carp", j => j.Set("power", null)) };
            yield return new Fx { name = "missing name", rule = "E3", file = F("pale_chub"), contains = "name is required",
                make = () => Species(real, "pale_chub", j => j.Set("name", null)) };
            yield return new Fx { name = "missing time activity", rule = "E3", file = F("crucian_carp"), contains = "activity is required",
                make = () => Species(real, "crucian_carp", j => j.Set("activity", null)) };
            yield return new Fx { name = "missing one period's activity", rule = "E3", file = F("bluegill"), contains = "activity.night",
                make = () => Species(real, "bluegill", j => j["activity"].Set("night", null)) };
            yield return new Fx { name = "missing diet", rule = "E3", file = F("smelt"), contains = "diet is required",
                make = () => Species(real, "smelt", j => j.Set("diet", null)) };
            yield return new Fx { name = "unknown food", rule = "E3", file = F("smelt"), contains = "worms",
                make = () => Species(real, "smelt", j => j["diet"].Set("foods", Strs("worms"))) };
            yield return new Fx { name = "missing cover", rule = "E3", file = F("horse_mackerel"), contains = "cover is required",
                make = () => Species(real, "horse_mackerel", j => j.Set("cover", null)) };
            yield return new Fx { name = "a jumper without jumpStyle", rule = "E3", file = F("rainbow_trout"), contains = "jumpStyle",
                make = () => Species(real, "rainbow_trout", j => j.Set("jumpStyle", null)) };
            yield return new Fx { name = "bad rarity", rule = "E3", file = F("lenok"), contains = "rarity",
                make = () => Species(real, "lenok", j => j.Set("rarity", JNode.Str("legend"))) };
            yield return new Fx { name = "rarity as a digit", rule = "E3", file = F("lenok"), contains = "rarity",
                make = () => Species(real, "lenok", j => j.Set("rarity", JNode.Str("4"))) };
            yield return new Fx { name = "a number as a string", rule = "E3", file = F("burbot"), contains = "maxCm must be a number",
                make = () => Species(real, "burbot", j => j.Set("maxCm", JNode.Str("80"))) };
            yield return new Fx { name = "a stage value missing", rule = "E3", file = "stages.json", contains = "population is required",
                make = () => Roster(real, r => Stage(r, "swamp").Set("population", null)) };
            // ---- E4 ranges
            yield return new Fx { name = "minCm above maxCm", rule = "E4", file = F("arctic_char"), contains = "minCm",
                make = () => Species(real, "arctic_char", j => j.Set("minCm", JNode.Num("90"))) };
            yield return new Fx { name = "a surge feeder on pellets", rule = "E4", file = F("arowana"), contains = "surge",
                make = () => Species(real, "arowana", j => j["diet"].Set("foods", Strs("pellet", "sardine"))) };
            yield return new Fx { name = "three foods", rule = "E4", file = F("carp"), contains = "3 foods",
                make = () => Species(real, "carp", j => j["diet"].Set("foods", Strs("pellet", "shrimp", "sardine"))) };
            yield return new Fx { name = "never active", rule = "E4", file = F("cave_tetra"), contains = "every period",
                make = () => Species(real, "cave_tetra", j => { foreach (var k in SpeciesData.ActivityKeys) j["activity"].Set(k, JNode.Num("0")); }) };
            // ---- E5 baits
            yield return new Fx { name = "unknown bait id", rule = "E5", file = F("bluegill"), contains = "bait_wurm",
                make = () => Species(real, "bluegill", j => j.Set("baits", JNode.Str(j["baits"].text.Replace("worm:", "wurm:")))) };
            yield return new Fx { name = "unknown lure action", rule = "E5", file = F("largemouth_bass"), contains = "@twich",
                make = () => Species(real, "largemouth_bass", j => j.Set("baits", JNode.Str(j["baits"].text.Replace("@twitch", "@twich")))) };
            yield return new Fx { name = "a bait twice", rule = "E5", file = F("mackerel"), contains = "twice",
                make = () => Species(real, "mackerel", j => j.Set("baits", JNode.Str(j["baits"].text + ",jig:0.2"))) };
            yield return new Fx { name = "a bad bait weight", rule = "E5", file = F("rockfish"), contains = "not a plain number",
                make = () => Species(real, "rockfish", j => j.Set("baits", JNode.Str(j["baits"].text.Replace("squid:0.8", "squid:.8")))) };
            // ---- E6 cover
            yield return new Fx { name = "a cover type the stage never has", rule = "E6", file = F("mandarin_fish"), contains = "'tet'",
                make = () => Species(real, "mandarin_fish", j => j["cover"].Set("types", Strs("tet"))) };
            yield return new Fx { name = "a cover user on a stage without obstacles", rule = "E6", file = F("rainbow_trout"), contains = "no obstacles_stream.json",
                make = () =>
                {
                    var c = real.Clone();
                    var inner = real.obstacles;
                    c.obstacles = id => id == "stream" ? null : inner(id);
                    return c;
                } };
            // ---- E7 sprites
            yield return new Fx { name = "a new species without its sprites", rule = "E7", file = F("test_fish"), min = 4, make = () =>
            {
                var c = Roster(real, r => FishList(r, "lake").items.Add(Entry("test_fish", "5")));
                var j = DataJson.Parse(c.species.First(s => s.name == "crucian_carp").text, out _);
                j.Set("id", JNode.Str("test_fish"));
                c.species.Add(new SpeciesData.Source { name = "test_fish", text = DataJson.Write(j) });
                return c;
            } };
            yield return new Fx { name = "one top sprite missing", rule = "E7", file = F("carp"), contains = "carp_t1", make = () =>
            {
                var c = real.Clone();
                var inner = real.spriteExists;
                c.spriteExists = n => n != "carp_t1" && inner(n);
                return c;
            } };
            // ---- E8 exposure and stages
            yield return new Fx { name = "a species exposed on no stage", rule = "E8", file = F("carp"), contains = "no stage",
                make = () => Roster(real, r => FishList(r, "lake").items.RemoveAll(e => e["id"].text == "carp")) };
            yield return new Fx { name = "a stage naming an unknown species", rule = "E8", file = "stages.json", contains = "ghost_fish",
                make = () => Roster(real, r => FishList(r, "stream").items.Add(Entry("ghost_fish", "5"))) };
            yield return new Fx { name = "a species on two stages", rule = "E8", file = F("smelt"), contains = "more than one stage",
                make = () => Roster(real, r => FishList(r, "lake").items.Add(Entry("smelt", "5"))) };
            yield return new Fx { name = "a species twice on a stage", rule = "E8", file = "stages.json", contains = "listed twice",
                make = () => Roster(real, r => FishList(r, "cave").items.Add(Entry("cave_tetra", "5"))) };
            yield return new Fx { name = "a missing weight", rule = "E8", file = "stages.json", contains = "weight is required",
                make = () => Roster(real, r => FishList(r, "sea").items.First(e => e["id"].text == "rockfish").Set("weight", null)) };
            yield return new Fx { name = "a zero weight", rule = "E8", file = "stages.json", contains = "must be > 0",
                make = () => Roster(real, r => FishList(r, "sea").items.First(e => e["id"].text == "flounder").Set("weight", JNode.Num("0"))) };
            yield return new Fx { name = "no lake stage", rule = "E8", contains = "no \"lake\" stage",
                make = () => Roster(real, r => Stage(r, "lake").Set("id", JNode.Str("pond"))) };
            yield return new Fx { name = "a stage of legends only", rule = "E8", contains = "no ordinary",
                make = () => Roster(real, r => FishList(r, "cave").items.RemoveAll(e => e["id"].text != "coelacanth")) };
            // ---- E9 habitat
            yield return new Fx { name = "a lake species without its habitat", rule = "E9", file = F("bluegill"), contains = "required",
                make = () => Species(real, "bluegill", j => j.Set("habitat", null)) };
            yield return new Fx { name = "a habitat without its column", rule = "E9", file = F("carp"), contains = "column",
                make = () => Species(real, "carp", j => j.Set("habitat", JNode.Str(j["habitat"].text.Replace("col:bottom,", "")))) };
            yield return new Fx { name = "an unknown habitat key", rule = "E9", file = F("carp"), contains = "deepth",
                make = () => Species(real, "carp", j => j.Set("habitat", JNode.Str(j["habitat"].text + ",deepth:3"))) };
            // ---- E10 legends
            yield return new Fx { name = "an unknown encounter", rule = "E10", file = F("golden_carp"), contains = "golden_karp",
                make = () => Species(real, "golden_carp", j => j.Set("encounter", JNode.Str("golden_karp"))) };
            yield return new Fx { name = "a legend without its \"encounter\"", rule = "E10", file = F("sturgeon"), contains = "no \"encounter\"",
                make = () => Species(real, "sturgeon", j => j.Set("encounter", null)) };
            yield return new Fx { name = "a legend with an @action", rule = "E10", file = F("sturgeon"), contains = "@action",
                make = () => Species(real, "sturgeon", j => j.Set("baits", JNode.Str(j["baits"].text + ",@bottom:0.5"))) };
            yield return new Fx { name = "a key rule off the key lures", rule = "E10", file = F("great_white"), contains = "bait_jig",
                make = () => Species(real, "great_white", j => j.Set("baits", JNode.Str("kona:1"))) };
            yield return new Fx { name = "a legend without its model", rule = "E10", file = F("blue_marlin"), contains = "legend_blue_marlin.fbx", make = () =>
            {
                var c = real.Clone();
                var inner = real.modelExists;
                c.modelExists = n => n != "legend_blue_marlin" && inner(n);
                return c;
            } };
            // ---- E11 Blender
            yield return new Fx { name = "the Blender script missing the model", rule = "E11", file = F("lenok"), contains = "fish(\"lenok\"", blender = true, make = () =>
            {
                var c = real.Clone();
                c.fkFishPy = real.fkFishPy.Replace("fish(\"lenok\"", "fish_old(\"lenok\"");
                return c;
            } };
            yield return new Fx { name = "a legend without its rig script", rule = "E11", file = F("arapaima"), contains = "legends/arapaima.py", blender = true, make = () =>
            {
                var c = real.Clone();
                var inner = real.legendScript;
                c.legendScript = id => id == "arapaima" ? null : inner(id);
                return c;
            } };
            // ---- E12 the running game's load errors
            yield return new Fx { name = "load errors in the running game", rule = "E12", make = () =>
            {
                var c = real.Clone();
                c.loadErrors = new[] { "E3 Fish/x.json: test" };
                return c;
            } };
        }
    }
}
