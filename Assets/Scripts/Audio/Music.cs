using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The background music (Tools/Music/README.md 4, Docs/music.md). Cues come from Resources/Data/music.json, written by
    /// Tools/Music/fk_music.py: a loop cue is a set of stems (Resources/Audio/Music/&lt;cue&gt;_&lt;stem&gt;) of the same length,
    /// played on a <em>deck</em> — one AudioSource per stem, all started with PlayScheduled on the same DSP time once their
    /// clips have loaded, so the stems stay sample-locked and are mixed live (a stage's day / night / fight layers). Each
    /// stem has its own level with a fade; a new cue crossfades (the old deck fades out once the new one has started,
    /// then stops and unloads its clips); a sting plays once on its own source and ducks the deck while it sounds, then
    /// lets it back up. A loop cue (or the manifest) that is missing falls back to the old chiptune (<see cref="Sfx.Music"/>)
    /// so the game is never silent while the tracks are being made. Lives on the persistent [Game] object; every fade
    /// runs on unscaled time. 설정 → 배경음 (<see cref="SaveData.musicOn"/>) switches it off and on; 소리 (AudioListener)
    /// still silences everything.
    /// <code>
    /// -fkmusic off        no music at all (the save's setting is left alone)
    /// -fkmusic chiptune   the manifest ignored: every loop cue is the old chiptune, no stings
    /// -fkmusiclog         a [MUSIC] line for every play / stem / sting / duck / start / stop
    /// </code>
    /// </summary>
    public class Music : MonoBehaviour
    {
        /// <summary>
        /// Master volume of every deck and sting. The tracks are normalized to an RMS of -17 (legend fights, stings) ..
        /// -22 dBFS (stage beds -21, menus -19), the old chiptune loop sat at about -33 dBFS (an unboosted square-wave mix
        /// at 0.22), the water ambience at about -34 dBFS (0.35) and the synthesized one-shots peak at 0.9 of full scale.
        /// 0.4 (-8 dB) puts a stage bed near -29 dBFS: a few dB over the chiptune it replaces (real instruments sound
        /// softer than square waves at the same RMS), still far under the effects, with the ambience heard in its rests.
        /// </summary>
        public const float Volume = 0.4f;
        /// <summary>The crossfade between the menu scenes' cues (title / map / aquarium).</summary>
        public const float SceneFade = 1.5f;
        /// <summary>Seconds between scheduling a deck and its DSP start (every stem gets the same start).</summary>
        const double StartLead = 0.06;
        /// <summary>A new deck still loading after this long: the old one fades out anyway (no long overlap).</summary>
        const float LoadTimeout = 3f;
        /// <summary>A sting whose clip is not loaded within this long is dropped (a late sting is worse than none).</summary>
        const float StingMaxWait = 0.75f;
        /// <summary>The sting's duck: down over StingAttack s, back up over StingRelease s starting StingLead s before its end.</summary>
        const float StingAttack = 0.12f, StingRelease = 0.9f, StingLead = 0.35f;
        /// <summary>Music turned off (setting / -fkmusic off): the decks fade out over this long.</summary>
        const float OffFade = 0.4f;

        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static Music I;
        /// <summary>-fkmusiclog: a [MUSIC] line for every event.</summary>
        public static bool Log;
        static bool forceOff, forceChip;

        // ------------------------------------------------------------------ the manifest (fk_music._register)
        [Serializable]
        public class StemDef
        {
            public string name;
            public string clip;   // a Resources path, e.g. "Audio/Music/stage_lake_day"
        }

        [Serializable]
        public class CueDef
        {
            public string id;
            public string kind;   // "loop" | "sting"
            public float bpm;
            public int beatsPerBar, bars;
            public int loopSamples;   // a loop's length in samples (0 for a sting)
            public string desc;
            public StemDef[] stems;
            public bool Loop => kind == "loop";
        }

        [Serializable]
        public class Manifest
        {
            public int rate;
            public CueDef[] cues;
        }

        /// <summary>A value easing to a target over a time on an equal-power curve (a rise quick at first, a fall slow at first: a crossfade keeps its loudness).</summary>
        struct Ramp
        {
            public float v, from, to, t, dur;

            public static Ramp At(float v) => new Ramp { v = v, from = v, to = v };

            public void Go(float target, float seconds)
            {
                from = v;
                to = target;
                t = 0f;
                dur = Mathf.Max(0f, seconds);
                if (dur <= 0f) v = to;
            }

            public void Step(float dt)
            {
                if (v == to) return;
                t += dt;
                if (t >= dur)
                {
                    v = to;
                    return;
                }
                float x = t / dur;
                float k = to > from ? Mathf.Sin(x * Mathf.PI * 0.5f) : 1f - Mathf.Cos(x * Mathf.PI * 0.5f);
                v = Mathf.Lerp(from, to, k);
            }
        }

        /// <summary>One loop cue playing: an AudioSource per stem (null = its clip is missing), started together.</summary>
        class Deck
        {
            public CueDef cue;
            public GameObject go;
            public AudioSource[] src;
            public AudioClip[] clip;
            public Ramp[] level;
            public Ramp fade = Ramp.At(0f);
            public float fadeIn;           // the fade-in it starts with once its stems are playing
            public float hold = 1f;        // the duck it had when it stopped being the current deck
            public float outAfter = -1f;   // dying: the fade-out it starts once the new deck has started
            public float age;
            public bool started, dying;

            public int Index(string stem) => Array.FindIndex(cue.stems, s => s.name == stem);
        }

        Manifest manifest;
        readonly Dictionary<string, CueDef> cues = new Dictionary<string, CueDef>();
        readonly Dictionary<string, AudioClip> stingClips = new Dictionary<string, AudioClip>();
        readonly List<Deck> decks = new List<Deck>();
        readonly HashSet<string> told = new HashSet<string>();
        Deck current;
        // the deck's ducks: Duck() (the game's, e.g. a bite) and the sting's
        Ramp duck = Ramp.At(1f), sduck = Ramp.At(1f);
        AudioSource stingSrc;
        CueDef stingCue;
        AudioClip stingClip;
        bool stingWaiting, stingReleased;
        float stingWait, stingDuckTo = 1f;
        bool chip, wasOn;
        // what the game asked for last (played again when the music is switched back on)
        string wanted;
        readonly Dictionary<string, float> wantLevels = new Dictionary<string, float>();

        static string F(float v) => v.ToString("0.00", CI);

        static void Say(string m)
        {
            if (Log) Debug.Log("[MUSIC] " + m);
        }

        /// <summary>A missing cue / clip / manifest: one line per thing, always (the tracks arrive while the game is played).</summary>
        void Missing(string what)
        {
            if (told.Add(what)) Debug.Log("[MUSIC] missing " + what);
        }

        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void Awake()
        {
            I = this;
            string mode = Arg("-fkmusic");
            forceOff = mode == "off";
            forceChip = mode == "chiptune";
            Log = Array.IndexOf(Environment.GetCommandLineArgs(), "-fkmusiclog") >= 0;
            stingSrc = gameObject.AddComponent<AudioSource>();
            stingSrc.playOnAwake = false;
            stingSrc.loop = false;
            stingSrc.priority = 0;
            stingSrc.volume = Volume;
            LoadManifest();
            wasOn = On;
        }

        void LoadManifest()
        {
            var ta = Resources.Load<TextAsset>("Data/music");
            if (ta == null)
            {
                Missing("Data/music.json (every cue falls back to the chiptune)");
                return;
            }
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(ta.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MUSIC] could not read Data/music.json: " + e.Message);
            }
            if (manifest?.cues == null) return;
            foreach (var c in manifest.cues)
                if (c != null && !string.IsNullOrEmpty(c.id) && c.stems != null && c.stems.Length > 0) cues[c.id] = c;
            // the stings are short and come at moments that must not wait for a load: kept in memory from the start
            foreach (var c in cues.Values.Where(c => !c.Loop))
            {
                var clip = string.IsNullOrEmpty(c.stems[0].clip) ? null : Resources.Load<AudioClip>(c.stems[0].clip);
                if (clip == null) continue;
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                stingClips[c.id] = clip;
            }
            Say($"manifest: {cues.Count} cues ({cues.Values.Count(c => c.Loop)} loops, {stingClips.Count} stings loaded), " +
                $"mode {(forceOff ? "off" : forceChip ? "chiptune" : "normal")}");
        }

        /// <summary>Music may sound: not -fkmusic off and 설정 → 배경음 on.</summary>
        static bool On => !forceOff && (Game.I == null || Game.Data.musicOn);

        CueDef Find(string id, bool loop)
        {
            if (forceChip || string.IsNullOrEmpty(id) || !cues.TryGetValue(id, out var c) || c.Loop != loop) return null;
            return c;
        }

        // ================================================================== the API
        /// <summary>
        /// Plays a loop cue: <paramref name="stems"/> at their levels (0..1) and every other stem at 0; none given = every
        /// stem at 1. The cue already playing only moves its stems to the new levels over <paramref name="fade"/> s;
        /// another cue crossfades in over <paramref name="fade"/> s. A cue that is missing plays the chiptune instead.
        /// </summary>
        public static void Play(string cue, float fade = SceneFade, params (string stem, float level)[] stems)
        {
            if (I != null && !string.IsNullOrEmpty(cue)) I.DoPlay(cue, fade, stems);
        }

        /// <summary>One stem of the current cue to <paramref name="level"/> over <paramref name="seconds"/>.</summary>
        public static void Stem(string name, float level, float seconds)
        {
            if (I == null || string.IsNullOrEmpty(name)) return;
            level = Mathf.Clamp01(level);
            var wc = I.Find(I.wanted, true);
            if (wc != null && wc.stems.Any(s => s.name == name)) I.wantLevels[name] = level;
            var d = I.current;
            if (d == null) return;
            int i = d.Index(name);
            if (i < 0)
            {
                I.Missing($"stem {name} in {d.cue.id}");
                return;
            }
            if (d.level[i].to == level) return;
            d.level[i].Go(level, seconds);
            Say($"stem {d.cue.id}.{name} -> {F(level)} ({F(seconds)} s)");
        }

        /// <summary>Plays a sting once; the current deck goes down to <paramref name="duck"/> while it sounds, then back up.</summary>
        public static void Sting(string cue, float duck = 0.35f)
        {
            if (I != null && !string.IsNullOrEmpty(cue)) I.DoSting(cue, duck);
        }

        /// <summary>The current deck (and any deck that becomes current) x <paramref name="level"/>, eased over <paramref name="seconds"/>.</summary>
        public static void Duck(float level, float seconds)
        {
            if (I == null) return;
            level = Mathf.Clamp01(level);
            if (I.duck.to == level) return;
            I.duck.Go(level, seconds);
            Say($"duck {F(level)} ({F(seconds)} s)");
        }

        /// <summary>The current cue fades out over <paramref name="fade"/> s (and nothing stands in for it, not even the chiptune).</summary>
        public static void Stop(float fade = SceneFade)
        {
            if (I == null) return;
            I.wanted = null;
            I.wantLevels.Clear();
            if (I.current != null) Say($"stop {I.current.cue.id} ({F(fade)} s)");
            I.Demote(I.current, fade);
            I.Chip(false);
        }

        /// <summary>The cue of the deck playing now (null: none, or the chiptune stands in for a missing one).</summary>
        public static string Current => I != null && I.current != null ? I.current.cue.id : null;

        /// <summary>The loop cue last asked for (kept while the music is off or the chiptune stands in for it).</summary>
        public static string Wanted => I != null ? I.wanted : null;

        /// <summary>The chiptune is standing in for a missing cue.</summary>
        public static bool Chiptune => I != null && I.chip;

        /// <summary>The manifest has this loop cue (its clips may still fail to load: then the chiptune plays).</summary>
        public static bool Has(string cue) => I != null && I.Find(cue, true) != null;

        /// <summary>The sting sounding (or about to: its clip still loading), else null.</summary>
        public static string StingNow => I != null && I.stingCue != null && (I.stingWaiting || I.stingSrc.isPlaying) ? I.stingCue.id : null;

        /// <summary>Seconds left of the sting sounding (its whole length while it loads), 0 when none.</summary>
        public static float StingLeft
        {
            get
            {
                if (I == null || I.stingCue == null || I.stingClip == null) return 0f;
                if (I.stingWaiting) return I.stingClip.length;
                if (!I.stingSrc.isPlaying) return 0f;
                return Mathf.Max(0f, (I.stingClip.samples - I.stingSrc.timeSamples) / (float)Mathf.Max(1, I.stingClip.frequency));
            }
        }

        // ------------------------------------------------------------------ for the tests (-fkauto music)
        /// <summary>The current deck has been made but its stems are not playing yet (clips loading).</summary>
        internal static bool Loading => I != null && I.current != null && !I.current.started;

        /// <summary>The ducks on the current deck now (the game's x the sting's).</summary>
        internal static float DuckNow => I != null ? I.duck.v * I.sduck.v : 1f;

        /// <summary>How loud a stem of the current deck is now, 0..1 of <see cref="Volume"/> (its level x the deck's fade x the ducks).</summary>
        internal static float Gain(string stem)
        {
            var d = I?.current;
            if (d == null || !d.started) return 0f;
            int i = d.Index(stem);
            if (i < 0 || d.src[i] == null) return 0f;
            return d.fade.v * d.level[i].v * I.duck.v * I.sduck.v;
        }

        /// <summary>The decks alive (the current one and those still fading out).</summary>
        internal static int DeckCount => I != null ? I.decks.Count : 0;

        /// <summary>The manifest's cue ids.</summary>
        internal static IEnumerable<string> CueIds => I != null ? I.cues.Keys : Enumerable.Empty<string>();

        /// <summary>The manifest has this sting and its clip.</summary>
        internal static bool HasSting(string cue) => I != null && I.Find(cue, false) != null && I.stingClips.ContainsKey(cue);

        /// <summary>
        /// The widest gap between the current deck's playing stems (ms, the loop's wrap taken into account); -1 with fewer
        /// than two. (The mixer moves every position on its own thread, a block at a time: a read that a block landed in
        /// the middle of — the first stem's position changed by the end — is taken again.)
        /// </summary>
        internal static float SyncMs()
        {
            var d = I?.current;
            if (d == null || !d.started) return -1f;
            int first = -1;
            for (int i = 0; i < d.src.Length && first < 0; i++)
                if (d.src[i] != null && d.src[i].isPlaying) first = i;
            if (first < 0) return -1f;
            var a = d.src[first];
            int len = Mathf.Max(1, d.clip[first].samples), rate = Mathf.Max(1, d.clip[first].frequency);
            int n = 0, spread = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int p0 = a.timeSamples;
                n = 1;
                spread = 0;
                for (int i = first + 1; i < d.src.Length; i++)
                {
                    var s = d.src[i];
                    if (s == null || !s.isPlaying) continue;
                    int gap = Mathf.Abs(s.timeSamples - p0);
                    spread = Mathf.Max(spread, Mathf.Min(gap, len - gap));
                    n++;
                }
                if (a.timeSamples == p0) break;
            }
            return n < 2 ? -1f : spread * 1000f / rate;
        }

        /// <summary>One line on what sounds: the deck and its stems' gains, the ducks, the sting, the chiptune.</summary>
        internal static string Describe()
        {
            if (I == null) return "no music";
            var d = I.current;
            string deck = d == null ? "none" : d.cue.id + (d.started ? "" : " (loading)") + " [" +
                string.Join(" ", d.cue.stems.Select(s => s.name + " " + F(Gain(s.name)))) + "]";
            string sting = StingNow != null ? $"{StingNow} {F(StingLeft)} s" : "-";
            return $"{deck} duck {F(I.duck.v)}x{F(I.sduck.v)} sting {sting} chip {(I.chip ? "on" : "off")} " +
                   $"dying {I.decks.Count(x => x.dying)} on {On}";
        }

        // ================================================================== decks
        void DoPlay(string id, float fade, (string stem, float level)[] stems)
        {
            var c = Find(id, true);
            wanted = id;
            wantLevels.Clear();
            if (c != null)
                foreach (var st in c.stems) wantLevels[st.name] = LevelOf(st.name, stems);
            if (!On) return;
            if (c == null)
            {
                Missing($"cue {id}{(forceChip ? " (-fkmusic chiptune)" : "")}");
                Fallback();
                return;
            }
            if (current != null && current.cue == c)
            {
                SetLevels(current, stems, fade);
                Say($"play {id} (already on) {Levels(current)} over {F(fade)} s");
                return;
            }
            // a deck of this cue still fading out: brought back up instead of a second copy of the same clips
            var back = decks.FirstOrDefault(x => x.dying && x.cue == c);
            Demote(current, fade);
            if (back != null)
            {
                back.dying = false;
                back.outAfter = -1f;
                // (as it was heard: its frozen duck folded into the fade, which then rises from there)
                back.fade.v = Mathf.Clamp01(back.fade.v * back.hold / Mathf.Max(0.001f, duck.v * sduck.v));
                back.fade.Go(1f, fade);
                back.hold = 1f;
                SetLevels(back, stems, fade);
                current = back;
                Chip(false);
                Say($"play {id}: back up from its fade-out, {Levels(back)} over {F(fade)} s");
                return;
            }
            var d = NewDeck(c, stems, fade);
            if (d == null)
            {
                Fallback();
                return;
            }
            current = d;
            Say($"play {id}: {Levels(d)}, fade {F(fade)} s");
        }

        Deck NewDeck(CueDef c, (string stem, float level)[] stems, float fade)
        {
            var go = new GameObject("Music " + c.id);
            go.transform.SetParent(transform, false);
            int n = c.stems.Length;
            var d = new Deck { cue = c, go = go, src = new AudioSource[n], clip = new AudioClip[n], level = new Ramp[n], fadeIn = fade };
            for (int i = 0; i < n; i++)
            {
                string path = c.stems[i].clip;
                var clip = string.IsNullOrEmpty(path) ? null : Resources.Load<AudioClip>(path);
                if (clip == null)
                {
                    Missing($"clip {path} ({c.id}.{c.stems[i].name})");
                    continue;
                }
                if (c.loopSamples > 0 && clip.samples != c.loopSamples && told.Add("len " + clip.name))
                    Debug.LogWarning($"[MUSIC] {clip.name}: {clip.samples} samples, the loop is {c.loopSamples} (the stems will drift apart)");
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = true;
                s.priority = 0;
                s.volume = 0f;
                s.clip = clip;
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                d.src[i] = s;
                d.clip[i] = clip;
            }
            if (d.src.All(s => s == null))
            {
                Destroy(go);
                return null;
            }
            SetLevels(d, stems, 0f);
            decks.Add(d);
            return d;
        }

        void SetLevels(Deck d, (string stem, float level)[] stems, float seconds)
        {
            for (int i = 0; i < d.level.Length; i++)
            {
                float l = LevelOf(d.cue.stems[i].name, stems);
                if (d.level[i].to != l || !d.started) d.level[i].Go(l, d.started ? seconds : 0f);
            }
            foreach (var (s, _) in stems)
                if (d.Index(s) < 0) Missing($"stem {s} in {d.cue.id}");
        }

        /// <summary>A stem's level in a Play: as given, 0 when others are given and it is not, 1 when none are.</summary>
        static float LevelOf(string stem, (string stem, float level)[] stems)
        {
            float l = stems.Length == 0 ? 1f : 0f;
            foreach (var (s, v) in stems)
                if (s == stem) l = Mathf.Clamp01(v);
            return l;
        }

        static string Levels(Deck d) => string.Join(" ", d.cue.stems.Select((s, i) => s.name + " " + F(d.level[i].to)));

        /// <summary>The current deck stops being current: it fades out over <paramref name="fade"/> s once the next one plays.</summary>
        void Demote(Deck d, float fade)
        {
            if (d == null) return;
            if (current == d) current = null;
            if (!d.started)
            {
                Kill(d);
                return;
            }
            d.hold = duck.v * sduck.v;
            d.dying = true;
            d.outAfter = Mathf.Max(0f, fade);
        }

        void Kill(Deck d)
        {
            decks.Remove(d);
            if (current == d) current = null;
            for (int i = 0; i < d.src.Length; i++)
            {
                if (d.src[i] != null) d.src[i].Stop();
                var clip = d.clip[i];
                // (unless another deck still plays the same clip)
                if (clip != null && !decks.Any(o => o.clip.Contains(clip))) clip.UnloadAudioData();
            }
            Destroy(d.go);
            Say($"free {d.cue.id}");
        }

        /// <summary>A missing cue: the decks fade out and the chiptune plays.</summary>
        void Fallback()
        {
            Demote(current, 1f);
            Chip(true);
        }

        void Chip(bool on)
        {
            if (chip == on) return;
            chip = on;
            Sfx.Music(on);
            Say("chiptune " + (on ? "on (fallback)" : "off"));
        }

        void TryStart(Deck d)
        {
            bool ready = true;
            for (int i = 0; i < d.src.Length; i++)
            {
                var clip = d.clip[i];
                if (clip == null) continue;
                var st = clip.loadState;
                if (st == AudioDataLoadState.Failed)
                {
                    Missing($"clip {clip.name} could not load");
                    Destroy(d.src[i]);
                    d.src[i] = null;
                    d.clip[i] = null;
                }
                else if (st != AudioDataLoadState.Loaded)
                {
                    if (st == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                    ready = false;
                }
            }
            if (d.src.All(s => s == null))
            {
                bool wasCurrent = d == current;
                Kill(d);
                if (wasCurrent) Fallback();
                return;
            }
            if (!ready) return;
            double at = AudioSettings.dspTime + StartLead;
            foreach (var s in d.src)
                if (s != null) s.PlayScheduled(at);
            d.started = true;
            d.fade.Go(1f, d.fadeIn);
            if (d == current) Chip(false);
            Say(string.Format(CI, "start {0}: {1} stems at dsp {2:0.000} after {3:0.00} s loading", d.cue.id, d.src.Count(s => s != null), at, d.age));
        }

        // ================================================================== stings
        void DoSting(string id, float duckTo)
        {
            if (!On) return;
            var c = Find(id, false);
            if (c == null || !stingClips.TryGetValue(id, out var clip) || clip == null)
            {
                Missing($"sting {id}");
                return;
            }
            stingSrc.Stop();
            stingCue = c;
            stingClip = clip;
            stingDuckTo = Mathf.Clamp01(duckTo);
            stingWait = 0f;
            stingWaiting = true;
            stingReleased = false;
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            Say($"sting {id} (duck {F(stingDuckTo)}, {F(clip.length)} s)");
            TickSting(0f);
        }

        void TickSting(float dt)
        {
            if (stingCue == null) return;
            if (stingWaiting)
            {
                stingWait += dt;
                var st = stingClip.loadState;
                if (st == AudioDataLoadState.Loaded)
                {
                    stingWaiting = false;
                    stingSrc.clip = stingClip;
                    stingSrc.volume = Volume;
                    stingSrc.Play();
                    sduck.Go(stingDuckTo, StingAttack);
                }
                else if (st == AudioDataLoadState.Failed || stingWait > StingMaxWait)
                {
                    stingWaiting = false;
                    stingReleased = true;
                    Missing($"sting {stingCue.id} ({(st == AudioDataLoadState.Failed ? "could not load" : "too slow to load: dropped")})");
                }
                return;
            }
            if (stingReleased) return;
            if (!stingSrc.isPlaying || StingLeft <= StingLead)
            {
                stingReleased = true;
                sduck.Go(1f, StingRelease);
            }
        }

        // ================================================================== the frame
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool on = On;
            if (on != wasOn)
            {
                wasOn = on;
                if (on) TurnOn();
                else TurnOff();
            }
            duck.Step(dt);
            TickSting(dt);
            sduck.Step(dt);
            for (int k = decks.Count - 1; k >= 0; k--)
            {
                var d = decks[k];
                d.age += dt;
                if (!d.started)
                {
                    TryStart(d);
                    if (!decks.Contains(d) || !d.started) continue;
                }
                if (d.dying && d.outAfter >= 0f && (current == null || current.started || current.age > LoadTimeout))
                {
                    d.fade.Go(0f, d.outAfter);
                    d.outAfter = -1f;
                }
                d.fade.Step(dt);
                for (int i = 0; i < d.level.Length; i++) d.level[i].Step(dt);
                if (d.dying && d.outAfter < 0f && d.fade.v <= 0f)
                {
                    Kill(d);
                    continue;
                }
                float g = Volume * d.fade.v * (d == current ? duck.v * sduck.v : d.hold);
                for (int i = 0; i < d.src.Length; i++)
                    if (d.src[i] != null) d.src[i].volume = g * d.level[i].v;
            }
        }

        /// <summary>Switched off (설정 → 배경음): every deck fades out and is freed, the sting and the chiptune stop; what was asked for is kept.</summary>
        void TurnOff()
        {
            Say("off");
            Demote(current, OffFade);
            foreach (var d in decks.Where(x => !x.dying).ToList()) Demote(d, OffFade);
            stingSrc.Stop();
            stingWaiting = false;
            stingReleased = true;
            sduck = Ramp.At(1f);
            Chip(false);
        }

        /// <summary>Switched back on: the cue last asked for, at the levels last asked for.</summary>
        void TurnOn()
        {
            Say("on");
            if (wanted == null) return;
            var levels = wantLevels.Select(kv => (kv.Key, kv.Value)).ToArray();
            DoPlay(wanted, 1f, levels);
        }
    }
}
