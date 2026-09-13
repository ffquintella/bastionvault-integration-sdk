# Appendix D — Requirement Index

Generated from the `**AREA-NNN**` markers in the specification documents. Every ID listed here that
applies to an SDK's conformance level MUST be referenced by at least one test (TST-040/041).
Regenerate after editing any section (see `tools/` in each SDK, or the generator described in [15](15-testing-requirements.md#traceability)).

Integration scenarios are identified as `ITG-S<nn>` in [15 — Required scenarios](15-testing-requirements.md#required-scenarios) and are tracked in addition to the IDs below.

Total requirements: **388**

| Area | Prefix | Count | Document(s) |
|------|--------|-------|-------------|
| Authentication | `AUT` | 40 | [05-authentication.md](05-authentication.md) |
| Batch | `BAT` | 8 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| Cache coherence | `CCH` | 6 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| Client Configuration | `CFG` | 36 | [02-client-configuration.md](02-client-configuration.md) |
| Conformance & Quality | `CNF` | 27 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| Documentation | `DOC` | 21 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| Cluster Discovery | `DSC` | 24 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| Rate gate | `EFF` | 6 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| Error Model | `ERR` | 22 | [04-error-model.md](04-error-model.md) |
| Files | `FIL` | 1 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| Fixtures | `FIX` | 9 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| Identity | `IDN` | 2 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| Integration testing | `ITG` | 16 | [15-testing-requirements.md](15-testing-requirements.md) |
| KV (common) | `KV` | 6 | [07-kv-engine.md](07-kv-engine.md) |
| KV v1 | `KV1` | 4 | [07-kv-engine.md](07-kv-engine.md) |
| KV v2 | `KV2` | 17 | [07-kv-engine.md](07-kv-engine.md) |
| LDAP | `LDP` | 1 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| Overview | `OVR` | 9 | [00-overview.md](00-overview.md) |
| Pagination | `PAG` | 7 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| PKI | `PKI` | 6 | [09-pki-engine.md](09-pki-engine.md) |
| Resilience | `RES` | 9 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| Resources | `RSC` | 2 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| Rustion | `RUS` | 3 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| SSH Broker | `SSB` | 2 | [10-ssh-engine.md](10-ssh-engine.md) |
| SSH | `SSH` | 3 | [10-ssh-engine.md](10-ssh-engine.md) |
| System API | `SYS` | 34 | [06-system-api.md](06-system-api.md) |
| TOTP | `TOT` | 4 | [11-totp-engine.md](11-totp-engine.md) |
| Transport & Protocol | `TRN` | 38 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| Transit | `TRS` | 7 | [08-transit-engine.md](08-transit-engine.md) |
| Testing | `TST` | 18 | [15-testing-requirements.md](15-testing-requirements.md) |

## All requirement IDs

### AUT — Authentication

| ID | Document |
|----|----------|
| AUT-001 | [05-authentication.md](05-authentication.md) |
| AUT-002 | [05-authentication.md](05-authentication.md) |
| AUT-003 | [05-authentication.md](05-authentication.md) |
| AUT-004 | [05-authentication.md](05-authentication.md) |
| AUT-010 | [05-authentication.md](05-authentication.md) |
| AUT-011 | [05-authentication.md](05-authentication.md) |
| AUT-012 | [05-authentication.md](05-authentication.md) |
| AUT-013 | [05-authentication.md](05-authentication.md) |
| AUT-014 | [05-authentication.md](05-authentication.md) |
| AUT-020 | [05-authentication.md](05-authentication.md) |
| AUT-030 | [05-authentication.md](05-authentication.md) |
| AUT-031 | [05-authentication.md](05-authentication.md) |
| AUT-032 | [05-authentication.md](05-authentication.md) |
| AUT-035 | [05-authentication.md](05-authentication.md) |
| AUT-040 | [05-authentication.md](05-authentication.md) |
| AUT-041 | [05-authentication.md](05-authentication.md) |
| AUT-042 | [05-authentication.md](05-authentication.md) |
| AUT-043 | [05-authentication.md](05-authentication.md) |
| AUT-044 | [05-authentication.md](05-authentication.md) |
| AUT-050 | [05-authentication.md](05-authentication.md) |
| AUT-051 | [05-authentication.md](05-authentication.md) |
| AUT-052 | [05-authentication.md](05-authentication.md) |
| AUT-053 | [05-authentication.md](05-authentication.md) |
| AUT-054 | [05-authentication.md](05-authentication.md) |
| AUT-060 | [05-authentication.md](05-authentication.md) |
| AUT-070 | [05-authentication.md](05-authentication.md) |
| AUT-080 | [05-authentication.md](05-authentication.md) |
| AUT-081 | [05-authentication.md](05-authentication.md) |
| AUT-082 | [05-authentication.md](05-authentication.md) |
| AUT-083 | [05-authentication.md](05-authentication.md) |
| AUT-084 | [05-authentication.md](05-authentication.md) |
| AUT-085 | [05-authentication.md](05-authentication.md) |
| AUT-090 | [05-authentication.md](05-authentication.md) |
| AUT-091 | [05-authentication.md](05-authentication.md) |
| AUT-092 | [05-authentication.md](05-authentication.md) |
| AUT-093 | [05-authentication.md](05-authentication.md) |
| AUT-094 | [05-authentication.md](05-authentication.md) |
| AUT-095 | [05-authentication.md](05-authentication.md) |
| AUT-100 | [05-authentication.md](05-authentication.md) |
| AUT-101 | [05-authentication.md](05-authentication.md) |

### BAT — Batch

| ID | Document |
|----|----------|
| BAT-001 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| BAT-002 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| BAT-003 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| BAT-004 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| BAT-005 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| BAT-006 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| BAT-007 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| BAT-008 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |

### CCH — Cache coherence

| ID | Document |
|----|----------|
| CCH-001 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| CCH-002 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| CCH-003 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| CCH-004 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| CCH-005 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| CCH-006 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |

### CFG — Client Configuration

| ID | Document |
|----|----------|
| CFG-001 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-002 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-003 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-004 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-005 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-010 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-011 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-012 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-013 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-014 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-015 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-016 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-017 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-018 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-020 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-030 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-031 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-032 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-040 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-041 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-042 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-043 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-044 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-050 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-051 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-052 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-053 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-054 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-055 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-060 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-061 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-070 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-071 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-072 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-080 | [02-client-configuration.md](02-client-configuration.md) |
| CFG-081 | [02-client-configuration.md](02-client-configuration.md) |

### CNF — Conformance & Quality

| ID | Document |
|----|----------|
| CNF-001 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-002 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-003 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-010 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-011 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-012 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-013 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-014 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-015 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-020 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-021 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-022 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-023 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-024 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-025 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-026 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-027 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-030 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-031 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-032 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-033 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-034 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-035 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-040 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-041 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-042 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |
| CNF-043 | [01-conformance-and-quality.md](01-conformance-and-quality.md) |

### DOC — Documentation

| ID | Document |
|----|----------|
| DOC-001 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-002 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-003 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-004 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-005 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-006 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-007 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-010 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-011 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-012 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-013 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-014 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-015 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-020 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-021 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-022 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-023 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-024 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-025 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-030 | [16-documentation-requirements.md](16-documentation-requirements.md) |
| DOC-031 | [16-documentation-requirements.md](16-documentation-requirements.md) |

### DSC — Cluster Discovery

| ID | Document |
|----|----------|
| DSC-001 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-002 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-010 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-011 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-012 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-013 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-014 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-020 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-021 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-022 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-030 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-031 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-032 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-033 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-034 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-035 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-036 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-040 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-041 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-042 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-043 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-044 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-045 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| DSC-046 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |

### EFF — Rate gate

| ID | Document |
|----|----------|
| EFF-001 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| EFF-002 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| EFF-003 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| EFF-004 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| EFF-005 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| EFF-006 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |

### ERR — Error Model

| ID | Document |
|----|----------|
| ERR-001 | [04-error-model.md](04-error-model.md) |
| ERR-002 | [04-error-model.md](04-error-model.md) |
| ERR-003 | [04-error-model.md](04-error-model.md) |
| ERR-004 | [04-error-model.md](04-error-model.md) |
| ERR-005 | [04-error-model.md](04-error-model.md) |
| ERR-006 | [04-error-model.md](04-error-model.md) |
| ERR-010 | [04-error-model.md](04-error-model.md) |
| ERR-020 | [04-error-model.md](04-error-model.md) |
| ERR-021 | [04-error-model.md](04-error-model.md) |
| ERR-022 | [04-error-model.md](04-error-model.md) |
| ERR-030 | [04-error-model.md](04-error-model.md) |
| ERR-031 | [04-error-model.md](04-error-model.md) |
| ERR-032 | [04-error-model.md](04-error-model.md) |
| ERR-033 | [04-error-model.md](04-error-model.md) |
| ERR-034 | [04-error-model.md](04-error-model.md) |
| ERR-035 | [04-error-model.md](04-error-model.md) |
| ERR-036 | [04-error-model.md](04-error-model.md) |
| ERR-037 | [04-error-model.md](04-error-model.md) |
| ERR-040 | [04-error-model.md](04-error-model.md) |
| ERR-050 | [04-error-model.md](04-error-model.md) |
| ERR-060 | [04-error-model.md](04-error-model.md) |
| ERR-061 | [04-error-model.md](04-error-model.md) |

### FIL — Files

| ID | Document |
|----|----------|
| FIL-001 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |

### FIX — Fixtures

| ID | Document |
|----|----------|
| FIX-001 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-002 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-003 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-004 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-005 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-006 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-010 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-011 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |
| FIX-012 | [appendix-c-conformance-fixtures.md](appendix-c-conformance-fixtures.md) |

### IDN — Identity

| ID | Document |
|----|----------|
| IDN-001 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| IDN-002 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |

### ITG — Integration testing

| ID | Document |
|----|----------|
| ITG-001 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-002 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-003 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-004 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-005 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-010 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-011 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-012 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-013 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-020 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-021 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-022 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-023 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-030 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-031 | [15-testing-requirements.md](15-testing-requirements.md) |
| ITG-032 | [15-testing-requirements.md](15-testing-requirements.md) |

### KV — KV (common)

| ID | Document |
|----|----------|
| KV-001 | [07-kv-engine.md](07-kv-engine.md) |
| KV-002 | [07-kv-engine.md](07-kv-engine.md) |
| KV-010 | [07-kv-engine.md](07-kv-engine.md) |
| KV-011 | [07-kv-engine.md](07-kv-engine.md) |
| KV-012 | [07-kv-engine.md](07-kv-engine.md) |
| KV-013 | [07-kv-engine.md](07-kv-engine.md) |

### KV1 — KV v1

| ID | Document |
|----|----------|
| KV1-001 | [07-kv-engine.md](07-kv-engine.md) |
| KV1-002 | [07-kv-engine.md](07-kv-engine.md) |
| KV1-003 | [07-kv-engine.md](07-kv-engine.md) |
| KV1-004 | [07-kv-engine.md](07-kv-engine.md) |

### KV2 — KV v2

| ID | Document |
|----|----------|
| KV2-001 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-002 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-003 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-004 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-005 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-006 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-007 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-008 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-009 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-010 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-011 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-020 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-021 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-022 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-023 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-024 | [07-kv-engine.md](07-kv-engine.md) |
| KV2-030 | [07-kv-engine.md](07-kv-engine.md) |

### LDP — LDAP

| ID | Document |
|----|----------|
| LDP-001 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |

### OVR — Overview

| ID | Document |
|----|----------|
| OVR-001 | [00-overview.md](00-overview.md) |
| OVR-002 | [00-overview.md](00-overview.md) |
| OVR-003 | [00-overview.md](00-overview.md) |
| OVR-004 | [00-overview.md](00-overview.md) |
| OVR-005 | [00-overview.md](00-overview.md) |
| OVR-006 | [00-overview.md](00-overview.md) |
| OVR-007 | [00-overview.md](00-overview.md) |
| OVR-008 | [00-overview.md](00-overview.md) |
| OVR-009 | [00-overview.md](00-overview.md) |

### PAG — Pagination

| ID | Document |
|----|----------|
| PAG-001 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| PAG-002 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| PAG-003 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| PAG-004 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| PAG-005 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| PAG-006 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |
| PAG-007 | [14-batch-and-request-efficiency.md](14-batch-and-request-efficiency.md) |

### PKI — PKI

| ID | Document |
|----|----------|
| PKI-001 | [09-pki-engine.md](09-pki-engine.md) |
| PKI-002 | [09-pki-engine.md](09-pki-engine.md) |
| PKI-010 | [09-pki-engine.md](09-pki-engine.md) |
| PKI-011 | [09-pki-engine.md](09-pki-engine.md) |
| PKI-020 | [09-pki-engine.md](09-pki-engine.md) |
| PKI-030 | [09-pki-engine.md](09-pki-engine.md) |

### RES — Resilience

| ID | Document |
|----|----------|
| RES-001 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-002 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-003 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-004 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-010 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-011 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-020 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-021 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |
| RES-030 | [13-cluster-discovery-and-resilience.md](13-cluster-discovery-and-resilience.md) |

### RSC — Resources

| ID | Document |
|----|----------|
| RSC-001 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| RSC-002 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |

### RUS — Rustion

| ID | Document |
|----|----------|
| RUS-001 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| RUS-002 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |
| RUS-003 | [12-other-engines-and-identity.md](12-other-engines-and-identity.md) |

### SSB — SSH Broker

| ID | Document |
|----|----------|
| SSB-001 | [10-ssh-engine.md](10-ssh-engine.md) |
| SSB-002 | [10-ssh-engine.md](10-ssh-engine.md) |

### SSH — SSH

| ID | Document |
|----|----------|
| SSH-001 | [10-ssh-engine.md](10-ssh-engine.md) |
| SSH-002 | [10-ssh-engine.md](10-ssh-engine.md) |
| SSH-003 | [10-ssh-engine.md](10-ssh-engine.md) |

### SYS — System API

| ID | Document |
|----|----------|
| SYS-001 | [06-system-api.md](06-system-api.md) |
| SYS-002 | [06-system-api.md](06-system-api.md) |
| SYS-005 | [06-system-api.md](06-system-api.md) |
| SYS-008 | [06-system-api.md](06-system-api.md) |
| SYS-010 | [06-system-api.md](06-system-api.md) |
| SYS-011 | [06-system-api.md](06-system-api.md) |
| SYS-012 | [06-system-api.md](06-system-api.md) |
| SYS-013 | [06-system-api.md](06-system-api.md) |
| SYS-020 | [06-system-api.md](06-system-api.md) |
| SYS-021 | [06-system-api.md](06-system-api.md) |
| SYS-022 | [06-system-api.md](06-system-api.md) |
| SYS-023 | [06-system-api.md](06-system-api.md) |
| SYS-024 | [06-system-api.md](06-system-api.md) |
| SYS-025 | [06-system-api.md](06-system-api.md) |
| SYS-026 | [06-system-api.md](06-system-api.md) |
| SYS-030 | [06-system-api.md](06-system-api.md) |
| SYS-040 | [06-system-api.md](06-system-api.md) |
| SYS-041 | [06-system-api.md](06-system-api.md) |
| SYS-042 | [06-system-api.md](06-system-api.md) |
| SYS-043 | [06-system-api.md](06-system-api.md) |
| SYS-045 | [06-system-api.md](06-system-api.md) |
| SYS-050 | [06-system-api.md](06-system-api.md) |
| SYS-051 | [06-system-api.md](06-system-api.md) |
| SYS-052 | [06-system-api.md](06-system-api.md) |
| SYS-053 | [06-system-api.md](06-system-api.md) |
| SYS-060 | [06-system-api.md](06-system-api.md) |
| SYS-061 | [06-system-api.md](06-system-api.md) |
| SYS-062 | [06-system-api.md](06-system-api.md) |
| SYS-070 | [06-system-api.md](06-system-api.md) |
| SYS-080 | [06-system-api.md](06-system-api.md) |
| SYS-090 | [06-system-api.md](06-system-api.md) |
| SYS-091 | [06-system-api.md](06-system-api.md) |
| SYS-100 | [06-system-api.md](06-system-api.md) |
| SYS-101 | [06-system-api.md](06-system-api.md) |

### TOT — TOTP

| ID | Document |
|----|----------|
| TOT-001 | [11-totp-engine.md](11-totp-engine.md) |
| TOT-002 | [11-totp-engine.md](11-totp-engine.md) |
| TOT-003 | [11-totp-engine.md](11-totp-engine.md) |
| TOT-004 | [11-totp-engine.md](11-totp-engine.md) |

### TRN — Transport & Protocol

| ID | Document |
|----|----------|
| TRN-001 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-002 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-003 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-010 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-011 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-012 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-013 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-014 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-015 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-016 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-017 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-020 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-021 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-022 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-023 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-030 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-031 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-032 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-033 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-040 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-041 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-042 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-043 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-050 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-051 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-052 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-053 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-054 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-060 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-070 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-071 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-072 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-080 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-081 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-090 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-091 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-092 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |
| TRN-100 | [03-transport-and-protocol.md](03-transport-and-protocol.md) |

### TRS — Transit

| ID | Document |
|----|----------|
| TRS-001 | [08-transit-engine.md](08-transit-engine.md) |
| TRS-002 | [08-transit-engine.md](08-transit-engine.md) |
| TRS-003 | [08-transit-engine.md](08-transit-engine.md) |
| TRS-010 | [08-transit-engine.md](08-transit-engine.md) |
| TRS-011 | [08-transit-engine.md](08-transit-engine.md) |
| TRS-012 | [08-transit-engine.md](08-transit-engine.md) |
| TRS-013 | [08-transit-engine.md](08-transit-engine.md) |

### TST — Testing

| ID | Document |
|----|----------|
| TST-001 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-002 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-003 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-004 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-010 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-011 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-012 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-013 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-020 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-021 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-030 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-031 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-040 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-041 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-042 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-050 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-051 | [15-testing-requirements.md](15-testing-requirements.md) |
| TST-060 | [15-testing-requirements.md](15-testing-requirements.md) |

