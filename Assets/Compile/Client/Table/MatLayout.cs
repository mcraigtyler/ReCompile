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
        static readonly float[] LaneCentersPx = { 211f, 688f, 1186.5f };
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

        // The local player's lanes, left to right (mat pixels). The opponent's lanes are these rotated 180°.
        static readonly Vector2[] LaneXRangePx = { new Vector2(30f, 392f), new Vector2(508f, 868f), new Vector2(1005f, 1368f) };
        public const float LaneTopPx = 2370f;

        // The center line between the two Protocol rows (mat pixels).
        public const float CenterLinePx = 1888f;

        public static float LaneCenterPx(int line) => LaneCentersPx[line];

        // Rectangle of a local lane in canvas pixels.
        public static Rect LocalLaneCanvasRect(int line)
        {
            var min = ToCanvas(LaneXRangePx[line].x, SizePx.y);
            var max = ToCanvas(LaneXRangePx[line].y, LaneTopPx);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // Mirrors a mat pixel position into the opponent's half (the mat is symmetric under a 180° turn).
        public static Vector2 MirrorPx(float x, float y) => new Vector2(SizePx.x - x, SizePx.y - y);

        // Protocol slot center in canvas pixels. Index is from that player's own left (0 = left, 2 = right).
        public static Vector2 ProtocolSlot(bool local, int index) =>
            local
                ? ToCanvas(LaneCentersPx[index], LocalProtocolRowPx)
                : ToCanvas(SizePx.x - LaneCentersPx[index], OpponentProtocolRowPx);

        // Protocol card art is portrait with sideways text. Turned this way it reads left-to-right for its owner.
        public static float ProtocolRotation(bool local) => local ? -90f : 90f;

        // Table-unit rectangle covering both rows of Protocol slots.
        public static Rect ProtocolRows => Rect.MinMaxRect(-SizeUnits.x / 2f, ToTable(0f, 2290f).y, SizeUnits.x / 2f, ToTable(0f, 1500f).y);
    }
}
