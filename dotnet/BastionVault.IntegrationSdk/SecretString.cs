namespace BastionVault.IntegrationSdk;

/// <summary>
/// Holds secret-bearing configuration (currently only <see cref="BastionVaultClientOptions.Token"/>).
/// Its default string representation never contains the value (CNF-031, CNF-032; D-M1a-9).
/// </summary>
public sealed class SecretString : IEquatable<SecretString>
{
    private readonly string? value;

    /// <summary>Wraps <paramref name="value"/>, which is never exposed by <see cref="ToString"/>.</summary>
    public SecretString(string? value)
    {
        this.value = value;
    }

    /// <summary>A <see cref="SecretString"/> holding no value.</summary>
    public static SecretString Empty { get; } = new(null);

    /// <summary>Whether this instance holds a non-empty value.</summary>
    public bool HasValue => !string.IsNullOrEmpty(value);

    /// <summary>
    /// Returns the underlying value. Named deliberately (rather than a property) so that reading the
    /// secret out is always a visible, searchable call site.
    /// </summary>
    public string? Reveal()
    {
        return value;
    }

    /// <summary>Always redacted; never includes the underlying value (CNF-031).</summary>
    public override string ToString()
    {
        return "[REDACTED]";
    }

    /// <inheritdoc/>
    public bool Equals(SecretString? other)
    {
        return other is not null && string.Equals(value, other.value, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Equals(obj as SecretString);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return value?.Length ?? 0;
    }
}
