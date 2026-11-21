#nullable enable

using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Collab;

/// <summary>
/// Identity of a collaboration participant. Users are either locally nicknamed or logged in via
/// Discord. The persistent user id doubles as the seed for the per-user accent color so the same
/// person renders in the same color in every session.
/// </summary>
internal sealed class CollabUserIdentity
{
    public Guid UserId { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = "";
    public string Source { get; init; } = "local";
    public string AvatarUrl { get; init; } = "";

    /// <summary>Language tag from the Discord profile ("hu", "en-US"); empty when not logged in.</summary>
    public string Lang { get; init; } = "";

    public Vector4 AccentColor => CollabColors.FromPacked(UserSettings.Config.CollabCursorColor, UserId);

    /// <summary>
    /// The participant id of a Discord account. Derived from the account id, so the same person
    /// has the same id (and color, and undo history) on every machine and in every session, and
    /// a host can compute it from the account the relay verified.
    /// </summary>
    public static Guid UserIdForDiscordAccount(string discordId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("tixl-collab-discord:" + discordId));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Short display form of a language tag for lists: "hu" -> "HU", "en-US" -> "EN-US".</summary>
    public static string FormatLang(string lang) => string.IsNullOrWhiteSpace(lang) ? "--" : lang.Trim().ToUpperInvariant();
}

internal static class CollabColors
{
    /// <summary>
    /// Deterministic accent color for a user id. Hashes the GUID bytes, spreads the hue over the full
    /// circle and keeps saturation/value fixed so every color stays readable on dark node bodies and
    /// the graph background alike.
    /// </summary>
    public static Vector4 FromId(Guid id)
    {
        var hash = StableHash(id.ToByteArray());
        var hue = (hash & 0xffff) / 65535f;
        return HsvToRgb(hue, 0.72f, 0.98f);
    }

    /// <summary>The picked color from its 0xRRGGBB form; 0 means none was picked and the id decides.</summary>
    public static Vector4 FromPacked(int rgb, Guid fallbackId)
    {
        if (rgb == 0)
            return FromId(fallbackId);

        return new Vector4(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }

    /// <summary>Packs a picked color as 0xRRGGBB; never 0, which is reserved for "not picked".</summary>
    public static int ToPacked(Vector4 color)
    {
        var r = (int)(Math.Clamp(color.X, 0f, 1f) * 255f + 0.5f);
        var g = (int)(Math.Clamp(color.Y, 0f, 1f) * 255f + 0.5f);
        var b = (int)(Math.Clamp(color.Z, 0f, 1f) * 255f + 0.5f);
        return Math.Max((r << 16) | (g << 8) | b, 1);
    }

    public static uint ToUint(Vector4 color)
    {
        var r = (uint)(Math.Clamp(color.X, 0f, 1f) * 255f);
        var g = (uint)(Math.Clamp(color.Y, 0f, 1f) * 255f);
        var b = (uint)(Math.Clamp(color.Z, 0f, 1f) * 255f);
        var a = (uint)(Math.Clamp(color.W, 0f, 1f) * 255f);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    /// <summary>The near-black ink for text drawn on top of accent-filled pills and tags.</summary>
    public static readonly Vector4 Ink = new(0.06f, 0.06f, 0.08f, 1f);

    /// <summary>Black faded to the given alpha, for cursor shadows and rims.</summary>
    public static uint Black(float alpha) => ToUint(new Vector4(0f, 0f, 0f, alpha));

    private static int StableHash(byte[] bytes)
    {
        unchecked
        {
            var hash = 17;
            foreach (var b in bytes)
                hash = hash * 31 + b;

            return hash;
        }
    }

    private static Vector4 HsvToRgb(float hue, float saturation, float value)
    {
        hue = hue % 1f;
        if (hue < 0)
            hue += 1f;

        var hue6 = hue * 6f;
        var sector = (int)hue6 % 6;
        var fraction = hue6 - (int)hue6;
        var minValue = value * (1f - saturation);
        var descending = value * (1f - fraction * saturation);
        var ascending = value * (1f - (1f - fraction) * saturation);

        return sector switch
                   {
                       0 => new Vector4(value, ascending, minValue, 1f),
                       1 => new Vector4(descending, value, minValue, 1f),
                       2 => new Vector4(minValue, value, ascending, 1f),
                       3 => new Vector4(minValue, descending, value, 1f),
                       4 => new Vector4(ascending, minValue, value, 1f),
                       _ => new Vector4(value, minValue, descending, 1f),
                   };
    }
}
