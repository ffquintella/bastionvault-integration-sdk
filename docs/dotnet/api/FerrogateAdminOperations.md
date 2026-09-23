# `FerrogateAdminOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `FerrogateAdminOperations`

#### `ReadConfigAsync(mount, options, cancellationToken)`

AUT-054: `GET auth/{mount}/config` — root-authenticated per Appendix A.

Wire params: `mount` builds the route; no body. Returns `null` on a `404` with an empty body. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Ferrogate.Admin.ReadConfig — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:38`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

AUT-054: `POST auth/{mount}/config` — root-authenticated per Appendix A.

Wire params: `config` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Ferrogate.Admin.WriteConfig — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:46`*

#### `RegisterAsync(machine, mount, options, cancellationToken)`

AUT-054: `POST auth/{mount}/register` — an administrator registering a machine directly, root-authenticated per Appendix A.

Wire params: `machine` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Ferrogate.Admin.Register — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:54`*

#### `ListMachinesAsync(mount, options, cancellationToken)`

AUT-054: `LIST auth/{mount}/machines/` — root-authenticated per Appendix A.

Wire params: `mount` builds the route; no body. Returns an empty list when there are none (TRN-050), never `null`. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Ferrogate.Admin.ListMachines — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:62`*

#### `ReadMachineAsync(machineId, mount, options, cancellationToken)`

AUT-054: `GET auth/{mount}/machines/{machineId}` — root-authenticated per Appendix A.

Wire params: `mount`, `machineId` build the route; no body. Returns `null` on a `404` with an empty body. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Ferrogate.Admin.ReadMachine — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:70`*

#### `DeleteMachineAsync(machineId, mount, options, cancellationToken)`

AUT-054: `DELETE auth/{mount}/machines/{machineId}` — root-authenticated per Appendix A; the machine must re-register to authenticate again.

Wire params: `mount`, `machineId` build the route; no body. Returns nothing; an already-absent machine is not an error. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Ferrogate.Admin.DeleteMachine — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:78`*

#### `ApproveAsync(machineId, mount, options, cancellationToken)`

AUT-054: `POST auth/{mount}/machines/{machineId}/approve`, root-authenticated per Appendix A. The machine may log in afterwards.

Wire params: `mount`, `machineId` build the route; no body. Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Ferrogate.Admin.Approve — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:86`*

#### `RejectAsync(machineId, mount, options, cancellationToken)`

AUT-054: `POST auth/{mount}/machines/{machineId}/reject`, root-authenticated per Appendix A. Its logins then fail `BV-AUTH-013`.

Wire params: `mount`, `machineId` build the route; no body. Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061) — `BV-AUTH-013` is raised on the machine's subsequent <em>login</em>, not by this call.

**Spec:** `Auth.Ferrogate.Admin.Reject — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:94`*

#### `RevokeAsync(machineId, mount, options, cancellationToken)`

AUT-054: `POST auth/{mount}/machines/{machineId}/revoke`, root-authenticated per Appendix A. Its logins then fail `BV-AUTH-014`.

Wire params: `mount`, `machineId` build the route; no body. Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-054). No error codes beyond the common set (ERR-061) — `BV-AUTH-014` is raised on the machine's subsequent <em>login</em>, not by this call.

**Spec:** `Auth.Ferrogate.Admin.Revoke — AUT-054`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateAdminOperations.cs:102`*

