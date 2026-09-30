using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// A legend as a real-time 3D model (Resources/Models/&lt;def.model&gt;, built by
    /// Tools/Blender/variants/hybrid/hyb_legend3d.py): rigid parts on a bone hierarchy, modelled 1.0 m long facing +Z and
    /// scaled to its size (cm / 100), posed every frame like <see cref="Reel3D"/>. Bone conventions (Unity localRotation
    /// Euler degrees on top of the rest pose): the spine and tail yaw about Y (+ turns a front bone's nose to +X, swings
    /// a back bone's tail to -X), the jaw opens with +X, the skull lifts with -X, Dorsal1 rises with +X, the left paired
    /// fins swing forward with +Y and the right ones with -Y, Dorsal2 / Anal scull about Z, the head pitches nose-down
    /// with +X.
    /// <para>The swim: a travelling wave down the spine (the legend's <see cref="Choreo.swim"/> amplitudes, the
    /// coelacanth's Spine.F 2, B1 4, B2 6, B3 8, Tail 12 degrees, 0.9 rad a joint), the paired fins trotting in diagonal
    /// pairs (Pec.L with Pel.R, Pec.R with Pel.L), the second dorsal and the anal fin sculling in anti-phase.</para>
    /// <para>Species bones (Docs/legends_rollout.md 7), each driven only when the model has it: Lips / UpperJaw protrude
    /// (the palette's <c>_rig.protrude</c>), Barbel.* curl and sway, Bill flexes, EyeRoll.* roll the eyeballs
    /// (<c>_rig.roll</c>), the <c>_rig.glow</c> materials light up; the Jaw / Dorsal1 clamps come from <c>_rig.limits</c>
    /// and the face outline from <c>_face</c> (the coelacanth has neither: its constants below).</para>
    /// </summary>
    public class Legend3D
    {
        /// <summary>This frame's pose (angles in degrees; phases in radians, advanced by the caller).</summary>
        public struct Pose
        {
            public float swim;          // tail-beat phase
            public float swimAmp;       // x the spine amplitudes (1 = the swim cycle)
            public float fin;           // paired-fin phase
            public float finAmp;        // x 25 degrees
            public float headYaw;       // + = nose to the fish's right
            public float bend;          // a turn bends the body (+ = curving to the right)
            public float jaw, skull, dorsal1;
            public float flare;         // 0..1: the pectoral lobes spread, the pelvics too
            public float tailSnap;      // extra tail yaw (a flinch)
            public float shake;         // extra head yaw (hooked head shakes)
            // ---- species channels (a bone the model lacks is skipped)
            public float headPitch;     // Head X (+ = nose down)
            public float protrude;      // 0..1: Lips / UpperJaw out (_rig.protrude)
            public float barbel;        // 0..1 the barbels curl down ("feel"); up to 1.4 for the fake-out's twitch
            public float eyeRoll;       // 0..1: the eyeballs roll back (_rig.roll)
            public float bill;          // extra Bill yaw
            public float glow;          // 0..1: the _rig.glow materials light up
        }

        public readonly GameObject Go;
        readonly Transform root;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        readonly Transform eyeL, eyeR, mouth;
        readonly List<Renderer> body = new List<Renderer>(), eyes = new List<Renderer>();
        readonly List<Material> mats = new List<Material>();
        /// <summary>World metres per model metre (the length in cm / 100).</summary>
        public float Scale { get; private set; } = 1f;

        static readonly int LurePosId = Shader.PropertyToID("_LurePos"), AbyssId = Shader.PropertyToID("_Abyss"),
            FogId = Shader.PropertyToID("_FogOutline"), SunMixId = Shader.PropertyToID("_SunMix"), KeyDirId = Shader.PropertyToID("_KeyDir"),
            LightId = Shader.PropertyToID("_Light");

        static readonly string[] Names =
        {
            "Root", "Spine.F", "Head", "Skull", "Jaw", "Pec.L", "Pec.L.Fan", "Pec.R", "Pec.R.Fan", "Dorsal1", "Spine.B1",
            "Pel.L", "Pel.L.Fan", "Pel.R", "Pel.R.Fan", "Spine.B2", "Dorsal2", "Dorsal2.Fan", "Anal", "Anal.Fan",
            "Spine.B3", "Tail", "Tail.Upper", "Tail.Mid", "Tail.Lower",
            // species bones (Docs/legends_rollout.md 7)
            "Lips", "UpperJaw", "Barbel.L1", "Barbel.R1", "Barbel.L2", "Barbel.R2", "Bill", "EyeRoll.L", "EyeRoll.R",
        };

        // ---- the palette's _rig
        class Protruder
        {
            public Transform t;
            public Vector3 restPos, move, tilt;
            public Quaternion restRot;
        }

        readonly List<Protruder> protruders = new List<Protruder>();
        readonly List<(Transform t, Vector3 axis, float deg)> rollers = new List<(Transform, Vector3, float)>();
        readonly Dictionary<string, float> limits = new Dictionary<string, float>();
        readonly List<(Material m, Color dim, Color lit)> glowMats = new List<(Material, Color, Color)>();
        Choreo prof = new Choreo();

        Legend3D(GameObject go, string palette)
        {
            Go = go;
            root = go.transform;
            foreach (var n in Names)
            {
                var t = ActorArt.Find(root, n);
                if (t == null) continue;
                bones[n] = t;
                rest[t] = t.localRotation;
            }
            eyeL = ActorArt.Find(root, "Eye.L");
            eyeR = ActorArt.Find(root, "Eye.R");
            mouth = ActorArt.Find(root, "Mouth");
            if (mouth != null) MouthBack = Mathf.Max(0f, 0.5f - root.InverseTransformPoint(mouth.position).z);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("geo_Eye")) eyes.Add(r);
                else body.Add(r);
                foreach (var m in r.sharedMaterials)
                    if (m != null && !mats.Contains(m)) mats.Add(m);
            }
            ReadRig(palette);
            var h = Bone("Head");
            // the face outline rides on the bones in model units: check it against the Mouth empty (on Head)
            string faceErr = "-";
            if (face == null && h != null && mouth != null)
            {
                float s = Mathf.Max(1e-4f, root.lossyScale.x);
                var want = h.position + h.rotation * ((MouthModel - HeadAt) * s);
                faceErr = ((want - mouth.position).magnitude / s).ToString("0.000");
            }
            Debug.Log($"[Legend3D] {go.name}: {bones.Count}/{Names.Length} bones, {body.Count} body + {eyes.Count} eye renderers, " +
                      $"{mats.Count} materials; Head rest {(h != null ? h.localRotation.eulerAngles.ToString("F1") : "-")} " +
                      $"eyes {(eyeL != null && eyeR != null)} mouth {(mouth != null)} face outline {(face != null ? "_face " + face.Count + " bones" : "err " + faceErr + " m")}; " +
                      $"rig: {protruders.Count} protrude, {rollers.Count} roll, {glowMats.Count} glow, limits {string.Join(" ", LimitsText())}");
        }

        IEnumerable<string> LimitsText()
        {
            foreach (var kv in limits) yield return kv.Key + " " + kv.Value.ToString("0");
        }

        // the coelacanth's face (what captions must never cover): the head's outline in model space (1 m model, nose tip
        // at z +0.50; legends/coelacanth.py), each point on the bone it moves with: the gill cover on Head, the upper head
        // and snout on Skull, the lower jaw on Jaw (so an open jaw counts). Models with a palette _face use that instead.
        static readonly Vector3 HeadAt = new Vector3(0f, 0f, 0.24f), MouthModel = new Vector3(0f, -0.010f, 0.49f);
        static readonly (string bone, Vector3 at, Vector3[] pts)[] CoelFace =
        {
            ("Head", HeadAt, new[]
            {
                new Vector3(0f, 0.082f, 0.29f), new Vector3(0f, -0.082f, 0.29f), new Vector3(0.046f, 0f, 0.29f), new Vector3(-0.046f, 0f, 0.29f),
            }),
            ("Skull", new Vector3(0f, 0.035f, 0.34f), new[]
            {
                new Vector3(0f, 0.075f, 0.33f), new Vector3(0f, 0.056f, 0.415f), new Vector3(0f, 0.030f, 0.475f), new Vector3(0f, -0.004f, 0.50f),
                new Vector3(0.043f, 0f, 0.33f), new Vector3(-0.043f, 0f, 0.33f), new Vector3(0.035f, 0.02f, 0.415f), new Vector3(-0.035f, 0.02f, 0.415f),
                new Vector3(0.024f, 0f, 0.475f), new Vector3(-0.024f, 0f, 0.475f),
            }),
            ("Jaw", new Vector3(0f, -0.025f, 0.33f), new[]
            {
                new Vector3(0f, -0.077f, 0.33f), new Vector3(0f, -0.064f, 0.415f), new Vector3(0f, -0.045f, 0.475f), new Vector3(0f, -0.016f, 0.50f),
                new Vector3(0.043f, -0.04f, 0.33f), new Vector3(-0.043f, -0.04f, 0.33f), new Vector3(0.03f, -0.05f, 0.45f), new Vector3(-0.03f, -0.05f, 0.45f),
            }),
        };

        List<(string bone, Vector3 at, Vector3[] pts)> face;

        /// <summary>The palette json's _rig (protrude, roll, limits, glow) and _face.</summary>
        void ReadRig(string palette)
        {
            var ta = Resources.Load<TextAsset>("Models/" + palette);
            if (ta == null) return;
            Dictionary<string, object> json;
            try { json = MiniJson.Parse(ta.text) as Dictionary<string, object>; }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Legend3D] {palette}: bad json ({ex.Message})");
                return;
            }
            if (json == null) return;
            if (json.TryGetValue("_rig", out var rigO) && rigO is Dictionary<string, object> rig)
            {
                foreach (var o in MiniJson.List(rig, "protrude"))
                {
                    if (!(o is Dictionary<string, object> p)) continue;
                    var t = Bone(MiniJson.Str(p, "bone"));
                    if (t == null) continue;
                    protruders.Add(new Protruder
                    {
                        t = t, restPos = t.localPosition, restRot = t.localRotation, move = MiniJson.Vec(p, "move"), tilt = MiniJson.Vec(p, "tilt"),
                    });
                }
                foreach (var o in MiniJson.List(rig, "roll"))
                {
                    if (!(o is Dictionary<string, object> p)) continue;
                    var t = Bone(MiniJson.Str(p, "bone"));
                    if (t != null) rollers.Add((t, MiniJson.Vec(p, "axis").normalized, MiniJson.Num(p, "deg", 150f)));
                }
                foreach (var o in MiniJson.List(rig, "limits"))
                    if (o is Dictionary<string, object> p && MiniJson.Str(p, "bone") != null) limits[MiniJson.Str(p, "bone")] = MiniJson.Num(p, "max", 0f);
                var tones = ActorArt.Palette(palette);
                foreach (var o in MiniJson.List(rig, "glow"))
                {
                    if (!(o is Dictionary<string, object> p)) continue;
                    string name = MiniJson.Str(p, "material");
                    if (name == null || !tones.TryGetValue(name, out var tone)) continue;
                    foreach (var m in mats)
                        if (m.name == "toon_" + name) glowMats.Add((m, tone.mid, tone.light));
                }
            }
            if (json.TryGetValue("_face", out var faceO) && faceO is List<object> fl)
            {
                face = new List<(string, Vector3, Vector3[])>();
                foreach (var o in fl)
                {
                    if (!(o is Dictionary<string, object> p)) continue;
                    string bone = MiniJson.Str(p, "bone");
                    var nums = MiniJson.List(p, "pts");
                    var pts = new Vector3[nums.Count / 3];
                    for (int i = 0; i < pts.Length; i++)
                        pts[i] = new Vector3(MiniJson.F(nums[3 * i]), MiniJson.F(nums[3 * i + 1]), MiniJson.F(nums[3 * i + 2]));
                    if (bone != null && pts.Length > 0) face.Add((bone, MiniJson.Vec(p, "at"), pts));
                }
                if (face.Count == 0) face = null;
            }
        }

        /// <summary>
        /// The face's points in world space as posed now: the eyes, the mouth and the head's outline from the gill cover to
        /// the snout, the lower jaw as opened (<paramref name="eyesOnly"/>: just the two eyes, for the eyes in the dark).
        /// </summary>
        public void FacePoints(List<Vector3> pts, bool eyesOnly)
        {
            pts.Add(eyeL.position);
            pts.Add(eyeR.position);
            if (eyesOnly) return;
            pts.Add(mouth.position);
            float s = Scale;
            if (face != null)
            {
                foreach (var (name, at, outline) in face)
                {
                    var b = Bone(name);
                    if (b == null) continue;
                    foreach (var p in outline) pts.Add(b.position + b.rotation * ((p - at) * s));
                }
                return;
            }
            foreach (var (name, at, outline) in CoelFace)
            {
                var b = Bone(name);
                if (b == null) continue;
                foreach (var p in outline) pts.Add(b.position + b.rotation * ((p - at) * s));
            }
        }

        /// <summary>The legend's model on the given layer, or null when the model (or its eyes / mouth) is missing.</summary>
        public static Legend3D TryCreate(EncounterDef def, int layer, Transform parent)
        {
            var prefab = ActorArt.Model(def.model);
            if (prefab == null)
            {
                Debug.LogWarning("[Legend3D] missing model " + def.model);
                return null;
            }
            var go = ActorArt.Spawn(prefab, def.model + "_palette", layer, parent);
            var l = new Legend3D(go, def.model + "_palette");
            if (l.eyeL == null || l.eyeR == null || l.mouth == null || !l.bones.ContainsKey("Head"))
            {
                Debug.LogWarning($"[Legend3D] {def.model}: missing Eye.L / Eye.R / Mouth / Head");
                Object.Destroy(go);
                return null;
            }
            l.prof = def.choreo ?? new Choreo();
            return l;
        }

        Transform Bone(string n) => n != null && bones.TryGetValue(n, out var t) ? t : null;

        public bool Has(string bone) => bones.ContainsKey(bone);

        public Vector3 EyeL => eyeL.position;
        public Vector3 EyeR => eyeR.position;
        /// <summary>Where each eye looks (its local +Z).</summary>
        public Vector3 EyeFwdL => eyeL.forward;
        public Vector3 EyeFwdR => eyeR.forward;
        public Vector3 Mouth => mouth.position;
        public Vector3 HeadPos => bones["Head"].position;
        /// <summary>The tail's root (the Tail bone; else 0.4 of the length behind the root).</summary>
        public Vector3 TailPos => bones.TryGetValue("Tail", out var t) ? t.position : root.position - root.forward * 0.4f * Scale;
        public Vector3 Forward => root.forward;
        /// <summary>How far the mouth sits behind the nose tip, in model metres (the nose tip is at z +0.50).</summary>
        public float MouthBack { get; private set; }

        /// <summary>The clamp of a hinged bone (Jaw 40 / Dorsal1 60 unless the palette's _rig.limits says otherwise).</summary>
        public float Limit(string bone, float fallback) => limits.TryGetValue(bone, out float v) ? v : fallback;

        /// <summary>
        /// Root (mid-body) at <paramref name="pos"/>, heading degrees (0 = +Z, + = towards +X), pitched (+ = nose down) and
        /// banked about its length.
        /// </summary>
        public void Place(Vector3 pos, float heading, float bank, float scale, float pitch = 0f)
        {
            Scale = scale;
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, heading, 0f) * Quaternion.Euler(pitch, 0f, 0f) * Quaternion.Euler(0f, 0f, bank));
            root.localScale = Vector3.one * scale;
        }

        void Rot(string n, float x, float y, float z)
        {
            var t = Bone(n);
            if (t != null) t.localRotation = rest[t] * Quaternion.Euler(x, y, z);
        }

        public void Apply(in Pose p)
        {
            float s = p.swim, a = p.swimAmp;
            var sw = prof.swim != null && prof.swim.Length >= 5 ? prof.swim : new Choreo().swim;
            const float step = 0.9f;
            // the head end swings a little against the tail (the body is nearly stiff, like the real fish)
            Rot("Spine.F", 0f, -sw[0] * a * Mathf.Sin(s + step) + p.bend * 0.3f, 0f);
            Rot("Head", p.headPitch, p.headYaw + p.shake + p.bend * 0.2f, 0f);
            Rot("Spine.B1", 0f, sw[1] * a * Mathf.Sin(s - step) - p.bend * 0.25f, 0f);
            Rot("Spine.B2", 0f, sw[2] * a * Mathf.Sin(s - 2f * step) - p.bend * 0.25f, 0f);
            Rot("Spine.B3", 0f, sw[3] * a * Mathf.Sin(s - 3f * step) - p.bend * 0.25f, 0f);
            Rot("Tail", 0f, sw[4] * a * Mathf.Sin(s - 4f * step) + p.tailSnap - p.bend * 0.25f, 0f);
            float lobe = 0.5f * sw[4] * a * Mathf.Sin(s - 4.6f * step);
            Rot("Tail.Upper", 0f, lobe, 0f);
            Rot("Tail.Mid", 0f, lobe * 1.3f, 0f);
            Rot("Tail.Lower", 0f, lobe, 0f);
            // the paired fins' diagonal trot (+Y is forward for the left fins, -Y for the right ones), each fan lagging
            float f = Mathf.Sin(p.fin), fl = Mathf.Sin(p.fin - 0.3f);
            float amp = 25f * p.finAmp * prof.finAmp * (1f - 0.6f * p.flare), fan = 15f * p.finAmp;
            float fx = prof.flare;
            Rot("Pec.L", 0f, amp * f + fx * p.flare, 0f);
            Rot("Pec.R", 0f, amp * f - fx * p.flare, 0f);
            Rot("Pel.L", 0f, -amp * f - fx * 2f / 3f * p.flare, 0f);
            Rot("Pel.R", 0f, -amp * f + fx * 2f / 3f * p.flare, 0f);
            Rot("Pec.L.Fan", 0f, fan * fl - 15f * p.flare, 0f);
            Rot("Pec.R.Fan", 0f, fan * fl + 15f * p.flare, 0f);
            Rot("Pel.L.Fan", 0f, -fan * fl, 0f);
            Rot("Pel.R.Fan", 0f, -fan * fl, 0f);
            float scull = prof.scull * Mathf.Sin(s * 0.8f);
            Rot("Dorsal2", 0f, 0f, scull);
            Rot("Anal", 0f, 0f, -scull);
            Rot("Dorsal2.Fan", 0f, 0f, scull * 0.4f);
            Rot("Anal.Fan", 0f, 0f, -scull * 0.4f);
            Rot("Dorsal1", Mathf.Clamp(p.dorsal1, 0f, Limit("Dorsal1", 60f)), 0f, 0f);
            Rot("Jaw", Mathf.Clamp(p.jaw, 0f, Limit("Jaw", 40f)), 0f, 0f);
            Rot("Skull", -Mathf.Clamp(p.skull, 0f, 10f), 0f, 0f);
            // ---- species bones
            float pr = Mathf.Clamp01(p.protrude);
            foreach (var q in protruders)
            {
                q.t.localPosition = q.restPos + q.move * pr;
                q.t.localRotation = q.restRot * Quaternion.Euler(q.tilt * pr);
            }
            // barbels: trail and sway with the swim wave (lagging 0.6 rad; the outer pair wider), curl onto the floor / lure
            float curl = prof.barbelSign * 25f * p.barbel;
            for (int k = 1; k <= 2; k++)
            {
                float sway = (k == 1 ? 8f : 12f) * Mathf.Max(0.3f, a) * Mathf.Sin(s - 4f * step - 0.6f);
                foreach (var side in new[] { "L", "R" })
                {
                    float sgn = side == "L" ? 1f : -1f;
                    if (prof.barbelRoll) Rot($"Barbel.{side}{k}", curl, 0f, sway * sgn);
                    else Rot($"Barbel.{side}{k}", curl, sway, 0f);
                }
            }
            Rot("Bill", 0f, p.bill + 4f * a * Mathf.Sin(s + step - 0.6f), 0f);
            float r = Mathf.Clamp01(p.eyeRoll);
            foreach (var (t, axis, deg) in rollers) t.localRotation = rest[t] * Quaternion.AngleAxis(deg * r, axis);
            float g = Mathf.Clamp01(p.glow);
            foreach (var (m, dim, lit) in glowMats) m.SetColor(LightId, Color.Lerp(dim, lit, g));
        }

        /// <summary>For the -fkencdebug log: the root, the first body renderer's bounds and state, a material's light.</summary>
        public string DebugText()
        {
            var r = body.Count > 0 ? body[0] : null;
            var m = mats.Count > 0 ? mats[0] : null;
            return $"root {root.position:F2} s {root.lossyScale.x:0.00} layer {Go.layer} body0 {(r != null ? r.name + " en " + r.enabled + " vis " + r.isVisible + " b " + r.bounds.center.ToString("F2") + "/" + r.bounds.size.ToString("F2") : "-")} " +
                   $"mat {(m != null ? m.name + " sh " + (m.shader != null ? m.shader.name + " ok " + m.shader.isSupported : "null") + " lure " + m.GetVector(LurePosId).ToString("F2") + " sun " + m.GetFloat(SunMixId).ToString("0.00") : "-")}";
        }

        /// <summary>Only the eyes glowing in the dark (false), or the whole fish.</summary>
        public void SetBodyVisible(bool on)
        {
            foreach (var r in body) r.enabled = on;
        }

        public void SetEyesVisible(bool on)
        {
            foreach (var r in eyes) r.enabled = on;
        }

        public void SetVisible(bool on)
        {
            SetBodyVisible(on);
            SetEyesVisible(on);
        }

        /// <summary>
        /// The encounter's darkness: lit by the lure (xyz world, w radius m; w = 0 turns it off), and in a daylight set
        /// by the sun too (<paramref name="sunMix"/> of the key towards <paramref name="keyDir"/>; 0 = the lure alone).
        /// </summary>
        public void SetLight(Vector4 lure, Color abyss, Color fog, float sunMix = 0f, Vector3 keyDir = default)
        {
            var key = keyDir.sqrMagnitude > 1e-6f ? keyDir.normalized : ActorArt.KeyDir.normalized;
            foreach (var m in mats)
            {
                m.SetVector(LurePosId, lure);
                m.SetColor(AbyssId, abyss);
                m.SetColor(FogId, fog);
                m.SetFloat(SunMixId, sunMix);
                m.SetVector(KeyDirId, key);
            }
        }

        public void Destroy()
        {
            SetLight(Vector4.zero, Color.black, Color.black);
            foreach (var (m, dim, lit) in glowMats) m.SetColor(LightId, lit);
            if (Go != null) Object.Destroy(Go);
        }
    }

    /// <summary>A small JSON reader for the palette's _rig / _face (objects, arrays, numbers, strings, true / false / null).</summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new System.FormatException("end of input");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws(s, ref i);
                if (s[i] == '}')
                {
                    i++;
                    return d;
                }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new System.FormatException("':' expected at " + i);
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new System.FormatException("',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Ws(s, ref i);
                if (s[i] == ']')
                {
                    i++;
                    return l;
                }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new System.FormatException("',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
            int st = i;
            while (i < s.Length && ("+-.eE".IndexOf(s[i]) >= 0 || char.IsDigit(s[i]))) i++;
            return double.Parse(s.Substring(st, i - st), System.Globalization.CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new System.FormatException("'\"' expected at " + i);
            i++;
            var sb = new System.Text.StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'u')
                    {
                        sb.Append((char)System.Convert.ToInt32(s.Substring(i + 1, 4), 16));
                        i += 4;
                    }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }

        public static List<object> List(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();

        public static string Str(Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) ? v as string : null;

        public static float F(object o) => o is double x ? (float)x : 0f;

        public static float Num(Dictionary<string, object> d, string key, float fallback) =>
            d != null && d.TryGetValue(key, out var v) && v is double x ? (float)x : fallback;

        public static Vector3 Vec(Dictionary<string, object> d, string key)
        {
            var l = List(d, key);
            return l.Count >= 3 ? new Vector3(F(l[0]), F(l[1]), F(l[2])) : Vector3.zero;
        }
    }
}
