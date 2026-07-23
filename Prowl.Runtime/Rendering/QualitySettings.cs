// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Global quality knobs for player and editor preview tiers.
/// Editor initializes a lighter default (smaller shadow atlas) via <see cref="ApplyEditorDefaults"/>.
/// </summary>
public static class QualitySettings
{
    private static int s_shadowAtlasSize = 4096;
    private static bool s_shadowsEnabled = true;
    private static QualityTier s_tier = QualityTier.Full;

    public enum QualityTier
    {
        /// <summary>Editor Scene/Game preview: reduced atlas, optional feature skips later.</summary>
        Preview,
        /// <summary>Full player / play-mode quality.</summary>
        Full,
    }

    /// <summary>Current quality tier (Preview vs Full).</summary>
    public static QualityTier Tier
    {
        get => s_tier;
        set
        {
            s_tier = value;
            ApplyTierDefaults(value);
        }
    }

    /// <summary>
    /// Preferred shadow atlas edge length (power-of-two). Applied on next atlas create.
    /// Clamped to [512, 8192].
    /// </summary>
    public static int ShadowAtlasSize
    {
        get => s_shadowAtlasSize;
        set
        {
            int v = value;
            if (v < 512) v = 512;
            if (v > 8192) v = 8192;
            // Snap down to power of two.
            int pot = 512;
            while (pot * 2 <= v) pot *= 2;
            s_shadowAtlasSize = pot;
            ShadowAtlas.PreferredSize = s_shadowAtlasSize;
        }
    }

    /// <summary>When false, pipelines should skip shadow atlas work even if lights cast shadows.</summary>
    public static bool ShadowsEnabled
    {
        get => s_shadowsEnabled;
        set => s_shadowsEnabled = value;
    }

    /// <summary>Editor startup: Preview tier + 2048 atlas (VRAM / clear bandwidth).</summary>
    public static void ApplyEditorDefaults()
    {
        s_tier = QualityTier.Preview;
        ShadowAtlasSize = 2048;
        s_shadowsEnabled = true;
    }

    /// <summary>Player / play-mode: Full tier + 4096 atlas default.</summary>
    public static void ApplyPlayerDefaults()
    {
        s_tier = QualityTier.Full;
        ShadowAtlasSize = 4096;
        s_shadowsEnabled = true;
    }

    private static void ApplyTierDefaults(QualityTier tier)
    {
        if (tier == QualityTier.Preview)
        {
            if (s_shadowAtlasSize > 2048)
                ShadowAtlasSize = 2048;
        }
        else
        {
            if (s_shadowAtlasSize < 4096)
                ShadowAtlasSize = 4096;
        }
    }
}
