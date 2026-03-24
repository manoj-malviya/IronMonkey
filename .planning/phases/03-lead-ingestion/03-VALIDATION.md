---
phase: 03
slug: lead-ingestion
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-21
---

# Phase 03 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 with Testcontainers PostgreSQL |
| **Config file** | None — tests use PostgreSqlFixture (shared) and unique DB per test (GUID suffix) |
| **Quick run command** | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~LeadIngestionTests" -p:ParallelizeAssembly=false` |
| **Full suite command** | `dotnet test IronMonkey.Tests` |
| **Estimated runtime** | ~45 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~*LeadIngestion*" -p:ParallelizeAssembly=false`
- **After every plan wave:** Run `dotnet test IronMonkey.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 45 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 03-01-01 | 01 | 0 | INGST-01 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ManualLeadCreationTests"` | ❌ W0 | ⬜ pending |
| 03-01-02 | 01 | 0 | INGST-02 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~CsvImportTests"` | ❌ W0 | ⬜ pending |
| 03-01-03 | 01 | 0 | INGST-03 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApiLeadCreationTests"` | ❌ W0 | ⬜ pending |
| 03-01-04 | 01 | 0 | INGST-04 | Integration | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~WebFormTests"` | ❌ W0 | ⬜ pending |
| 03-01-05 | 01 | 0 | INGST-02 | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~CsvValidationTests"` | ❌ W0 | ⬜ pending |
| 03-01-06 | 01 | 0 | INGST-03 | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~ApiAuthTests"` | ❌ W0 | ⬜ pending |
| 03-01-07 | 01 | 0 | INGST-04 | Unit | `dotnet test IronMonkey.Tests --filter "FullyQualifiedName~HoneypotTests"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `IronMonkey.Tests/Integration/ManualLeadCreationTests.cs` — stubs for INGST-01 (manual entry with duplicate warning)
- [ ] `IronMonkey.Tests/Integration/CsvImportTests.cs` — stubs for INGST-02 (CSV validation, import, error handling, duplicate flagging)
- [ ] `IronMonkey.Tests/Integration/ApiLeadCreationTests.cs` — stubs for INGST-03 (API key auth, rate limiting, duplicate response)
- [ ] `IronMonkey.Tests/Integration/WebFormTests.cs` — stubs for INGST-04 (form generation, honeypot, rate limiting, duplicate flagging)
- [ ] `IronMonkey.Tests/Unit/CsvValidationTests.cs` — stubs for CSV column matching, required fields
- [ ] `IronMonkey.Tests/Unit/ApiAuthTests.cs` — stubs for API key hashing, validation, missing key
- [ ] `IronMonkey.Tests/Unit/HoneypotTests.cs` — stubs for bot rejection

*Framework install: Already present (xUnit 2.9.3, Moq 4.20.72, Testcontainers 4.3.0)*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Web form hosted page renders correctly | INGST-04 | Visual rendering cannot be tested via unit/integration test | Navigate to form URL, verify fields display, submit test entry |
| CSV error file is downloadable | INGST-02 | File download UX requires browser interaction | Upload CSV with bad rows, verify error file link appears and downloads |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 45s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
