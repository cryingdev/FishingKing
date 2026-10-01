using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The angler as a real-time 3D model (Resources/Models/angler, rigid parts on a 20-bone rig, contract in
    /// Tools/Blender/_tmp/actors3d/actors3d_notes.md), posed every frame on its <see cref="ActorLayer"/>:
    /// body posture per pose like the sprite poses (lean back, crouch, upper-body turn, right shoulder rolled forward,
    /// stance) blended over ~0.15 s, legs by two-bone IK with the feet planted, the LEFT hand's grip on the rod grip and
    /// the RIGHT hand's grip on the reel knob (or the rod behind the left hand) by analytic two-bone IK, the head
    /// looking ahead. The whole figure is yawed 10 degrees to the right like the sprites; on top of that the body turns
    /// by <see cref="BodyYaw"/> (feet pivoting, hips, spine and chest each taking a share) and the head by
    /// <see cref="HeadYaw"/> towards where he fishes, and the feet stay planted, stepping when he walks (<see cref="Steps"/>).
    /// All targets are game-space points; a target out of reach slides along its camera ray until it is reachable,
    /// so on screen the fist still lands exactly on it (the sprites did the same).
    /// </summary>
    public class Angler3D
    {
        public const float Yaw = 10f;          // the sprites' body turn towards the screen right (hyb_character TURN = 80)
        const float Tau = 0.05f;               // posture smoothing time constant (~95 % after 0.15 s)
        const float ReachUse = 0.985f;         // never quite straighten an arm
        // how the body turn is shared from the ground up (sums to 1: the chest ends up turned by the whole BodyYaw)
        const float FeetShare = 0.35f, HipShare = 0.25f, SpineShare = 0.2f, ChestShare = 0.2f;
        // stepping: a planted foot steps once it is StepAt (m) from its place, one foot at a time, StepTime s per step
        // (walking, the steps follow each other: ~2.7 steps/s), lifting the ankle Lift m and landing Lead of a step ahead
        // of its place so that while planted it lags from half a step ahead to half a step behind (the legs reach it);
        // walking widens the stance by Spread each side so the shuffling feet never cross, and bends the knees (the
        // hips drop WalkCrouch) for the wider stance
        const float StepAt = 0.1f, StepTime = 0.36f, Lift = 0.05f, Spread = 0.1f, Lead = 0.5f, WalkCrouch = 0.05f;
        const float SlideSpeed = 0.5f;         // m/s: standing still, small stance changes (pose, turn) just slide

        /// <summary>Degrees the body turns on top of <see cref="Yaw"/> (+ = to the right).</summary>
        public float BodyYaw;
        /// <summary>Degrees the head turns on top of <see cref="Yaw"/> (+ = to the right).</summary>
        public float HeadYaw;
        /// <summary>The feet's velocity (m/s, game space) while walking, and 0..1 how much he is walking.</summary>
        public Vector3 Velocity;
        public float Walk;

        /// <summary>
        /// 왼손 (Angler.LeftHanded): the posture mirrored — the rod in the RIGHT fist, the LEFT hand cranking (the caller
        /// passes the grips that way round), the upper-body turn, the cranking shoulder's roll (Shoulder.L), the elbow
        /// poles, the stance and the figure's 10 degree yaw all mirrored. The model itself is not mirrored (the hat, the
        /// vest, the creel and its strap stay as they are).
        /// </summary>
        public bool Mirror;
        /// <summary>가운데 (Angler.RodCentre): the reeling hold with the rod in front of the belly: the figure square to the front.</summary>
        public bool Centre;
        /// <summary>The figure's yaw on top of which the body and head turn (degrees, + = right): <see cref="Yaw"/>, mirrored left-handed, 0 held in the middle.</summary>
        public float FigureYaw => Centre ? 0f : Mirror ? -Yaw : Yaw;
        /// <summary>The next <see cref="Pose"/> takes the posture at once (the hold changed: no blend from the other side).</summary>
        public void Snap() => started = false;

        struct Foot
        {
            public Vector3 at, from, pos;   // planted ankle, where the step started, the ankle this frame
            public float t;                 // 0..1 through the step
            public bool swing;
        }
        Foot stepL, stepR;
        bool planted;
        /// <summary>Steps taken so far (for measuring the walking cadence).</summary>
        public int StepCount { get; private set; }

        public readonly GameObject Go;
        readonly Transform model, hips, spine, chest, neck, head, shoulderL, shoulderR, upperL, upperR, foreL, foreR, gripL, gripR,
            thighL, thighR, shinL, shinR, footL, footR;
        readonly Transform[] bones;
        readonly Vector3[] restPos;
        readonly Quaternion[] restRot;
        readonly Quaternion headRest, neckRest, footLRest, footRRest;
        readonly float armL1, armL2, armR1, armR2, legL1, legL2, legR1, legR2;
        Posture cur;
        bool started;

        /// <summary>Where the grips were aimed this frame (after sliding along the camera ray), for debugging.</summary>
        public Vector3 LeftAim { get; private set; }
        public Vector3 RightAim { get; private set; }
        /// <summary>Where the fists (HandGrip.L / .R) are after posing.</summary>
        public Vector3 LeftGripPos => gripL.position;
        public Vector3 RightGripPos => gripR.position;
        /// <summary>Interior angle (degrees) of the right elbow after posing: 180 = a straight arm.</summary>
        public float RightElbowDeg => Vector3.Angle(upperR.position - foreR.position, gripR.position - foreR.position);
        /// <summary>Where the right elbow (ForeArm.R) is after posing.</summary>
        public Vector3 RightElbowPos => foreR.position;
        /// <summary>The head bone (Head, the hat sits on it) after posing.</summary>
        public Vector3 HeadPos => head.position;
        /// <summary>The ankles (Foot.L / .R) after posing.</summary>
        public Vector3 LeftFootPos => footL.position;
        public Vector3 RightFootPos => footR.position;

        struct Posture
        {
            public float lean, crouch, turn;       // deg (+ = top towards the camera), m, deg (+ = upper body towards the left)
            public Vector3 rsh;                    // right shoulder roll, character space (x right, y up, z forward)
            public Vector3 footL, footR;           // ankles, character space
            public Vector3 poleL, poleR;           // elbow pole directions, character space

            public static Posture Lerp(Posture a, Posture b, float t) => new Posture
            {
                lean = Mathf.Lerp(a.lean, b.lean, t),
                crouch = Mathf.Lerp(a.crouch, b.crouch, t),
                turn = Mathf.Lerp(a.turn, b.turn, t),
                rsh = Vector3.Lerp(a.rsh, b.rsh, t),
                footL = Vector3.Lerp(a.footL, b.footL, t),
                footR = Vector3.Lerp(a.footR, b.footR, t),
                poleL = Vector3.Lerp(a.poleL, b.poleL, t),
                poleR = Vector3.Lerp(a.poleR, b.poleR, t),
            };
        }

        // hyb_character.POSES converted to Unity character space (x right, y up, z forward; char frame (x, y, z) ->
        // (-y, z, x)): left elbow "tuck" back along the side, right elbow "cross" in front of the chest, or both out
        static readonly Vector3 Tuck = new Vector3(-0.5f, -1f, -0.6f), Cross = new Vector3(0.4f, -0.8f, 1f);
        static readonly Vector3 OutL = new Vector3(-1f, -0.45f, -0.35f), OutR = new Vector3(1f, -0.45f, -0.35f);

        // The reeling hold (idle / reel / fight: the right fist on the crank knob, the left fist on the rod grip out at the
        // left hip, Angler.HoldIdle / HoldReel / HoldFight): the upper body turns 15 degrees towards the reel (turn 25
        // minus the 10 degree figure yaw) and the right shoulder rolls forward / in, so the right arm reaches the knob
        // with the elbow clearly bent (~90-120 degrees round the crank turn), pointing down and out; the left elbow
        // tucks back so the left arm stays under the reel. Static so the -fkarmtune test switch (ActorStrip) can try values.
        internal static float HoldTurn = 25f;
        internal static Vector3 HoldRsh = new Vector3(-0.11f, -0.03f, 0.13f), HoldPoleR = new Vector3(0.3f, -1f, 0.2f), HoldPoleL = Tuck;

        static Posture For(string pose)
        {
            switch (pose)
            {
                case "aim":
                    return new Posture { lean = 8f, rsh = Vector3.zero, footR = new Vector3(0.11f, 0.1f, 0.08f), footL = new Vector3(-0.11f, 0.1f, -0.1f), poleL = OutL, poleR = OutR };
                case "cast":
                    return new Posture { lean = -10f, turn = 4f, rsh = new Vector3(-0.04f, 0f, 0.07f), footR = new Vector3(0.11f, 0.1f, 0.16f), footL = new Vector3(-0.1f, 0.1f, -0.06f), poleL = OutL, poleR = Cross };
                case "reel":
                case "reel2":
                    return new Posture { lean = 3f, turn = HoldTurn, rsh = HoldRsh, footR = new Vector3(0.11f, 0.1f, 0.1f), footL = new Vector3(-0.11f, 0.1f, -0.03f), poleL = HoldPoleL, poleR = HoldPoleR };
                case "fight":
                    return new Posture { lean = 18f, crouch = 0.07f, turn = HoldTurn, rsh = HoldRsh, footR = new Vector3(0.16f, 0.1f, 0.14f), footL = new Vector3(-0.16f, 0.1f, -0.1f), poleL = HoldPoleL, poleR = HoldPoleR };
                case "cheer":
                    return new Posture { lean = 0f, rsh = Vector3.zero, footR = new Vector3(0.12f, 0.1f, 0.06f), footL = new Vector3(-0.12f, 0.1f, 0f), poleL = OutL, poleR = OutR };
                default: // idle
                    return new Posture { lean = 0f, turn = HoldTurn, rsh = HoldRsh, footR = new Vector3(0.1f, 0.1f, 0.1f), footL = new Vector3(-0.1f, 0.1f, -0.04f), poleL = HoldPoleL, poleR = HoldPoleR };
            }
        }

        // The middle hold (Angler.RodCentre: the rod fist on the grip in front of the belly, the reel under it): the upper
        // body square to the front (no turn towards a reel at the hip), the cranking shoulder rolled a little forward, the
        // rod arm's elbow out to its side and down, the cranking elbow out and down to its side. Static for -fkarmtune.
        internal static float CentreTurn = 0f;
        internal static Vector3 CentreRsh = new Vector3(-0.03f, -0.02f, 0.08f), CentrePoleR = new Vector3(0.7f, -1f, -0.1f), CentrePoleL = new Vector3(-0.8f, -1f, -0.25f);

        /// <summary>The posture for a pose with the current hold: the middle hold's arms, mirrored left-handed.</summary>
        Posture Want(string pose)
        {
            var p = For(pose);
            if (Centre && (pose == "idle" || pose == "reel" || pose == "reel2" || pose == "fight"))
            {
                p.turn = CentreTurn;
                p.rsh = CentreRsh;
                p.poleL = CentrePoleL;
                p.poleR = CentrePoleR;
            }
            if (!Mirror) return p;
            static Vector3 X(Vector3 v) => new Vector3(-v.x, v.y, v.z);
            return new Posture
            {
                lean = p.lean, crouch = p.crouch, turn = -p.turn,
                rsh = X(p.rsh),                        // (the cranking shoulder: Shoulder.L left-handed)
                footL = X(p.footR), footR = X(p.footL),
                poleL = X(p.poleR), poleR = X(p.poleL),
            };
        }

        /// <summary>The cheering right fist (character space), the only pose whose right hand is not on the rod or reel.</summary>
        public static readonly Vector3 CheerFist = new Vector3(0.34f, 1.7f, 0.03f);

        Angler3D(GameObject go)
        {
            Go = go;
            model = go.transform;
            Transform F(string n)
            {
                var t = ActorArt.Find(model, n);
                if (t == null) throw new MissingReferenceException("angler bone " + n);
                return t;
            }
            hips = F("Hips");
            spine = F("Spine");
            chest = F("Chest");
            neck = F("Neck");
            head = F("Head");
            shoulderL = F("Shoulder.L");
            shoulderR = F("Shoulder.R");
            upperL = F("UpperArm.L");
            upperR = F("UpperArm.R");
            foreL = F("ForeArm.L");
            foreR = F("ForeArm.R");
            gripL = F("HandGrip.L");
            gripR = F("HandGrip.R");
            thighL = F("Thigh.L");
            thighR = F("Thigh.R");
            shinL = F("Shin.L");
            shinR = F("Shin.R");
            footL = F("Foot.L");
            footR = F("Foot.R");
            bones = model.GetComponentsInChildren<Transform>(true);
            restPos = new Vector3[bones.Length];
            restRot = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                restPos[i] = bones[i].localPosition;
                restRot[i] = bones[i].localRotation;
            }
            // rest measurements with the model at the origin, unrotated
            model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            headRest = head.rotation;
            neckRest = neck.rotation;
            footLRest = footL.rotation;
            footRRest = footR.rotation;
            armL1 = Vector3.Distance(upperL.position, foreL.position);
            armL2 = Vector3.Distance(foreL.position, gripL.position);
            armR1 = Vector3.Distance(upperR.position, foreR.position);
            armR2 = Vector3.Distance(foreR.position, gripR.position);
            legL1 = Vector3.Distance(thighL.position, shinL.position);
            legL2 = Vector3.Distance(shinL.position, footL.position);
            legR1 = Vector3.Distance(thighR.position, shinR.position);
            legR2 = Vector3.Distance(shinR.position, footR.position);
            Debug.Log($"[Angler3D] rest: UpperArm.L {V(upperL.position)} HandGrip.L {V(gripL.position)} HandGrip.R {V(gripR.position)} " +
                      $"Head {V(head.position)} Foot.L {V(footL.position)} arm {armL1:0.000}+{armL2:0.000} leg {legL1:0.000}+{legL2:0.000} bones {bones.Length}");
        }

        static string V(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";

        /// <summary>The angler model on the given layer, or null when the model is missing (the sprites stay).</summary>
        public static Angler3D TryCreate(int layer, Transform parent)
        {
            var prefab = ActorArt.Model("angler");
            if (prefab == null) return null;
            var go = ActorArt.Spawn(prefab, "angler_palette", layer, parent);
            try
            {
                return new Angler3D(go);
            }
            catch (MissingReferenceException e)
            {
                Debug.LogWarning("[Angler3D] " + e.Message);
                Object.Destroy(go);
                return null;
            }
        }

        /// <summary>
        /// Poses the figure for this frame.
        /// </summary>
        /// <param name="feet">feet (the model origin) in game space</param>
        /// <param name="pose">pose name (idle, aim, cast, reel, fight, cheer)</param>
        /// <param name="leftGrip">where the left fist holds the rod</param>
        /// <param name="rightGrip">where the right fist goes (the reel knob, the rod behind the left hand)</param>
        /// <param name="cam">the stage camera position (out-of-reach targets slide along its rays)</param>
        public void Pose(Vector3 feet, string pose, float dt, Vector3 leftGrip, Vector3 rightGrip, Vector3 cam)
        {
            var want = Want(pose);
            cur = started ? Posture.Lerp(cur, want, 1f - Mathf.Exp(-Mathf.Max(0f, dt) / Tau)) : want;
            started = true;

            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = restPos[i];
                bones[i].localRotation = restRot[i];
            }
            // the feet pivot a share of the body turn, the hips, spine and chest add theirs (see BodyYaw)
            var yaw = Quaternion.Euler(0f, FigureYaw + BodyYaw * FeetShare, 0f);
            model.SetPositionAndRotation(feet, yaw);
            var body = Quaternion.Euler(0f, FigureYaw + BodyYaw, 0f);
            var right = body * Vector3.right;

            // torso: crouch, lean back shared by the pelvis and the spine, the upper body turned towards the reel
            hips.position += Vector3.down * (cur.crouch + WalkCrouch * Mathf.Clamp01(Walk));
            hips.rotation = Quaternion.AngleAxis(-cur.lean * 0.4f, right) * Quaternion.AngleAxis(-cur.turn * 0.2f + BodyYaw * HipShare, Vector3.up) * hips.rotation;
            spine.rotation = Quaternion.AngleAxis(-cur.lean * 0.6f, right) * Quaternion.AngleAxis(-cur.turn * 0.4f + BodyYaw * SpineShare, Vector3.up) * spine.rotation;
            chest.rotation = Quaternion.AngleAxis(-cur.turn * 0.4f + BodyYaw * ChestShare, Vector3.up) * chest.rotation;
            // head looks ahead, or towards where he fishes
            var look = Quaternion.Euler(0f, FigureYaw + HeadYaw, 0f);
            neck.rotation = Quaternion.Slerp(neck.rotation, look * neckRest, 0.5f);
            head.rotation = look * headRest;
            // the right shoulder rolls forward / in to reach across to the reel
            // (left-handed the left shoulder: the cranking one)
            if (cur.rsh.sqrMagnitude > 1e-6f)
            {
                if (Mirror) Aim(shoulderL, upperL.position, upperL.position + body * cur.rsh);
                else Aim(shoulderR, upperR.position, upperR.position + body * cur.rsh);
            }

            // legs: feet planted (stepping while he walks), knees forward
            var knee = yaw * new Vector3(0f, 0.25f, 1f);
            var widen = Vector3.right * (Spread * Mathf.Clamp01(Walk));
            Steps(feet + yaw * cur.footL - widen, feet + yaw * cur.footR + widen, dt);
            TwoBone(thighL, shinL, footL, stepL.pos, knee, legL1, legL2);
            TwoBone(thighR, shinR, footR, stepR.pos, knee, legR1, legR2);
            footL.rotation = yaw * footLRest;
            footR.rotation = yaw * footRRest;

            // arms
            LeftAim = Reachable(upperL.position, leftGrip, (armL1 + armL2) * ReachUse, cam);
            TwoBone(upperL, foreL, gripL, LeftAim, body * cur.poleL, armL1, armL2);
            RightAim = Reachable(upperR.position, rightGrip, (armR1 + armR2) * ReachUse, cam);
            TwoBone(upperR, foreR, gripR, RightAim, body * cur.poleR, armR1, armR2);
        }

        /// <summary>
        /// Procedural stepping (the body glides at a steady height, a little lower while walking, only the legs move):
        /// each foot stays planted until it is <see cref="StepAt"/> from its place under the body, then the farther one
        /// steps there (landing half a step ahead of where the body will be by then, <see cref="Lead"/>), lifting, while
        /// the other waits. Walking sideways this becomes a calm shuffle (~2.7 steps/s at 0.8 m/s), the leading foot
        /// stepping out and the trailing one closing in; stopping ends with a step or two into the stance. Standing
        /// still, small stance changes (a new pose, turning) slide.
        /// </summary>
        void Steps(Vector3 homeL, Vector3 homeR, float dt)
        {
            // walking, a foot's place is half a step ahead of where it stands under the body (0 standing still)
            var lead = Velocity * (StepTime * Lead);
            homeL += lead;
            homeR += lead;
            if (!planted || Flat(stepL.at - homeL) > 1f || Flat(stepR.at - homeR) > 1f)
            {
                stepL = new Foot { at = homeL, pos = homeL };
                stepR = new Foot { at = homeR, pos = homeR };
                planted = true;
                return;
            }
            float eL = Flat(stepL.at - homeL), eR = Flat(stepR.at - homeR);
            if (!stepL.swing && !stepR.swing)
            {
                if (eL >= eR && eL > StepAt) { Begin(ref stepL); StepCount++; }
                else if (eR > StepAt) { Begin(ref stepR); StepCount++; }
            }
            bool still = Velocity.sqrMagnitude < 0.0025f;
            Advance(ref stepL, homeL, still, dt);
            Advance(ref stepR, homeR, still, dt);
        }

        static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        static void Begin(ref Foot f)
        {
            f.swing = true;
            f.t = 0f;
            f.from = f.at;
        }

        void Advance(ref Foot f, Vector3 home, bool still, float dt)
        {
            if (!f.swing)
            {
                if (still) f.at = Vector3.MoveTowards(f.at, home, SlideSpeed * dt);
                f.pos = f.at;
                return;
            }
            f.t = Mathf.Min(1f, f.t + dt / StepTime);
            var to = home + Velocity * (StepTime * (1f - f.t)); // where its place will be when it lands
            f.pos = Vector3.Lerp(f.from, to, Mathf.SmoothStep(0f, 1f, f.t)) + Vector3.up * (Mathf.Sin(f.t * Mathf.PI) * Lift);
            if (f.t >= 1f)
            {
                f.swing = false;
                f.at = to;
                f.pos = to;
            }
        }

        /// <summary>
        /// The target itself when the shoulder reaches it, otherwise the point of the camera ray through it that is
        /// nearest to it within reach (it lands on the same screen pixel).
        /// </summary>
        static Vector3 Reachable(Vector3 shoulder, Vector3 target, float reach, Vector3 cam)
        {
            if ((target - shoulder).sqrMagnitude <= reach * reach) return target;
            var d = target - cam;
            var m = cam - shoulder;
            float a = Vector3.Dot(d, d), b = 2f * Vector3.Dot(m, d), c = Vector3.Dot(m, m) - reach * reach;
            float disc = b * b - 4f * a * c;
            if (a < 1e-8f) return target;
            if (disc < 0f) return cam + d * Mathf.Clamp(-b / (2f * a), 0f, 1f);   // closest approach
            float s = Mathf.Sqrt(disc);
            float t1 = (-b - s) / (2f * a), t2 = (-b + s) / (2f * a);
            float t = 1f > t2 ? t2 : (1f < t1 ? t1 : 1f);
            return cam + d * t;
        }

        /// <summary>Analytic two-bone IK: upper / lower bones, the end transform lands on target, the middle joint bends towards pole.</summary>
        static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float l1, float l2)
        {
            var s = upper.position;
            var dv = target - s;
            float dist = Mathf.Clamp(dv.magnitude, Mathf.Abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            var u = dv.sqrMagnitude > 1e-10f ? dv.normalized : Vector3.down;
            var v = pole - u * Vector3.Dot(pole, u);
            if (v.sqrMagnitude < 1e-8f) v = Vector3.Cross(u, Vector3.right);
            v.Normalize();
            float ca = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            float sa = Mathf.Sqrt(Mathf.Max(0f, 1f - ca * ca));
            var elbow = s + (u * ca + v * sa) * l1;
            var endPos = s + u * dist;
            Aim(upper, lower.position, elbow);
            Aim(lower, end.position, endPos);
        }

        /// <summary>Rotates bone about its own pivot so that a point it carries (childPos) points at want.</summary>
        static void Aim(Transform bone, Vector3 childPos, Vector3 want)
        {
            var from = childPos - bone.position;
            var to = want - bone.position;
            if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
        }

        public void SetVisible(bool on)
        {
            if (Go.activeSelf != on) Go.SetActive(on);
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
        }
    }
}
