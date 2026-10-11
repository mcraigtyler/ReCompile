using UnityEngine;

namespace Compile.Client.Table
{
    // Where things are on the play mat, measured on resources/img/PlayMat.jpg (1398×3788) in pixels from
    // its top-left. The mat lies at the table origin at 100 pixels per unit, with the local player's half
    // toward -Y. (The game's PlayMat.png is the same image centered in a transparent 2048×4096 texture, so
    // it can be GPU-compressed with mipmaps; centering keeps these measurements valid.)
    public static class MatLayout
    {
        public const float PixelsPerUnit = 100f;
        public static readonly Vector2 SizePx = new Vector2(1398f, 3788f);

        // Lane centers for the local player's left, middle, and right lanes. Protocol slots line up with them.
        static readonly float[] LaneCenterPx = { 211f, 688f, 1186.5f };
        const float LocalProtocolRowPx = 2092f;
        const float OpponentProtocolRowPx = 1695f;

        public const float ProtocolScale = 0.9f;
        public static readonly Vector2 ProtocolSlotSizePx = new Vector2(480f, 360f);

        // The local player's three lanes, below the "START ... END" banner.
        public static readonly Rect LocalLanesPx = Rect.MinMaxRect(30f, 2370f, 1368f, 3788f);

        // Where menu panels rest: on the desk just past the near edge of the mat.
        public static readonly Vector2 MenuSpot = new Vector2(0f, -25.5f);

        public static Vector2 SizeUnits => SizePx / PixelsPerUnit;

        // Mat pixel (origin top-left, +y down) to canvas-local pixels on a mat-sized canvas (origin at center, +y up).
        public static Vector2 ToCanvas(float x, float y) => new Vector2(x - SizePx.x / 2f, SizePx.y / 2f - y);

        // Mat pixel to table units.
        public static Vector2 ToTable(float x, float y) => ToCanvas(x, y) / PixelsPerUnit;

        // Protocol slot center in canvas pixels. Index is from that player's own left (0 = left, 2 = right).
        public static Vector2 ProtocolSlot(bool local, int index) =>
            local
                ? ToCanvas(LaneCenterPx[index], LocalProtocolRowPx)
                : ToCanvas(SizePx.x - LaneCenterPx[index], OpponentProtocolRowPx);

        // Protocol card art is portrait with sideways text. Turned this way it reads left-to-right for its owner.
        public static float ProtocolRotation(bool local) => local ? -90f : 90f;

        // Table-unit rectangle covering both rows of Protocol slots.
        public static Rect ProtocolRows => Rect.MinMaxRect(-SizeUnits.x / 2f, ToTable(0f, 2290f).y, SizeUnits.x / 2f, ToTable(0f, 1500f).y);
    }
}
