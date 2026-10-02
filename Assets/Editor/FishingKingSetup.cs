using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishingKing.EditorTools
{
    /// <summary>
    /// Creates the four game scenes (each one only holds a camera and a bootstrap component,
    /// everything else is built at runtime) and configures build / player settings.
    /// Menu: FishingKing/Setup Project. Batch: -executeMethod FishingKing.EditorTools.FishingKingSetup.Batch
    /// </summary>
    public static class FishingKingSetup
    {
        const string SceneDir = "Assets/Scenes";

        [MenuItem("FishingKing/Setup Project (scenes + settings)")]
        public static void SetupAll()
        {
            AssetDatabase.Refresh();
            ConfigurePlayer();
            Directory.CreateDirectory(SceneDir);
            var scenes = new[]
            {
                CreateScene("Title", typeof(TitleScene)),
                CreateScene("Map", typeof(MapScene)),
                CreateScene("Fishing", typeof(FishingScene)),
                CreateScene("Aquarium", typeof(AquariumScene)),
            };
            EditorBuildSettings.scenes = System.Array.ConvertAll(scenes, p => new EditorBuildSettingsScene(p, true));
            EditorSceneManager.OpenScene(scenes[0]);
            AssetDatabase.SaveAssets();
            Debug.Log("[FishingKing] Setup complete.");
        }

        [MenuItem("FishingKing/Reimport Sprites")]
        public static void ReimportSprites()
        {
            AssetDatabase.ImportAsset("Assets/Resources/Sprites", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }

        [MenuItem("FishingKing/Delete Save Data")]
        public static void DeleteSave()
        {
            SaveSystem.Delete();
            Debug.Log("[FishingKing] Save data deleted.");
        }

        public static void Batch()
        {
            ReimportSprites();
            SetupAll();
        }

        [MenuItem("FishingKing/Build Windows")]
        public static void BuildWindows()
        {
            // the species data first (Docs/data_reference.md 2.7): nothing is built from data the validator rejects
            if (!SpeciesValidator.Gate())
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else EditorUtility.DisplayDialog("Build Windows", "The species data has errors (see the Console): nothing was built.", "OK");
                return;
            }
            var scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path);
            // -fkBuildOut <dir>: build somewhere else (e.g. while Builds/Windows is in use); default Builds/Windows
            string outDir = "Builds/Windows";
            var args = System.Environment.GetCommandLineArgs();
            int oi = System.Array.IndexOf(args, "-fkBuildOut");
            if (oi >= 0 && oi + 1 < args.Length && !string.IsNullOrWhiteSpace(args[oi + 1])) outDir = args[oi + 1];
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = System.IO.Path.Combine(outDir, "FishingKing.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[FishingKing] Build {report.summary.result} -> {opts.locationPathName}: {report.summary.totalSize / 1024 / 1024} MB, errors {report.summary.totalErrors}");
        }

        static string CreateScene(string name, System.Type bootstrap)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var c = cam.AddComponent<Camera>();
            c.orthographic = true;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;
            cam.transform.position = new Vector3(0, 0, -10);
            var root = new GameObject(name + "Scene");
            root.AddComponent(bootstrap);
            string path = $"{SceneDir}/{name}.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "FishingKing";
            PlayerSettings.productName = "낚시왕 Fishing King";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            // Active Input Handling = Input System Package (new)
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps != null && ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 1)
                {
                    prop.intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
