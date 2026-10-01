using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The equipped reel as a real-time 3D model (Resources/Models/reel_&lt;id&gt;) on its own <see cref="ActorLayer"/>.
    /// Model contract (actors3d_notes.md): the origin is the top of the reel foot (the rod attachment point), +Z runs
    /// along the rod to the tip, the body hangs at -Y, the crank is on +X. `Crank` turns about its local X (0 = knob
    /// straight up, + = knob forward over the top = winding in); `Knob` is the knob centre (the right hand's target)
    /// and `Spool` is where the line leaves the reel.
    /// </summary>
    public class Reel3D
    {
        public readonly GameObject Go;
        public readonly string Id;
        readonly Transform crank, knob, spool;
        readonly Quaternion crankRest;

        Reel3D(GameObject go, string id)
        {
            Go = go;
            Id = id;
            crank = ActorArt.Find(go.transform, "Crank");
            knob = ActorArt.Find(go.transform, "Knob");
            spool = ActorArt.Find(go.transform, "Spool");
            crankRest = crank != null ? crank.localRotation : Quaternion.identity;
        }

        /// <summary>The reel model for this id on the given layer, or null when the model is missing.</summary>
        public static Reel3D TryCreate(string id, int layer, Transform parent)
        {
            var prefab = ActorArt.Model(id);
            if (prefab == null) return null;
            var go = ActorArt.Spawn(prefab, "reel_palette", layer, parent);
            var r = new Reel3D(go, id);
            if (r.knob == null || r.spool == null || r.crank == null)
            {
                Debug.LogWarning($"[Reel3D] {id}: missing Crank / Knob / Spool");
                Object.Destroy(go);
                return null;
            }
            return r;
        }

        /// <summary>Knob centre in game space (the cranking hand holds it).</summary>
        public Vector3 Knob => knob.position;

        /// <summary>Where the line leaves the reel, in game space.</summary>
        public Vector3 Spool => spool.position;

        /// <param name="foot">attachment point (top of the reel foot) in game space</param>
        /// <param name="along">rod direction at the seat, towards the tip</param>
        /// <param name="up">the rod's "up": normal to the rod in its vertical plane (the reel hangs on the other side)</param>
        /// <param name="crankDeg">crank angle, 0 = knob up, + = winding in</param>
        /// <param name="scale">display scale of the model</param>
        /// <param name="mirror">left-handed: the model mirrored across its own YZ plane, so the crank is on the reel's other
        /// side (a left-hand-wind reel; Unity flips the faces' winding for the negative scale). Turning about the mirrored X
        /// axis still brings the knob forward over the top: winding in looks the same.</param>
        public void Place(Vector3 foot, Vector3 along, Vector3 up, float crankDeg, float scale, bool mirror = false)
        {
            Go.transform.SetPositionAndRotation(foot, Quaternion.LookRotation(along, up));
            Go.transform.localScale = new Vector3(mirror ? -scale : scale, scale, scale);
            crank.localRotation = Quaternion.AngleAxis(crankDeg, Vector3.right) * crankRest;
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
        }
    }
}
