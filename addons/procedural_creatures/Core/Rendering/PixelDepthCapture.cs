// Experimental multi-view reconstruction data; captured from the original 2D renderer, not OBJ geometry.
using System;
using PixelCreatures.Core;

namespace PixelCreatures.Core.Rendering
{
    /// <summary>A copy of one post-cleanup G-buffer plus the exact 2D palette-index image.</summary>
    public sealed class PixelDepthCapture
    {
        public int Width { get; }
        public int Height { get; }
        public int OriginX { get; }
        public int OriginY { get; }
        public ViewSpec View { get; }
        public bool[] Visible { get; }
        public float[] Depth { get; }
        public float[] NormalX { get; }
        public float[] NormalY { get; }
        public float[] NormalZ { get; }
        public short[] Primitive { get; }
        public byte[] Group { get; }
        public byte[] PaletteIndex { get; }

        public PixelDepthCapture(PixelFrame image, in ViewSpec view, byte[] flags, float[] depth,
            float[] nx, float[] ny, float[] nz, short[] primitives, byte[] groups, byte coveredBit)
        {
            Width = image.Width; Height = image.Height;
            OriginX = image.OriginX; OriginY = image.OriginY;
            View = view;
            int n = checked(Width * Height);
            Visible = new bool[n];
            Depth = new float[n];
            NormalX = new float[n]; NormalY = new float[n]; NormalZ = new float[n];
            Primitive = new short[n]; Group = new byte[n]; PaletteIndex = new byte[n];
            for (int i = 0; i < n; i++)
            {
                Visible[i] = (flags[i] & coveredBit) != 0 && !float.IsNaN(depth[i]) && !float.IsInfinity(depth[i]);
                Depth[i] = depth[i];
                NormalX[i] = nx[i]; NormalY[i] = ny[i]; NormalZ[i] = nz[i];
                Primitive[i] = primitives[i]; Group[i] = groups[i];
                PaletteIndex[i] = image.Indices[i];
            }
        }

        /// <summary>Exact reverse projection for a visible pixel centre in creature coordinates.</summary>
        public Vec3 BackProject(int x, int y)
        {
            int i = y * Width + x;
            if (!Visible[i]) throw new ArgumentException("Pixel is not part of the creature.");
            Vec3 inView = new Vec3(x + 0.5 - OriginX, OriginY - (y + 0.5), Depth[i]);
            Mat3 toView = PixelCamera.WorldToView(View.Elevation) * PixelCamera.YawRotation(View.Yaw);
            Vec3 offset = PixelCamera.WorldToView(View.Elevation).Mul(View.RootOffset);
            return toView.Transposed.Mul(inView - offset);
        }

        /// <summary>Project a world-space creature point to nearest sampled 2D pixel and view depth.</summary>
        public bool Project(Vec3 point, out int x, out int y, out double z)
        {
            Vec3 view = PixelCamera.WorldToView(View.Elevation).Mul(
                PixelCamera.YawRotation(View.Yaw).Mul(point) + View.RootOffset);
            x = (int)Math.Floor(OriginX + view.X);
            y = (int)Math.Floor(OriginY - view.Y);
            z = view.Z;
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }
    }
}
