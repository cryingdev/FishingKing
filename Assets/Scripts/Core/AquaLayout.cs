using System;
using System.Linq;
using UnityEngine;

namespace FishingKing
{
    /// <summary>A decoration slot's place in one tank's art (aquarium_tank_&lt;n&gt;.json "slots").</summary>
    [Serializable]
    public class AquaSlotPos
    {
        public string id, kind;
        public int col, row, level, order, maxW, maxH;
        public float x, y;
    }

    /// <summary>The ledge's item columns (canvas px) of one tank.</summary>
    [Serializable]
    public class AquaLedgeCols
    {
        public int tub, cooler, sponge, net, siphon;
    }

    /// <summary>
    /// One tank level's art and layout (Tools/Blender/variants/hybrid/hyb_aquatanks.py → Resources/Data/aquarium_tank_&lt;n&gt;.json,
    /// Sprites/Stages/aquarium_&lt;n&gt;_back/front.png): the sprite is centred at world (0, 0), 16 px per unit
    /// (col = W/2 + 16 x, row = H/2 - 16 y); the view height grows with the tank. It is also an <see cref="AquariumData"/>
    /// (the swim bounds, the surface), so the older code that takes one reads it unchanged. The aquarium scene pins the
    /// layout it was built with (<see cref="Scene"/>): a tank bought while it is open takes over on the next visit.
    /// </summary>
    [Serializable]
    public class AquaLayout : AquariumData
    {
        public int level;
        public string key, name, style, back, front;
        public int canvasW, canvasH, ppu, viewH, viewW, viewMaxW, viewMaxH;
        public float viewScale, orthoSize;
        public int[] frameRect, glassRect, waterRect, gravelTopRow, gravelBand, gravelRows, dirtRect, ledgeRect;
        public float glassL, glassR, glassT, glassB;
        public int glassW, glassH;
        public int surfaceRow;
        public float surfaceFx, tankMinX, tankMaxX, gravelTopY, pelletRestY, gravelY;
        public int dirtH;
        public float dirtCentreY;
        public float[] bubble, lightAt, glowAt, bagX;
        public int ledgeRow;
        public float ledgeY, bagCentreY, tubX, coolerX, boxY, spongeX, netX, siphonX;
        public AquaLedgeCols ledgeCols;
        public AquaSlotPos[] slots;
        public int[] viewRect, uiTopBand, uiHint, uiIncome, uiButtons;

        public const int Levels = 5;

        /// <summary>The layout the open aquarium scene was built with (null outside it).</summary>
        public static AquaLayout Scene;

        /// <summary>The layout in use: the open scene's, else the player's tank level's.</summary>
        public static AquaLayout Active => Scene ?? Of(Game.I != null ? Game.Data.tankLevel : 1);

        public static AquaLayout Of(int level) => Art.Data<AquaLayout>("aquarium_tank_" + Mathf.Clamp(level, 0, Levels - 1));

        // ---- derived
        public Vector2 GlassCentre => new Vector2((glassL + glassR) * 0.5f, (glassT + glassB) * 0.5f);
        public Vector2 LightAt => lightAt != null && lightAt.Length >= 2 ? new Vector2(lightAt[0], lightAt[1]) : new Vector2(0f, glassT + 2.25f);
        public Vector2 GlowAt => glowAt != null && glowAt.Length >= 2 ? new Vector2(glowAt[0], glowAt[1]) : GlassCentre;
        public Vector2 Bubble => bubble != null && bubble.Length >= 2 ? new Vector2(bubble[0], bubble[1]) : new Vector2(-6.5f, gravelTopY + 0.4f);
        public float BottomY => gravelTopY + 0.125f;   // where a pellet comes to rest (legacy -3.5 over -3.625)
        public float ChaseMinY => gravelTopY + 0.275f; // the lowest a chasing fish goes (legacy -3.35)
        /// <summary>World size of the glass (for the algae / murk / glow overlays made for the 436 x 164 legacy glass).</summary>
        public Vector2 GlassScale => new Vector2(glassW / 436f, glassH / 164f);

        public AquaSlotPos SlotPos(string id) => slots?.FirstOrDefault(s => s.id == id);
        public bool HasSlot(string id) => SlotPos(id) != null;

        /// <summary>World row → y (the sprite centred at the origin).</summary>
        public float RowY(float row) => (canvasH * 0.5f - row) / ppu;
        public float ColX(float col) => (col - canvasW * 0.5f) / ppu;

        /// <summary>The gravel's top (world y) at x, from the rendered per-column rows.</summary>
        public float GravelTop(float x)
        {
            if (gravelRows == null || gravelRows.Length == 0 || glassRect == null) return gravelTopY;
            int i = Mathf.Clamp(Mathf.RoundToInt(canvasW * 0.5f + x * ppu) - glassRect[0], 0, gravelRows.Length - 1);
            return RowY(gravelRows[i]);
        }
    }
}
