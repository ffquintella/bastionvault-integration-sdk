namespace BastionVault.IntegrationSdk;

/// <summary>
/// One row of <c>specifications/appendix-b-error-catalogue.md</c> §1 (ERR-036). The shape is pinned
/// by <c>decisions/0005-m1c-error-model.md</c> D-M1c-7; the rows themselves are generated from the
/// appendix by <c>tools/error-catalogue</c> (D-M1c-1) and never hand-transcribed.
/// </summary>
/// <param name="Code">Stable identifier, <c>BV-&lt;CATEGORY&gt;-&lt;NNN&gt;</c>.</param>
/// <param name="Name">The appendix's <c>Name</c> column, e.g. <c>PermissionDenied</c>.</param>
/// <param name="Category">The category this code belongs to.</param>
/// <param name="Message">The default English message (ERR-030).</param>
/// <param name="Hint">The actionable hint (ERR-031); never empty.</param>
/// <param name="Retryable">ERR-006's retryability, cross-checked against the appendix's <c>R</c> column at generation time (D-M1c-8).</param>
public sealed record ErrorCatalogEntry(
    string Code,
    string Name,
    ErrorCategory Category,
    string Message,
    string Hint,
    bool Retryable);
