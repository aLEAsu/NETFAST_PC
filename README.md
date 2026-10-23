# NetPulse Optimizer

Windows desktop tool to diagnose, measure, and (in later phases, under explicit user
authorization) optimize a PC's network path.

**Current status: Phases 3-7 — diagnostics, recommendations, a first privileged action, a
security/performance pass, and a real installer.** This build enumerates network adapters,
runs bounded ICMP latency diagnostics with confidence-labeled findings, evaluates a fixed
set of deterministic rules to produce risk-labeled recommendations, and implements one real,
reversible optimization (`SetAdapterDnsServersAction`) behind a per-operation UAC elevation
boundary. **That action's actual write has never been executed against a real machine** -
see `documentation/01-architecture.md` §10 for exactly why and what that means before you
trust it. Phase 6 measured real idle CPU (0.10%, within the brief's reference) and memory
(~280-290 MB, above it, confirmed stable over a full minute), found and fixed one real
timing bug, and ran a security audit. Phase 7 built a real per-user MSI installer (no admin
needed to install/uninstall), tested a full install/upgrade/uninstall cycle, found and fixed
a real "doesn't show up in Add/Remove Programs" bug, and established a code-signing
procedure with a self-signed test certificate — **no CA-issued certificate exists**, so
nothing here is trusted-publisher-signed yet.

See [`documentation/01-architecture.md`](documentation/01-architecture.md) for the full
design, [`documentation/02-roadmap-and-limitations.md`](documentation/02-roadmap-and-limitations.md)
for what is and isn't implemented yet,
[`documentation/03-phase6-security-performance.md`](documentation/03-phase6-security-performance.md)
for the security/performance findings, and
[`documentation/04-phase7-distribution.md`](documentation/04-phase7-distribution.md) for the
installer/signing work.

## Build and run

Requires the .NET 10 SDK.

```bash
dotnet build NetPulseOptimizer.slnx
dotnet test tests/NetPulse.UnitTests/NetPulse.UnitTests.csproj
dotnet test tests/NetPulse.IntegrationTests/NetPulse.IntegrationTests.csproj
dotnet run --project src/NetPulse.App/NetPulse.App.csproj
```

## Solution layout

```
src/NetPulse.Domain                 pure entities, statistics math, recommendation rules, privileged-op validators - no OS/UI dependency
src/NetPulse.Application            use cases and ports
src/NetPulse.Infrastructure.Windows adapter inventory, ICMP probe, DNS read (all read-only) + the elevated-helper launcher
src/NetPulse.Persistence            local SQLite history + privileged-operation audit log
src/NetPulse.PrivilegedAgent        SetAdapterDnsServersAction - orchestrates elevation, never performs it
src/NetPulse.PrivilegedAgent.Host   separate elevated process; the ONLY code that writes DNS config (see its docs)
src/NetPulse.App                    WPF dashboard (MVVM)
tests/NetPulse.UnitTests            real tests for Domain/Application/PrivilegedAgent logic (88 tests, all fakes)
tests/NetPulse.IntegrationTests     real tests against the actual adapter/ICMP/SQLite/WMI-read (28 tests)
tests/NetPulse.SafetyTests          real recovery-lifecycle tests against fakes (5 tests) - elevated write path still needs manual verification
tests/NetPulse.PerformanceTests     real CPU/allocation benchmarks for the diagnostic pipeline (4 tests)
installer/                          WiX MSI installer (build.ps1, sign.ps1) - see its own README
documentation/                      design docs
```

## Building the installer

```powershell
cd installer
.\build.ps1 -ProductVersion 0.1.0.0
```

Produces a per-user `NetPulseOptimizer.msi` requiring no admin rights to install or
uninstall. See [`installer/README.md`](installer/README.md) and
[`documentation/04-phase7-distribution.md`](documentation/04-phase7-distribution.md).
