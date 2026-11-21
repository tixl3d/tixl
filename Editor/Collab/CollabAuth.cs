#nullable enable
using System.Security.Cryptography;

namespace T3.Editor.Collab;

/// <summary>
/// Password authentication for collab sessions. The password itself never crosses the wire: the host
/// sends a random salt and nonce, the client derives a key with PBKDF2 and answers with an HMAC over
/// the nonce. An eavesdropper on the LAN sees neither the password nor a replayable token, and the
/// challenge changes on every handshake.
/// </summary>
internal static class CollabAuth
{
    private const int KeySizeBytes = 32;
    private const int Pbkdf2Iterations = 50_000;
    public const int SaltSizeBytes = 16;
    public const int NonceSizeBytes = 32;
    public const int MacSizeBytes = 32;

    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltSizeBytes);

    public static byte[] CreateNonce() => RandomNumberGenerator.GetBytes(NonceSizeBytes);

    public static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);
    }

    public static byte[] ComputeMac(byte[] key, byte[] nonce)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(nonce);
    }

    /// <summary>Constant-time comparison so handshakes don't leak timing information.</summary>
    public static bool MacsEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string ToHexString(byte[] bytes) => Convert.ToHexString(bytes);

    public static bool TryFromHexString(string hex, out byte[] bytes)
    {
        bytes = null!;
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
            return false;

        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
