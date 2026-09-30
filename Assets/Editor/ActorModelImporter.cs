using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishingKing.EditorTools
{
    /// <summary>
    /// Import settings for the real-time 3D actors under Resources/Models (Tools/Blender/variants/hybrid/hyb_actors3d.py,
    /// contract in Tools/Blender/_tmp/actors3d/actors3d_notes.md): metres at scale 1, no axis baking, no rig / animation /
    /// cameras / lights, the bone hierarchy kept as plain transforms. The FBX materials stay embedded only for their
    /// names (the game swaps them for toon materials built from the palette json, see ActorArt); they get the toon
    /// shader so nothing drags the Standard shader into the build. Every mesh gets its position-averaged normals in
    /// UV3 for the outline hull (the parts are flat shaded, a hull pushed along split face normals would crack).
    /// </summary>
    public class ActorModelImporter : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Models/";
        const string ToonShader = "Assets/Shaders/ActorToon.shader";

        public override uint GetVersion() => 1;

        bool Ours => assetPath.Replace('\\', '/').StartsWith(Root);

        void OnPreprocessModel()
        {
            if (!Ours) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.optimizeGameObjects = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!Ours) return;
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(ToonShader);
            if (sh == null) return;
            material.shader = sh;
            if (description.TryGetProperty("DiffuseColor", out Vector4 c))
                material.SetColor("_Mid", new Color(c.x, c.y, c.z, 1f));
        }

        void OnPostprocessModel(GameObject g)
        {
            if (!Ours) return;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var v = m.vertices;
                var n = m.normals;
                if (n == null || n.Length != v.Length) continue;
                var sum = new Dictionary<Vector3Int, Vector3>();
                for (int i = 0; i < v.Length; i++)
                {
                    var k = Key(v[i]);
                    sum.TryGetValue(k, out var s);
                    sum[k] = s + n[i];
                }
                var sm = new List<Vector3>(v.Length);
                for (int i = 0; i < v.Length; i++)
                {
                    var s = sum[Key(v[i])];
                    sm.Add(s.sqrMagnitude > 1e-10f ? s.normalized : n[i]);
                }
                m.SetUVs(3, sm);
            }
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        static Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 20000f), Mathf.RoundToInt(p.y * 20000f), Mathf.RoundToInt(p.z * 20000f));
    }
}
