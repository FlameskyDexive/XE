// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

public static class ShadowAtlas
{
    private static int size;
    private static RenderTexture? atlas;

    /// <summary>
    /// Preferred atlas edge length. 0 = auto (8192 if supported, else 4096).
    /// Editors should prefer 2048/4096 to cut idle VRAM and clear bandwidth.
    /// Applied on next <see cref="TryInitialize"/> when no atlas exists yet.
    /// </summary>
    public static int PreferredSize { get; set; }

    // Simple Guillotine algorithm - maintains a list of free rectangles
    private class FreeRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public FreeRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    private static List<FreeRect> freeRects = [];

    /// <summary>True when the GPU atlas texture has been created.</summary>
    public static bool IsInitialized => atlas.IsValid();

    public static void TryInitialize()
    {
        if (atlas.IsValid()) return;

        size = ResolveSize();

        atlas ??= new RenderTexture(size, size, true, []);

        // Sample the atlas through hardware depth comparison: a sampler2DShadow in the lighting
        // shaders then gets fixed-function 2x2 PCF (with LINEAR filtering) instead of the manual
        // per-tap compare. Set once on the depth attachment after creation.
        atlas.InternalDepth.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
        atlas.InternalDepth.SetDepthCompareMode(true);

        // Initialize with one large free rectangle covering the entire atlas
        freeRects.Clear();
        freeRects.Add(new FreeRect(0, 0, size, size));
    }

    private static int ResolveSize()
    {
        int max = Graphics.MaxTextureSize > 0 ? Graphics.MaxTextureSize : 4096;
        int preferred = PreferredSize;
        if (preferred > 0)
        {
            // Clamp to power-of-two-ish bounds used by lights; never exceed device max.
            preferred = Maths.Clamp(preferred, 512, max);
            return preferred;
        }

        bool supports8k = max >= 8192;
        return supports8k ? 8192 : 4096;
    }

    public static int GetMinShadowSize() => 32; // Minimum shadow resolution
    public static int GetSize() => size;
    public static RenderTexture? GetAtlas() => atlas;

    public static Int2? ReserveTiles(int width, int height, int lightID)
    {
        // Callers only reach here when shadows are needed; create atlas lazily.
        TryInitialize();

        // Clamp to min/max bounds
        width = Maths.Max(width, 32);
        height = Maths.Max(height, 32);

        if (width > size || height > size)
            return null;

        // Find the best free rectangle using Best-Short-Side-Fit (BSSF) heuristic
        int bestIndex = -1;
        int bestShortSideFit = int.MaxValue;
        int bestLongSideFit = int.MaxValue;

        for (int i = 0; i < freeRects.Count; i++)
        {
            FreeRect rect = freeRects[i];

            // Check if the rectangle fits
            if (rect.Width >= width && rect.Height >= height)
            {
                int leftoverX = rect.Width - width;
                int leftoverY = rect.Height - height;
                int shortSideFit = Maths.Min(leftoverX, leftoverY);
                int longSideFit = Maths.Max(leftoverX, leftoverY);

                if (shortSideFit < bestShortSideFit ||
                    (shortSideFit == bestShortSideFit && longSideFit < bestLongSideFit))
                {
                    bestIndex = i;
                    bestShortSideFit = shortSideFit;
                    bestLongSideFit = longSideFit;
                }
            }
        }

        if (bestIndex == -1)
            return null; // No suitable space found

        // Place the rectangle in the best location
        FreeRect chosen = freeRects[bestIndex];
        int placedX = chosen.X;
        int placedY = chosen.Y;

        // Split the chosen rectangle using the Guillotine method
        // We split along the shorter leftover axis for better space utilization
        List<FreeRect> newRects = new List<FreeRect>();

        int leftoverWidth = chosen.Width - width;
        int leftoverHeight = chosen.Height - height;

        if (leftoverWidth > 0 && leftoverHeight > 0)
        {
            // Both dimensions have leftover space - split into two rectangles
            if (leftoverWidth <= leftoverHeight)
            {
                // Horizontal split
                newRects.Add(new FreeRect(chosen.X + width, chosen.Y, leftoverWidth, chosen.Height));
                newRects.Add(new FreeRect(chosen.X, chosen.Y + height, width, leftoverHeight));
            }
            else
            {
                // Vertical split
                newRects.Add(new FreeRect(chosen.X, chosen.Y + height, chosen.Width, leftoverHeight));
                newRects.Add(new FreeRect(chosen.X + width, chosen.Y, leftoverWidth, height));
            }
        }
        else if (leftoverWidth > 0)
        {
            // Only horizontal space left
            newRects.Add(new FreeRect(chosen.X + width, chosen.Y, leftoverWidth, chosen.Height));
        }
        else if (leftoverHeight > 0)
        {
            // Only vertical space left
            newRects.Add(new FreeRect(chosen.X, chosen.Y + height, chosen.Width, leftoverHeight));
        }

        // Remove the used rectangle and add the new free rectangles
        freeRects.RemoveAt(bestIndex);
        freeRects.AddRange(newRects);

        // TODO: Merge adjacent rectangles to reduce fragmentation

        return new Int2(placedX, placedY);
    }

    /// <summary>
    /// Reset packing free-rects only. No-op if the atlas has not been created yet
    /// (empty scenes never pay for 4K/8K allocation).
    /// </summary>
    public static void Clear()
    {
        if (!atlas.IsValid())
            return;

        // Reset to initial state with one large free rectangle
        freeRects.Clear();
        freeRects.Add(new FreeRect(0, 0, size, size));
    }
}
