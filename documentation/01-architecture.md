# NetPulse Optimizer — Technical Design

Status: **Phase 3 (diagnostics-only prototype) implemented and building.** No optimization,
no privileged operation, and no system/network modification exists in this codebase yet.
Everything below that is not reflected in `src/` is a proposal for later phases, labeled as such.

## 1. Executive summary

NetPulse Optimizer is a Windows desktop application that diagnoses, measures, and —
in later phases, under explicit user authorization — optimizes a PC's network path. The
current build enumerates network adapters, runs bounded ICMP latency samples against
user-chosen targets, computes median/P95/jitter/loss, classifies each result with an
explicit confidence level, and stores the aggregated result locally in SQLite. It changes
nothing on the machine.

## 2. Requirements

### Functional (this phase)
- Enumerate adapters (Ethernet/USB-Ethernet/Wi-Fi/loopback) read-only via `NetworkInterface`.
- Run a bounded, user-controlled number of ICMP probes per target; never an unbounded loop.
- Compute min/median/P95/max/jitter/loss from the samples.
- Classify the result as Confirmed / Probable / Hypothesis / External limitation / Healthy —
  never a bare "good"/"bad".
- Persist aggregated run statistics (never raw packets) locally.

### Functional (future phases, not implemented)
- Bufferbloat test (latency under controlled, consented load).
- Per-adapter advanced properties (RSS, interrupt moderation, offloads) where the driver
  exposes them.
- Rule-based optimization engine with capture/apply/verify/rollback per change.
- Game-protection mode that suspends diagnostics while a game is foregrounded.
- Per-process bandwidth visibility where the Windows APIs expose it.

### Non-functional
- No telemetry, no cloud dependency; everything required to use the diagnostics works
  fully offline.
- No unbounded background polling: every probe run is a bounded batch the user (or later, a
  scheduled profile) explicitly started.
- No elevation requested in this phase. When later phases need it, it is scoped per-operation,
  never a permanently-elevated background service, unless a concrete capability proves that
  necessary.
- Every statistic shown states its target, sample count, and time window — never presented as
  a universal verdict on "internet quality."

## 3. Why WPF instead of WinUI 3 for this prototype

The original brief specifies WinUI 3 + the Windows App SDK. This development machine has
the .NET SDK but **no Visual Studio and no Windows App SDK workload installed**, and WinUI 3
desktop apps are difficult to get building reliably from the .NET SDK alone without it. Rather
than claim a WinUI 3 build that was never actually compiled, this prototype uses **WPF**, which:

- Ships inside the .NET SDK — no extra workload, confirmed to build and run in this environment.
- Supports the same MVVM pattern, the same XAML mental model, and the same project separation
  (`NetPulse.App` only hosts views/view-models; it contains no diagnostic or network logic).
- Is a fully supported, actively maintained desktop UI framework on current .NET.

If Visual Studio + the Windows App SDK workload become available, `NetPulse.App` can be
replaced with a WinUI 3 project without touching `Domain`, `Application`, `Infrastructure.Windows`,
or `Persistence` — those have zero dependency on the UI framework. This is a recorded technical
decision, not a silent scope cut.

## 4. Architecture

```
NetPulse.App  (WPF, MVVM)
    -> NetPulse.Application   (use cases, ports)
         -> NetPulse.Domain   (entities, pure logic, no OS/UI dependency)
    -> NetPulse.Infrastructure.Windows  (implements Application ports via NetworkInterface/Ping)
    -> NetPulse.Persistence   (implements Application ports via SQLite)
    -> NetPulse.PrivilegedAgent  (contract-only placeholder; no operations yet)
```

Dependency rule: `Domain` depends on nothing. `Application` depends only on `Domain`.
`Infrastructure.Windows`, `Persistence`, and `PrivilegedAgent` depend on `Application`/`Domain`
and implement the ports those layers declare. `App` is the only project allowed to reference
everything, and it contains no business logic of its own — only presentation.

This is verified structurally: `Domain`'s `.csproj` has zero `PackageReference`/`ProjectReference`
entries, so it cannot accidentally pull in WPF, SQLite, or Windows-only APIs.

### Project responsibilities actually present in `src/` and `tests/`

| Project | Responsibility | Depends on |
|---|---|---|
| `NetPulse.Domain` | `NetworkAdapterInfo`, `LatencySample`, `LatencyStatistics` (pure stats math), `DiagnosticFinding`/`DiagnosticConfidence`, `IOptimizationAction` contract (unused by any implementation yet) | — |
| `NetPulse.Application` | Ports (`INetworkAdapterInventoryService`, `ILatencyProbeService`, `IDiagnosticHistoryRepository`), `ConnectionDiagnosticUseCase` (orchestration + classification) | Domain |
| `NetPulse.Infrastructure.Windows` | `NetworkAdapterInventoryService` (via `System.Net.NetworkInformation.NetworkInterface`, read-only), `IcmpLatencyProbeService` (via `Ping`) | Domain, Application |
| `NetPulse.Persistence` | `SqliteDiagnosticHistoryRepository` — aggregated run history only, no raw packets | Domain, Application |
| `NetPulse.PrivilegedAgent` | Placeholder only (see its `README.md`) — no code | Domain, Application |
| `NetPulse.App` | WPF dashboard, `DashboardViewModel` (CommunityToolkit.Mvvm), DI wiring in `App.xaml.cs` | all of the above |
| `tests/NetPulse.UnitTests` | Real tests for `LatencyStatistics` and `ConnectionDiagnosticUseCase` (incl. classification thresholds), using fakes for the ports | Domain, Application |
| `tests/NetPulse.IntegrationTests`, `SafetyTests`, `PerformanceTests` | Scaffolded, empty. Populate starting Phase 5 once there are privileged operations and optimization rules to exercise. | varies |

