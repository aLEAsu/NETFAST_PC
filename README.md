# NetPulse Optimizer

Windows desktop tool to diagnose, measure, and (in later phases, under explicit user
authorization) optimize a PC's network path.

**Current status: Phase 3 — diagnostics-only prototype.** This build enumerates network
adapters and runs bounded ICMP latency diagnostics with confidence-labeled findings. It does
not modify any system, network, or registry setting.

See [`documentation/01-architecture.md`](documentation/01-architecture.md) for the full
design and [`documentation/02-roadmap-and-limitations.md`](documentation/02-roadmap-and-limitations.md)
for what is and isn't implemented yet.

## Build and run

Requires the .NET 10 SDK.

```bash
dotnet build NetPulseOptimizer.slnx
dotnet test tests/NetPulse.UnitTests/NetPulse.UnitTests.csproj
dotnet run --project src/NetPulse.App/NetPulse.App.csproj
```

## Solution layout

```
src/NetPulse.Domain                 pure entities and statistics math, no OS/UI dependency
src/NetPulse.Application            use cases and ports
src/NetPulse.Infrastructure.Windows adapter inventory + ICMP probe (read-only)
src/NetPulse.Persistence            local SQLite history store
src/NetPulse.PrivilegedAgent        placeholder - no operations yet (see its README)
src/NetPulse.App                    WPF dashboard (MVVM)
tests/NetPulse.UnitTests            real tests for Domain/Application logic
tests/NetPulse.IntegrationTests     scaffolded, empty
tests/NetPulse.SafetyTests          scaffolded, empty
tests/NetPulse.PerformanceTests     scaffolded, empty
documentation/                      design docs
```
