# NetPulse Optimizer — Technical Design

Status: **Phases 3 and 4 (diagnostics + recommendation engine) implemented and building.**
No optimization is actually applied, no privileged operation exists, and no system/network
modification exists in this codebase yet - every recommendation is informational only.
Everything below that is not reflected in `src/` is a proposal for later phases, labeled as such.

## 1. Executive summary

NetPulse Optimizer is a Windows desktop application that diagnoses, measures, and —
in later phases, under explicit user authorization — optimizes a PC's network path. The
current build enumerates network adapters, runs bounded ICMP latency samples against
user-chosen targets, computes median/P95/jitter/loss, classifies each result with an
explicit confidence level, stores the aggregated result locally in SQLite, and runs a fixed
set of deterministic rules over the result to produce risk-labeled, evidence-backed
recommendations. It changes nothing on the machine - recommendations are informational only.

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
| `NetPulse.Domain` | `NetworkAdapterInfo`, `LatencySample`, `LatencyStatistics` (pure stats math), `DiagnosticFinding`/`DiagnosticConfidence`, `IOptimizationAction` contract (unused by any implementation yet), `IRecommendationRule`/`RecommendationEngine`/`DefaultRecommendationRules` + 5 rule implementations (§5a) | — |
| `NetPulse.Application` | Ports (`INetworkAdapterInventoryService`, `ILatencyProbeService`, `IDiagnosticHistoryRepository`), `ConnectionDiagnosticUseCase` (orchestration + classification), `GenerateRecommendationsUseCase` (thin adapter to the Domain engine) | Domain |
| `NetPulse.Infrastructure.Windows` | `NetworkAdapterInventoryService` (via `System.Net.NetworkInformation.NetworkInterface`, read-only), `IcmpLatencyProbeService` (via `Ping`) | Domain, Application |
| `NetPulse.Persistence` | `SqliteDiagnosticHistoryRepository` — aggregated run history only, no raw packets | Domain, Application |
| `NetPulse.PrivilegedAgent` | Placeholder only (see its `README.md`) — no code | Domain, Application |
| `NetPulse.App` | WPF dashboard, `DashboardViewModel` (CommunityToolkit.Mvvm), DI wiring in `App.xaml.cs` | all of the above |
| `tests/NetPulse.UnitTests` | Real tests for `LatencyStatistics`, `ConnectionDiagnosticUseCase` (incl. classification thresholds), all 5 recommendation rules, `RecommendationEngine`, and `GenerateRecommendationsUseCase` — 46 tests, all fake/pure-input based | Domain, Application |
| `tests/NetPulse.IntegrationTests` | Real tests against the actual adapter stack, real ICMP, and a real SQLite file — no fakes (see §9a) | Domain, Application, Infrastructure.Windows, Persistence |
| `tests/NetPulse.SafetyTests`, `PerformanceTests` | Scaffolded, empty. Populate starting Phase 5 once there are privileged operations and optimization rules to exercise. | varies |

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

These are starting thresholds for the prototype, not a claimed universal standard. Phase 4's
recommendation engine (§5a) currently mirrors the same kind of fixed cutoffs rather than
historical-baseline analysis — replacing them with variability-aware, learned baselines per
§3.4 of the brief is still future work, tracked in `02-roadmap-and-limitations.md`.

## 5a. Recommendation engine (implemented, Phase 4)

`NetPulse.Domain.Recommendations` defines the rule contract and engine; `NetPulse.Application`
only adapts between the diagnostic use case's output and the engine's input.

1. After `ConnectionDiagnosticUseCase.RunAsync` returns one `DiagnosticRunResult` per target,
   `DashboardViewModel.RunDiagnosticAsync` calls `GenerateRecommendationsUseCase.Run(results)`.
2. That use case unwraps each result into `(LatencyStatistics, DiagnosticFinding)` and calls
   `RecommendationEngine.Evaluate(...)` once per target.
3. The engine runs every rule in `DefaultRecommendationRules.Create()` against that one target's
   evidence and collects whichever rules return a non-null `TriggeredRecommendation`. Rules are
   plain compiled classes - there is no plugin loader and no way to add a rule without a code
   review and a rebuild.
4. Each `TriggeredRecommendation` carries the firing rule's full `OptimizationDescriptor`
   (objective, evidence, preconditions, risk, required permissions, verification/success/failure
   criteria, rollback method, reference docs, known limitations) plus the specific
   `TriggerExplanation` text for that run. The dashboard's "Recommendations" grid shows all of it.

### The five rules shipped in this build

| Rule | Fires when | Ever proposes a change? |
|---|---|---|
| `InsufficientSamplesRule` | `DiagnosticConfidence.Hypothesis` (too few samples) | No — only "collect more samples" |
| `PacketLossInvestigateRule` | Loss ≥ 2% and confidence ≠ Hypothesis | No — only "investigate router/cable/ISP first" |
| `HighJitterBufferbloatFollowUpRule` | Jitter > 30 ms and confidence ≠ Hypothesis | No — only "run a bufferbloat load test" (not implemented yet) |
| `HighMedianLatencyRule` | Median RTT > 100 ms and confidence ≠ Hypothesis | No — only "compare wired vs. Wi-Fi / closer target" |
| `HealthyNoActionRule` | `DiagnosticConfidence.Healthy` | No — explicitly "no change recommended" |

No rule in this build can fire and propose an actual registry, TCP/IP, DNS, or adapter change,
because no such `IOptimizationAction` implementation exists yet (Phase 5). Every rule's
`RollbackMethod` reads "Not applicable" for exactly that reason. See
`02-roadmap-and-limitations.md` for what the brief's Phase 4 "simulación de cambios" means in
this build versus what is still deferred to Phase 5.

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

## 9a. Integration tests against the real adapter stack

`tests/NetPulse.IntegrationTests` deliberately contains **no fakes**. It exercises:

- `NetworkAdapterInventoryService` on the real machine (loopback is always present, so the
  suite is deterministic even on a runner with no physical connectivity).
- `IcmpLatencyProbeService` with real ICMP traffic: `127.0.0.1` for a guaranteed reply, and
  `203.0.113.1` (RFC 5737 TEST-NET-3, reserved/non-routable) for a guaranteed non-reply —
  chosen specifically so the suite never depends on a third-party host being reachable.
- `SqliteDiagnosticHistoryRepository` against a real temporary `.db` file (round trip,
  filtering, ordering, concurrent writes), cleaned up in each test class's `Dispose()`.
- `ConnectionDiagnosticUseCase` wired end-to-end with the three services above — the same
  wiring `App.xaml.cs` uses, minus WPF.

These tests found a real defect: the loopback adapter reports MTU as `-1` instead of a sane
value or an exception. `NetworkAdapterInventoryService.SafeMtu` now normalizes any
non-positive MTU to `0`. This is the kind of bug fake-based unit tests cannot surface by
construction — it is the reason this suite exists.

## 9. How to build and run

```bash
dotnet build NetPulseOptimizer.slnx
dotnet test tests/NetPulse.UnitTests/NetPulse.UnitTests.csproj
dotnet test tests/NetPulse.IntegrationTests/NetPulse.IntegrationTests.csproj
dotnet run --project src/NetPulse.App/NetPulse.App.csproj
```

Verified in this environment: solution builds with 0 errors/0 warnings, 46/46 unit tests pass,
21/21 integration tests pass against the real adapter stack/ICMP/SQLite, and the app launches
and reaches the dashboard (`MainWindowTitle` confirmed via a running process check).
