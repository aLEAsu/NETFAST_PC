# NetPulse Optimizer

Windows desktop tool to diagnose, measure, and (in later phases, under explicit user
authorization) optimize a PC's network path.

**Current status: Phases 3-4 — diagnostics + recommendation engine.** This build enumerates
network adapters, runs bounded ICMP latency diagnostics with confidence-labeled findings, and
evaluates a fixed set of deterministic rules to produce risk-labeled, evidence-backed
recommendations. It does not modify any system, network, or registry setting — recommendations
are informational only; nothing is applied automatically.

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
src/NetPulse.Domain                 pure entities, statistics math, and recommendation rules - no OS/UI dependency
src/NetPulse.Application            use cases and ports
src/NetPulse.Infrastructure.Windows adapter inventory + ICMP probe (read-only)
src/NetPulse.Persistence            local SQLite history store
src/NetPulse.PrivilegedAgent        placeholder - no operations yet (see its README)
src/NetPulse.App                    WPF dashboard (MVVM)
tests/NetPulse.UnitTests            real tests for Domain/Application logic (46 tests)
tests/NetPulse.IntegrationTests     real tests against the actual adapter/ICMP/SQLite (21 tests)
tests/NetPulse.SafetyTests          scaffolded, empty
tests/NetPulse.PerformanceTests     scaffolded, empty
documentation/                      design docs
```
