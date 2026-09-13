# bastionvault-integration-sdk

Base repository for the BastionVault Integration SDK with libraries in:

- .NET (`/dotnet/BastionVault.IntegrationSdk`)
- Rust (`/rust/bastionvault-integration-sdk`)
- Python (`/python`)

## Build artifacts locally

### .NET

```bash
dotnet pack ./dotnet/BastionVault.IntegrationSdk/BastionVault.IntegrationSdk.csproj -p:ContinuousIntegrationBuild=true
```

### Rust

```bash
cargo package --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml
```

### Python

```bash
python -m pip install --upgrade build
python -m build ./python --outdir ./artifacts/python
```

## Tests

- .NET: `dotnet test ./dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj`
- Rust: `cargo test --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml`
- Python: `PYTHONPATH=./python/src python -m unittest discover -s ./python/tests`