## 5. Diagnostic flow (implemented)

1. User enters comma-separated targets and a sample count in the dashboard (defaults:
   `1.1.1.1, 8.8.8.8`, 20 samples).
2. `DashboardViewModel.RunDiagnosticAsync` builds a `DiagnosticOptions` and calls
   `ConnectionDiagnosticUseCase.RunAsync`.
3. For each target, the use case sends `SampleCount` ICMP probes (via the injected
   `ILatencyProbeService`), spaced by `IntervalBetweenSamples` (default 200 ms) — so a 20-sample
   run against one target takes about 4 seconds and sends 20 packets, not a flood.
4. `LatencyStatistics.From(...)` (pure `Domain` code, unit-tested, no I/O) computes loss/median/
   P95/jitter.
5. `ConnectionDiagnosticUseCase.Classify(...)` turns the statistics into a `DiagnosticFinding`
   with an explicit `DiagnosticConfidence`, following the thresholds in §6.
6. The result is persisted via `IDiagnosticHistoryRepository` (SQLite) and shown in the
   "Diagnostic findings" grid together with its stated limitations.

### Confidence thresholds (documented, not hidden in code)

| Condition | Confidence | Notes |
|---|---|---|
| 0 responses | Probable | Not "Confirmed" — ICMP can be filtered without the connection being down. |
| `SampleCount` < 10 | Hypothesis | Overrides every other check; too few samples to trust. |
| Packet loss ≥ 2% | Probable | States it may originate outside the PC. |
| Jitter (mean abs. delta between consecutive RTTs) > 30 ms | Probable | States that cause attribution needs a load test, not this alone. |
| None of the above | Healthy | States it only covers this target/window, not "the internet." |

These are starting thresholds for the prototype, not a claimed universal standard — Phase 4's
recommendation engine is expected to replace fixed cutoffs with historical-baseline and
variability-aware analysis per §3.4 of the brief.

## 6. Data model (implemented)

SQLite table `DiagnosticRuns` (created on first use by `SqliteDiagnosticHistoryRepository`):

```
Id, Target, SampleCount, RespondedCount, PacketLossPercent,
MinMs, MedianMs, P95Ms, MaxMs, JitterMs, WindowStart, WindowEnd
```

Only aggregated statistics are stored — no raw ICMP payloads, no Wi-Fi passwords, no
credentials. Database file: `%LOCALAPPDATA%\NetPulseOptimizer\netpulse.db`.

## 7. Security posture (this phase)

- The process runs with the user's normal (non-elevated) token. Nothing in this build
  requests or needs elevation.
- `NetworkAdapterInventoryService` only reads `NetworkInterface`/`IPInterfaceProperties` —
  no WMI writes, no registry writes, no `NetAdapter` cmdlets.
- `IcmpLatencyProbeService` only sends ICMP echo requests through the BCL `Ping` class — no
  raw sockets, no packet crafting.
- There is no code path anywhere in this solution that accepts a string and executes it as a
  shell command. `IOptimizationAction` (Domain) is designed specifically to prevent that
  pattern in future phases — the capability is a compiled, named class, not an arbitrary
  command.
- `NetPulse.PrivilegedAgent` contains no operations. Its eventual allow-list design is
  documented in its `README.md` and is deferred until a concrete optimization needs it.

## 8. What is explicitly deferred (not implemented, not stubbed-and-hidden)

- Any registry, TCP/IP, DNS, or adapter *write*.
- Bufferbloat active-load testing.
- The rule-based recommendation/optimization engine.
- The privileged agent's actual capabilities.
- Per-process bandwidth accounting.
- Game-protection mode.
- Installer/signing pipeline.
- WinUI 3 (pending Visual Studio + Windows App SDK workload availability — see §3).

Each of these needs its own design pass before code; none should be bolted on as a quick
addition to the current prototype.

## 9. How to build and run

```bash
dotnet build NetPulseOptimizer.slnx
dotnet test tests/NetPulse.UnitTests/NetPulse.UnitTests.csproj
dotnet run --project src/NetPulse.App/NetPulse.App.csproj
```

Verified in this environment: solution builds with 0 errors/0 warnings, 14/14 unit tests pass,
and the app launches and reaches the dashboard (`MainWindowTitle` confirmed via a running
process check).
