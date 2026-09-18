namespace BastionVault.IntegrationSdk;

/// <summary>
/// Holds secret-bearing binary material (TRS-013: <c>Transit.Decrypt</c>'s plaintext and a
/// datakey's plaintext). The byte-valued counterpart to <see cref="SecretString"/> (CNF-031,
/// CNF-032): its default string representation never contains the value, and reading the value out
/// is a visible, searchable call (<see cref="Reveal"/>), never a property.
/// </summary>
public sealed class SecretBytes : IEquatable<SecretBytes>
{
    private readonly byte[]? value;

    /// <summary>Wraps <paramref name="value"/>, which is never exposed by <see cref="ToString"/>.</summary>
    public SecretBytes(byte[]? value)
    {
        this.value = value;
    }

    /// <summary>A <see cref="SecretBytes"/> holding no value.</summary>
    public static SecretBytes Empty { get; } = new(null);

    /// <summary>Whether this instance holds a non-empty value.</summary>
    public bool HasValue => value is { Length: > 0 };

    /// <summary>
    /// Returns the underlying bytes. Named deliberately (rather than a property) so that reading
    /// the secret out is always a visible, searchable call site.
    /// </summary>
    public byte[]? Reveal()
    {
        return value;
    }

    /// <summary>Always redacted; never includes the underlying value (CNF-031).</summary>
    public override string ToString()
    {
        return "[REDACTED]";
    }

    /// <inheritdoc/>
    public bool Equals(SecretBytes? other)
    {
        if (other is null)
        {
            return false;
        }

        if (value is null || other.value is null)
        {
            return value is null && other.value is null;
        }

        return value.AsSpan().SequenceEqual(other.value);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Equals(obj as SecretBytes);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return value?.Length ?? 0;
    }
}
