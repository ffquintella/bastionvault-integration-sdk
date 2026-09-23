# `TokenSource` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/TokenSource.cs`](../../../dotnet/BastionVault.IntegrationSdk/TokenSource.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `TokenSource`

#### `Kind`

Which variant this is. AUT-001 requires a client to hold exactly one source and
`SetToken` to replace it with `Static`; this is the
observable that makes both assertable.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenSource.cs:85`*

#### `Static(token)`

A source holding `token` directly (AUT-001).

*Source: `dotnet/BastionVault.IntegrationSdk/TokenSource.cs:88`*

#### `Callback(callback)`

A source that calls `callback` on every resolution (AUT-001). It is
asynchronous because the specification's own example for this variant is a KMS lookup
(D-M2-6), and it is deliberately not de-duplicated: a callback is the application's
own function and the SDK does not get to coalesce its calls on its behalf (D-M2-11(a)).

*Source: `dotnet/BastionVault.IntegrationSdk/TokenSource.cs:100`*

#### `Login(method, credentials, options)`

AUT-001's `Login` variant: the SDK logs in with
`credentials` lazily, on the first authenticated request, and again after
an AUT-003 re-login (D-M2-9's ruling — `Client.Auth.AuthenticateAsync` forces it
eagerly).

The returned source is not yet bound to a client, which is what lets it be handed to
`BastionVaultClientOptions.TokenSource` before one exists. The client binds a performer
at construction; the unbound instance itself never logs in, and
<see cref="ResolveAsync"/> on it reports `BV-AUTH-001` rather than silently answering
"no token".

*Source: `dotnet/BastionVault.IntegrationSdk/TokenSource.cs:123`*

#### `ResolveAsync(cancellationToken)`

D-M2-9's seam: the token this source currently stands for, resolving it if that takes I/O.

A `Login` resolution is single-flighted (D-M2-11(a)). An
awaiting caller that cancels abandons only its own wait (`Task.WaitAsync`) and does
not cancel the shared login, because one caller's cancellation must not fail the
other callers awaiting the same flight. That is why the login runs under
`None` rather than under whichever caller happened to win
the race to start it.



A flight that fails is not cached (D-M2-17): its awaiters all see its failure and
none of them retries, and the next resolution after it attempts again.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenSource.cs:193`*

