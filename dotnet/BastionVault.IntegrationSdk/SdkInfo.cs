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
    public static string SpecificationVersion => "1.1.0";

    /// <summary>
    /// The version of this SDK package, matching the <c>&lt;Version&gt;</c> element in
    /// <c>BastionVault.IntegrationSdk.csproj</c>.
    /// </summary>
    public static string SdkVersion => "0.15.0";

    /// <summary>
    /// The upstream BastionVault server release that <see cref="SpecificationVersion"/> was
    /// derived from (CNF-047), pinned in <c>specifications/provenance.json</c> as
    /// <c>specificationVersion</c>'s companion <c>upstream</c> release.
    /// </summary>
    public static string SpecificationSourceRelease => "0.42.0";

    /// <summary>
    /// The upstream git ref (tag) corresponding to <see cref="SpecificationSourceRelease"/>,
    /// as pinned in <c>specifications/provenance.json</c> (CNF-047).
    /// </summary>
    public static string SpecificationSourceRef => "v0.42.0";
}
