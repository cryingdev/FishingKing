using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FishingKing.EditorTools
{
    /// <summary>
    /// The species data validator in the editor (Docs/data_reference.md 2.7): it reads the project's files directly
    /// (SpeciesCheck.FromFiles: the species files, the roster, the sprites, the legend models, the obstacles and the Blender
    /// scripts), so it sees an edit at once. Menu: FishingKing/Validate Species Data. Batch:
    /// <c>Unity.exe -batchmode -nographics -projectPath &lt;project&gt; -executeMethod FishingKing.EditorTools.SpeciesValidator.Batch -logFile &lt;log&gt;</c>
    /// (exit code 1 on errors; with -fkspeciesfixtures also the broken-data fixtures). The Windows build runs it first and
    /// builds nothing on an error.
    /// </summary>
    public static class SpeciesValidator
    {
        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>Runs the validator and logs every finding; returns the error count.</summary>
        public static int Validate()
        {
            var ctx = SpeciesCheck.FromFiles(ProjectRoot);
            var found = SpeciesCheck.Run(ctx, out var r);
            foreach (var f in found)
            {
                if (f.error) Debug.LogError("[SPECIES] " + f);
                else Debug.LogWarning("[SPECIES] " + f);
            }
            int errors = SpeciesCheck.Errors(found);
            Debug.Log("[SPECIES] validate: " + SpeciesCheck.Summary(found, r));
            return errors;
        }

        [MenuItem("FishingKing/Validate Species Data")]
        public static void Menu()
        {
            var ctx = SpeciesCheck.FromFiles(ProjectRoot);
            var found = SpeciesCheck.Run(ctx, out var r);
            foreach (var f in found)
            {
                if (f.error) Debug.LogError("[SPECIES] " + f);
                else Debug.LogWarning("[SPECIES] " + f);
            }
            string head = SpeciesCheck.Summary(found, r);
            Debug.Log("[SPECIES] validate: " + head);
            string body = string.Join("\n", found.Where(f => f.error).Take(12).Select(f => f.ToString()));
            EditorUtility.DisplayDialog("Validate Species Data", head + (body.Length > 0 ? "\n\n" + body + "\n\n(the full list is in the Console)" : ""), "OK");
        }

        /// <summary>The batch entry point: exit 0 when the data passes, 1 on an error (or a failed fixture).</summary>
        public static void Batch()
        {
            int code = 0;
            try
            {
                int errors = Validate();
                if (errors > 0) code = 1;
                if (System.Environment.GetCommandLineArgs().Contains("-fkspeciesfixtures"))
                {
                    int fails = SpeciesFixtures.Run(SpeciesCheck.FromFiles(ProjectRoot), m => Debug.Log(m));
                    Debug.Log($"[SPECIES] fixtures: {fails} failed");
                    if (fails > 0) code = 1;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SPECIES] the validator failed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>The build's gate: true when the data passes (the build goes on).</summary>
        public static bool Gate()
        {
            int errors;
            try
            {
                errors = Validate();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SPECIES] the validator failed: " + e);
                errors = 1;
            }
            if (errors == 0) return true;
            Debug.LogError($"[SPECIES] {errors} errors in the species data: no build (Docs/data_reference.md 2.7)");
            return false;
        }
    }
}
