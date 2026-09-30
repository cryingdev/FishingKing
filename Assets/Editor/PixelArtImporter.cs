using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishingKing.EditorTools
{
    /// <summary>
    /// Import settings for every Blender-rendered sprite under Resources/Sprites:
    /// point filtering, no compression, 16 pixels per unit, 9-slice borders for UI frames
    /// and a feet pivot for the angler.
    /// </summary>
    public class PixelArtImporter : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Sprites/";
        public const int PixelsPerUnit = 16;

        // bump whenever the settings below change, so Unity reimports the affected assets
        public override uint GetVersion() => 5;

        // Must match Tools/Blender/fk_items.py build_frames()
        static readonly Dictionary<string, int> Borders = new Dictionary<string, int>
        {
            { "panel_wood", 14 }, { "panel_dark", 9 }, { "panel_paper", 8 },
            { "btn_green", 9 }, { "btn_blue", 9 }, { "btn_red", 9 }, { "btn_yellow", 9 }, { "btn_grey", 9 },
            { "slot", 7 }, { "bar_bg", 4 }, { "bar_fill", 4 }, { "badge", 5 },
            // legend encounter window frame (Tools/Blender/variants/hybrid/hyb_encounter.py make_frame)
            { "enc_frame_cave", 6 },
            { "enc_frame_lake", 6 }, { "enc_frame_swamp", 6 },
            { "enc_frame_ice", 6 }, { "enc_frame_ocean", 6 },
        };

        /// <summary>
        /// Galmuri pixel fonts: dynamic glyphs rasterised without anti-aliasing so the pixel outlines
        /// stay hard-edged when rendered at whole multiples of their grid size.
        /// </summary>
        void OnPreprocessAsset()
        {
            if (!(assetImporter is TrueTypeFontImporter fi)) return;
            if (!assetPath.Replace('\\', '/').StartsWith("Assets/Resources/Fonts/")) return;
            fi.fontTextureCase = FontTextureCase.Dynamic;
            fi.fontRenderingMode = FontRenderingMode.HintedRaster;
            fi.includeFontData = true;
            fi.characterSpacing = 0;
            fi.characterPadding = 1;
        }

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(Root)) return;

            var ti = (TextureImporter)assetImporter;
            // front-layer depth maps (Tools/Blender/variants/hybrid/hyb_frontdepth.py, Data/frontdepth_<stage>.json):
            // 16-bit camera depth in R/G, coverage in A - raw data, not a sprite: exact bytes (sRGB off, no alpha
            // dilation, no NPOT rescale, no compression, no mipmaps), point-sampled, readable for CPU checks
            if (path.StartsWith(Root + "Stages/") && Path.GetFileNameWithoutExtension(path).EndsWith("_front_depth"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Point;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.isReadable = true;
                return;
            }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            // fish sprites stay readable: the game measures their brightness to keep shadows visible on any water; the
            // aquarium's dirt overlays, their grow maps and the wipe brushes too (AquaClean builds the per-pixel wipe
            // mask from them: Tools/Blender/fk_aquaclean.py)
            string file = Path.GetFileNameWithoutExtension(path);
            ti.isReadable = path.Contains("/Fish/") || file.StartsWith("clean_algae_") || file.StartsWith("clean_dirt_")
                || file.EndsWith("_grow") || file.StartsWith("clean_") && file.EndsWith("_brush");

            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteExtrude = 0;
            s.spriteGenerateFallbackPhysicsShape = false;

            string name = Path.GetFileNameWithoutExtension(path);
            if (path.Contains("/Character/"))
            {
                // Feet sit 10px above the bottom edge of the 96x112 frame (see fk_character.py).
                s.spriteAlignment = (int)SpriteAlignment.Custom;
                s.spritePivot = new Vector2(0.5f, 10f / 112f);
            }
            else
            {
                s.spriteAlignment = (int)SpriteAlignment.Center;
            }

            s.spriteBorder = Borders.TryGetValue(name, out int b) ? new Vector4(b, b, b, b) : Vector4.zero;
            ti.SetTextureSettings(s);
        }
    }
}
