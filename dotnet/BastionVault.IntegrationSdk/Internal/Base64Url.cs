using System.Text;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// IDN-001's base64url-no-padding encoding, done client-side so a caller passes the plain path.
/// Shared by <see cref="IdentitySharingOperations"/>'s <c>target</c> and
/// <see cref="AssetGroupOperations.BySecretAsync"/>'s <c>path</c> (DR-0017 slice a).
/// </summary>
internal static class Base64Url
{
    /// <summary>Standard base64 with <c>+</c>→<c>-</c>, <c>/</c>→<c>_</c>, and <c>=</c> padding stripped.</summary>
    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>The inverse: restores <c>+</c>/<c>/</c> and re-pads before decoding.</summary>
    public static string Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string restored = value.Replace('-', '+').Replace('_', '/');
        int remainder = restored.Length % 4;
        string padded = remainder == 0 ? restored : restored + new string('=', 4 - remainder);
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
