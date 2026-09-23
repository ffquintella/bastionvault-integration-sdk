# `BastionVaultException` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs`](../../../dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `BastionVaultException`

#### `Code`

Stable identifier, `BV-&lt;CATEGORY&gt;-&lt;NNN&gt;`. Never localised, never changed.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:75`*

#### `Category`

One of the categories in <see cref="ErrorCategory"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:78`*

#### `Hint`

Actionable guidance. Never empty (enforced at construction).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:81`*

#### `ServerMessage`

The raw server `error` string or joined `errors[]`, when the error came from a server response.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:84`*

#### `ServerErrors`

The raw `errors[]` array, when present.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:87`*

#### `StatusCode`

HTTP status, when a request was made.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:90`*

#### `RetryAfter`

Parsed `Retry-After`, when present.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:93`*

#### `Retryable`

Whether an identical retry may succeed without operator/developer action (ERR-006).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:96`*

#### `Method`

`GET`, `POST`, `LIST`, etc., when a request was made.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:99`*

#### `Path`

Logical path, with namespace prefix for display, when a request was made. Any
`lookup`/`renew`/`revoke`/`revoke-orphan` token segment is already
replaced with `&lt;redacted&gt;` (ERR-003).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:106`*

#### `Address`

Server host (no credentials, no query string), when a request was made.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:109`*

#### `Attempts`

Number of attempts made (0 for configuration errors, which never send a request).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:112`*

#### `Details`

Structured extras: `setting`, `path`, etc.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:115`*

#### `Cause`

Underlying runtime exception (IO, TLS, JSON), when this error wraps one.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:118`*

#### `Timestamp`

When this error was created (UTC).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:121`*

#### `ToString()`

ERR-002's one-line form: `"&lt;Code&gt;: &lt;Message&gt; — &lt;Hint&gt;"`, optionally followed
by `" [HTTP &lt;status&gt; &lt;METHOD&gt; &lt;path&gt;]"` and `" (server: "&lt;ServerMessage&gt;")"`.
Never contains a newline (ERR-002) and, per ERR-003, never contains secret material.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultException.cs:199`*

