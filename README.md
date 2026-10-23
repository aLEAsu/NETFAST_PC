# NetPulse Optimizer

Windows desktop tool to diagnose, measure, and (in later phases, under explicit user
authorization) optimize a PC's network path.

**Current status: Phases 3-7 complete, plus a post-Phase-7 completion pass.** Diagnostics now
combine ICMP latency, a TCP connectivity confirmation, and DNS resolution timing, so the app
can finally tell "ICMP filtered but reachable" apart from "actually down." Six deterministic
recommendation rules evaluate every run; one of them (slow/failed DNS resolution) has a real,
reversible action behind it - `SetAdapterDnsServersAction` - now **reachable from the running
app's UI** (select an adapter, run a diagnostic, click "Apply suggested DNS servers"). Six
diagnostic profiles (General/Gaming/Downloads/VideoCalls/Conservative/Maintenance) adjust
measurement parameters and result ordering. A real per-user WiX MSI installer exists, with a
tested install/upgrade/uninstall cycle and a demonstrated (self-signed) code-signing
procedure.

**What is still not true:** no automated session has ever executed the DNS action's actual
write against a real adapter - see `documentation/01-architecture.md` §10 and
§4 of `documentation/06-continuation-brief.md` for exactly what that means and what a human
needs to do about it. No CA-issued signing certificate exists. Bufferbloat testing,
per-process bandwidth, pktmon/ETW diagnostics, advanced NIC properties, and WinUI 3 are not
implemented - each has a concrete plan in `documentation/06-continuation-brief.md` §6-7.

Read in this order: [`README.md`](README.md) (here) →
[`documentation/01-architecture.md`](documentation/01-architecture.md) (design) →
[`documentation/02-roadmap-and-limitations.md`](documentation/02-roadmap-and-limitations.md)
(phase-by-phase status) →
[`documentation/05-code-audit-and-recommendations.md`](documentation/05-code-audit-and-recommendations.md)
(latest audit) →
[`documentation/06-continuation-brief.md`](documentation/06-continuation-brief.md) (what to
do next, in detail). `03-phase6-security-performance.md` and `04-phase7-distribution.md`
cover their respective phases.

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
src/NetPulse.Domain                 pure entities, statistics math, recommendation rules, profiles, privileged-op validators - no OS/UI dependency
src/NetPulse.Application            use cases and ports (diagnostics, recommendations, TCP/DNS probes)
src/NetPulse.Infrastructure.Windows adapter inventory, ICMP/TCP/DNS probes, DNS read (all read-only) + the elevated-helper launcher
src/NetPulse.Persistence            local SQLite history + privileged-operation audit log
src/NetPulse.PrivilegedAgent        SetAdapterDnsServersAction - orchestrates elevation, never performs it
src/NetPulse.PrivilegedAgent.Host   separate elevated process; the ONLY code that writes DNS config (see its docs)
src/NetPulse.App                    WPF dashboard (MVVM) - adapter selection, profile picker, DNS action wired to real buttons
tests/NetPulse.UnitTests            real tests for Domain/Application/PrivilegedAgent logic (~104 tests, all fakes)
tests/NetPulse.IntegrationTests     real tests against the actual adapter/ICMP/TCP/DNS/SQLite/WMI-read, plus NetPulse.App's FindingRowViewModel (~40 tests)
tests/NetPulse.SafetyTests          real recovery-lifecycle tests against fakes (5 tests) - elevated write path still needs manual verification
tests/NetPulse.PerformanceTests     real CPU/allocation benchmarks for the diagnostic pipeline (4 tests)
installer/                          WiX MSI installer (build.ps1, sign.ps1) - see its own README
documentation/                      design docs - see 06-continuation-brief.md for what's next
```

## Building the installer

```powershell
cd installer
.\build.ps1 -ProductVersion 0.1.0.0
```

Produces a per-user `NetPulseOptimizer.msi` requiring no admin rights to install or
uninstall. See [`installer/README.md`](installer/README.md) and
[`documentation/04-phase7-distribution.md`](documentation/04-phase7-distribution.md).
