namespace BastionVault.IntegrationSdk;

/// <summary>
/// Version metadata for this SDK. Per decision D-M0-12, this is the SDK's only public surface
/// at milestone M0.
/// </summary>
public static class SdkInfo
{
    /// <summary>
    /// The version of the BastionVault Integration SDK specification (see
    /// <c>specifications/00-overview.md#specification-version</c>) that this library implements.
    /// </summary>
    public static string SpecificationVersion => "1.0.0";

    /// <summary>
    /// The version of this SDK package, matching the <c>&lt;Version&gt;</c> element in
    /// <c>BastionVault.IntegrationSdk.csproj</c>.
    /// </summary>
    public static string SdkVersion => "0.5.0";
}
