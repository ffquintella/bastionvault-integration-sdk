using System.Buffers;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// AUT-035's two-step WebAuthn login, implemented once for both surfaces that expose it: the
/// userpass mount's <c>auth/{mount}/fido2/login/{begin,complete}</c> and the standalone mount's
/// <c>auth/{mount}/login/{begin,complete}</c> (Appendix A).
/// </summary>
/// <remarks>
/// <para>
/// <b>Opaque means opaque.</b> The begin response is surfaced as text and the caller's credential
/// is embedded as an equivalent JSON <i>value</i>, uninterpreted — see <c>CompleteBody</c> for why
/// "equivalent" rather than "byte-identical" is both the truth and enough (D-M6-20). The only
/// thing this type does to either payload is check that the credential parses as JSON at all —
/// which is not interpretation, it is the difference between sending a request and sending a
/// malformed one, and it is what turns a caller mistake into <c>BV-INPUT-001</c> before a network
/// call instead of a server <c>400</c> (D-M6-4).
/// </para>
/// <para>
/// Both requests are marked <c>isLogin</c>. TRN-015 says a login carries no token header, and
/// Appendix A marks all four paths <c>Auth: no</c>; the executor's own <c>auth/*/login</c> pattern
/// already covers the standalone mount's two paths but cannot see the userpass mount's, so the
/// flag is what makes the two behave identically rather than accidentally.
/// </para>
/// </remarks>
internal sealed class Fido2LoginFlow
{
    /// <summary>The relaxed escaper, for the parity reason recorded on <c>AuthEndpoint</c> (D-M6-12).</summary>
    private static readonly JsonWriterOptions BodyWriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AuthEndpoint endpoint;
    private readonly LoginRunner runner;
    private readonly bool standalone;

    public Fido2LoginFlow(ClientContext context, string activeNamespace, bool standalone)
    {
        endpoint = new AuthEndpoint(context, activeNamespace);
        runner = new LoginRunner(context, activeNamespace);
        this.standalone = standalone;
    }

    public async Task<WebAuthnAssertionOptions> BeginAsync(
        string username,
        string mount,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        string path = Path(mount, "begin");
        Response? response = await endpoint.WriteTokenlessAsync(
            path, AuthEndpoint.JsonObject(("username", username)), options, cancellationToken).ConfigureAwait(false);

        return new WebAuthnAssertionOptions { Json = AssertionJson(response, path) };
    }

    public Task<AuthInfo> CompleteAsync(
        string username,
        string credentialJson,
        string mount,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialJson);
        return runner.LoginAsync(Path(mount, "complete"), CompleteBody(username, credentialJson), install: true, options, cancellationToken);
    }

    /// <summary>
    /// Appendix A's two spellings. The userpass mount nests the flow under <c>fido2/</c>; the
    /// standalone mount is the flow.
    /// </summary>
    private string Path(string mount, string step)
    {
        string encoded = AuthEndpoint.Mount(mount);
        return standalone ? $"auth/{encoded}/login/{step}" : $"auth/{encoded}/fido2/login/{step}";
    }

    /// <summary>
    /// D-M6-4: the completion body is <c>{"username": …, "credential": &lt;the caller's JSON&gt;}</c>.
    /// The credential is embedded as <b>an equivalent JSON value</b> — not as a byte copy of the
    /// caller's text, and not as a re-modelled object either.
    /// </summary>
    /// <remarks>
    /// <c>WriteTo</c> writes the parsed document back out, so insignificant whitespace is dropped,
    /// escape sequences are normalised, numbers are canonicalised and a duplicate member collapses:
    /// the bytes may differ from the caller's, the JSON value does not (D-M6-20). That is
    /// sufficient here because WebAuthn's signed material travels inside base64url <i>string
    /// values</i>, and a string's value survives re-serialisation unchanged — what a verifier
    /// hashes is the decoded bytes of the member, not the document's spelling. The SDK still
    /// interprets no member, which is what AUT-035 forbids; it is the difference between
    /// re-serialising a value and re-modelling it.
    /// </remarks>
    private static ReadOnlyMemory<byte> CompleteBody(string username, string credentialJson)
    {
        JsonDocument credential;
        try
        {
            credential = JsonDocument.Parse(credentialJson);
        }
        catch (JsonException cause)
        {
            throw AuthEndpoint.InvalidArgument(
                "credentialJson",
                $"AUT-035 passes the WebAuthn credential through as opaque JSON, and this is not JSON: {cause.Message}");
        }

        using (credential)
        {
            ArrayBufferWriter<byte> buffer = new();
            using Utf8JsonWriter writer = new(buffer, BodyWriterOptions);
            writer.WriteStartObject();
            writer.WriteString("username", username);
            writer.WritePropertyName("credential");
            credential.RootElement.WriteTo(writer);
            writer.WriteEndObject();
            writer.Flush();
            return buffer.WrittenMemory;
        }
    }

    /// <summary>
    /// The assertion options as text: the envelope's <c>data</c> when the server wrapped them in
    /// one (Shape A), and the whole body when it did not. Uninterpreted either way.
    /// </summary>
    private string AssertionJson(Response? response, string path)
    {
        if (response?.Data is { } data)
        {
            ArrayBufferWriter<byte> buffer = new();
            using Utf8JsonWriter writer = new(buffer, BodyWriterOptions);
            writer.WriteStartObject();
            foreach ((string name, JsonElement value) in data)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        throw endpoint.EnvelopeMismatch(path, "data");
    }
}
