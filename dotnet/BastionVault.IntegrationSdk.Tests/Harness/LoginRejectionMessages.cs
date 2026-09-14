using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk.Tests.Harness;

/// <summary>
/// D-M2-25 item 3: the harness's one view of the <b>generated</b> <c>data.error</c> table
/// (<c>ErrorCatalogData.LoginRejections</c>), so the mock server's <c>login-failure-as-200</c>
/// simulation (TST-021) and the AUT-011 tests read the same list the recogniser is built from.
/// </summary>
/// <remarks>
/// M1c's rule was "generated, never hand-transcribed", and the two lists overlapping stopped being
/// hypothetical the moment AUT-011's rows became reachable from a <c>200</c> body (D-M2-4a). This
/// type exists so there is exactly one place the test tree reaches the generated table, rather than
/// a string literal in the mock server and a different one in each test.
/// </remarks>
public static class LoginRejectionMessages
{
    /// <summary>Every generated <c>(code, message)</c> row, in Appendix B §2 table order.</summary>
    public static IReadOnlyList<(string Code, string Message)> All { get; } =
        ErrorCatalogData.LoginRejections.Select(row => (row.Code, row.Message)).ToArray();

    /// <summary>The first generated message that maps to <paramref name="code"/> at status 200.</summary>
    /// <exception cref="InvalidOperationException">
    /// No generated row produces <paramref name="code"/>. A hard failure rather than a fallback
    /// literal: a missing row means the appendix and the harness disagree, which is the exact drift
    /// D-M2-25 item 3 removed.
    /// </exception>
    public static string For(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        foreach ((string rowCode, string message) in All)
        {
            if (string.Equals(rowCode, code, StringComparison.Ordinal))
            {
                return message;
            }
        }

        throw new InvalidOperationException(
            $"The generated login-rejection table carries no message for '{code}'. "
                + "Add the Appendix B §2 row and regenerate (tools/error-catalogue).");
    }
}
