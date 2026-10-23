# NetPulse Optimizer

Windows desktop tool to diagnose, measure, and (in later phases, under explicit user
authorization) optimize a PC's network path.

**Current status: Phases 3-5 — diagnostics, recommendations, and a first privileged action.**
This build enumerates network adapters, runs bounded ICMP latency diagnostics with
confidence-labeled findings, evaluates a fixed set of deterministic rules to produce
risk-labeled recommendations, and implements one real, reversible optimization
(`SetAdapterDnsServersAction`) behind a per-operation UAC elevation boundary. **That action's
actual write has never been executed against a real machine** - see
`documentation/01-architecture.md` §10 for exactly why and what that means before you trust it.

See [`documentation/01-architecture.md`](documentation/01-architecture.md) for the full
design and [`documentation/02-roadmap-and-limitations.md`](documentation/02-roadmap-and-limitations.md)
for what is and isn't implemented yet.

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
tests/NetPulse.UnitTests            real tests for Domain/Application/PrivilegedAgent logic (83 tests, all fakes)
tests/NetPulse.IntegrationTests     real tests against the actual adapter/ICMP/SQLite/WMI-read (28 tests)
tests/NetPulse.SafetyTests          scaffolded, empty - where the elevated write path should be verified manually
tests/NetPulse.PerformanceTests     scaffolded, empty
documentation/                      design docs
```
