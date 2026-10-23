# NetPulse Optimizer — Technical Design

Status: **Phases 3-7 implemented and building**, plus a post-Phase-7 completion pass (TCP/DNS
probes, Profiles, and UI wiring for the one real action). See
`02-roadmap-and-limitations.md` for the phase-by-phase table and
`05-code-audit-and-recommendations.md` for the latest full audit - both are more current than
some of the section-level detail below, which is left in place as a historical record of each
phase's own state rather than continuously rewritten. `SetAdapterDnsServersAction`'s write
path has **never been executed against a real machine** by any automated session; see §10 -
that has not changed even though it is now UI-reachable. Everything below that is not
reflected in `src/` is a proposal for later work, labeled as such.

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
    -> NetPulse.PrivilegedAgent  (SetAdapterDnsServersAction; orchestrates elevation, never performs it itself)
         -> NetPulse.PrivilegedAgent.Host  (separate elevated process, launched per-operation; §10)
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
| `NetPulse.Persistence` | `SqliteDiagnosticHistoryRepository` (run history), `SqlitePrivilegedOperationAuditLog` (audit trail) — aggregated data only, no raw packets, no credentials | Domain, Application |
| `NetPulse.PrivilegedAgent` | `SetAdapterDnsServersAction` — the first real `IOptimizationAction`; never writes anything itself, only orchestrates the ports | Domain, Application |
| `NetPulse.PrivilegedAgent.Host` | Separate elevated console executable; the ONLY code in the solution that calls `SetDNSServerSearchOrder` | Domain only |
| `NetPulse.App` | WPF dashboard, `DashboardViewModel` (CommunityToolkit.Mvvm), DI wiring in `App.xaml.cs` | all of the above |
| `tests/NetPulse.UnitTests` | Real tests for `LatencyStatistics`, `ConnectionDiagnosticUseCase`, `DiagnosticOptions`, all 5 recommendation rules, `RecommendationEngine`, `GenerateRecommendationsUseCase`, both privileged-operation validators, and `SetAdapterDnsServersAction` — 88 tests, all fake/pure-input based | Domain, Application, PrivilegedAgent |
| `tests/NetPulse.IntegrationTests` | Real tests against the actual adapter stack, real ICMP, a real SQLite file, and a real read-only WMI query — no fakes, 28 tests (see §9) | Domain, Application, Infrastructure.Windows, Persistence |
| `tests/NetPulse.SafetyTests` | Real tests for the full Check/Capture/Apply/Verify/Rollback recovery lifecycle against fakes (5 tests, see Phase 6 in `02-roadmap-and-limitations.md`). The elevated write path itself still needs to be verified manually, by a person - see §10. | Domain, Application, PrivilegedAgent |
| `tests/NetPulse.PerformanceTests` | Real CPU/allocation benchmarks for the diagnostic pipeline with network time isolated out (4 tests; see `03-phase6-security-performance.md`) | Domain, Application, Infrastructure.Windows |

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

## 7. Security posture

- `NetPulse.App`, `NetPulse.Domain`, `NetPulse.Application`, `NetPulse.Infrastructure.Windows`,
  and `NetPulse.Persistence` all run with the user's normal (non-elevated) token and request
  no elevation. Only `NetPulse.PrivilegedAgent.Host` ever runs elevated, and only for the
  single operation it was launched for (§10).
- `NetworkAdapterInventoryService` and `WmiAdapterDnsConfigurationReader` are read-only - no
  WMI writes, no registry writes, no `NetAdapter` cmdlets, anywhere in the unelevated app's
  dependency graph.
- `IcmpLatencyProbeService` only sends ICMP echo requests through the BCL `Ping` class — no
  raw sockets, no packet crafting.
- There is no code path anywhere in this solution that accepts a string and executes it as a
  shell command. The one process boundary that does launch another process
  (`ElevatedHelperOperationLauncher`) builds its child process's arguments from individually
  validated, typed values (`AdapterIdValidator`, `DnsServerAddressValidator`), never by
  concatenating free-form text, and the receiving end (`NetPulse.PrivilegedAgent.Host`)
  re-validates every argument again rather than trusting the caller.
- The capability surface is fixed at compile time (`PrivilegedCapability` enum, currently one
  member). There is no plugin loader, no way to register a new capability from configuration
  or the UI, and no way to pass an arbitrary executable name or argument list to the elevated
  helper.
- Every privileged-operation attempt - Check, Capture, Apply, Verify, Rollback, successful or
  not - is written to the local `PrivilegedOperationAudit` SQLite table via
  `IPrivilegedOperationAuditLog`, never transmitted anywhere.

## 8. What is explicitly deferred (not implemented, not stubbed-and-hidden)

- A second or third `IOptimizationAction` beyond the one DNS action (Phase 5 shipped exactly
  one, deliberately, to prove the pattern rather than rushing several).
- Wiring a recommendation rule (Phase 4) to actually *offer* running `SetAdapterDnsServersAction`
  from the dashboard - today the two phases are not cross-linked; see §10.
- Bufferbloat active-load testing.
- Per-process bandwidth accounting.
- Game-protection mode.
- Installer/signing pipeline (which is also what would deploy `NetPulse.PrivilegedAgent.Host.exe`
  alongside the main app - see §10's known gap on that).
- WinUI 3 (pending Visual Studio + Windows App SDK workload availability — see §3).

Each of these needs its own design pass before code; none should be bolted on as a quick
addition to the current prototype.

## 9. Integration tests against the real adapter stack

`tests/NetPulse.IntegrationTests` deliberately contains **no fakes**. It exercises:

- `NetworkAdapterInventoryService` on the real machine (loopback is always present, so the
  suite is deterministic even on a runner with no physical connectivity).
- `IcmpLatencyProbeService` with real ICMP traffic: `127.0.0.1` for a guaranteed reply, and
  `203.0.113.1` (RFC 5737 TEST-NET-3, reserved/non-routable) for a guaranteed non-reply —
  chosen specifically so the suite never depends on a third-party host being reachable.
- `SqliteDiagnosticHistoryRepository` and `SqlitePrivilegedOperationAuditLog` against real
  temporary `.db` files (round trip, filtering, ordering, concurrent writes), cleaned up in
  each test class's `Dispose()`.
- `ConnectionDiagnosticUseCase` wired end-to-end with the three diagnostic services — the
  same wiring `App.xaml.cs` uses, minus WPF.
- `WmiAdapterDnsConfigurationReader` — a real, read-only WMI query against this machine's
  adapters (see §10 for why there is no equivalent test of the *write* path).

These tests found a real defect: the loopback adapter reports MTU as `-1` instead of a sane
value or an exception. `NetworkAdapterInventoryService.SafeMtu` now normalizes any
non-positive MTU to `0`. This is the kind of bug fake-based unit tests cannot surface by
construction — it is the reason this suite exists.

## 10. Privileged agent and elevation boundary (Phase 5)

### Decision: per-operation elevation, no persistent service

The brief (§9) asks for a decision between point-in-time elevation and an isolated service,
and says not to build a permanent privileged service without a demonstrated need. This build
chooses **per-operation elevation**: `NetPulse.PrivilegedAgent.Host` is a separate, minimal
console executable that:

- Is launched fresh for exactly one operation and exits immediately after.
- Requests elevation via its own manifest (`requestedExecutionLevel level="requireAdministrator"`),
  so Windows shows a UAC prompt the user must personally approve - there is no way to suppress
  or pre-answer it from this codebase.
- Has no network listener, no named pipe, no IPC surface that stays open between runs. It reads
  four command-line arguments, does one validated thing, writes one JSON result file, and exits.
- Depends on nothing but `NetPulse.Domain` (for the same validators the caller already ran) and
  `System.Management`. It does not reference `NetPulse.Application`, `NetPulse.Infrastructure.Windows`,
  or `NetPulse.Persistence` - the smaller its dependency graph, the smaller its attack surface.

No part of this design keeps an elevated process running in the background. There is no
persistent privileged service anywhere in this solution.

### The flow, end to end

```
NetPulse.App (unelevated)
  -> SetAdapterDnsServersAction.ApplyAsync
       -> IElevatedOperationLauncher.ExecuteAsync(PrivilegedOperationRequest)
            -> ElevatedHelperOperationLauncher (Infrastructure.Windows, unelevated)
                 - re-validates adapterId and DNS servers
                 - builds a result-file path under %LOCALAPPDATA%\NetPulseOptimizer\elevated-results\
                 - Process.Start(NetPulse.PrivilegedAgent.Host.exe, Verb="runas", <4 validated args>)
                   === UAC prompt; the user approves or declines ===
                 -> NetPulse.PrivilegedAgent.Host (elevated, if approved)
                      - re-validates every argument again, independently
                      - calls Win32_NetworkAdapterConfiguration.SetDNSServerSearchOrder
                      - writes {"success":.., "output":.., "error":..} to the result file, exits
            <- reads the result file, deletes it, returns PrivilegedOperationResult
       <- logs the attempt to PrivilegedOperationAudit (Persistence), maps to OptimizationResult
```

`CaptureAsync` and `VerifyAsync` only ever call `IAdapterDnsConfigurationReader`
(`WmiAdapterDnsConfigurationReader`), which is read-only and runs unelevated - capturing the
state to roll back to, and checking whether a change took effect, never themselves require
elevation.

### What has and has not been verified

| Step | Verified how |
|---|---|
| `AdapterIdValidator`, `DnsServerAddressValidator` | Real unit tests, many adversarial inputs (SQL-injection-shaped strings, shell-metacharacter-shaped strings, hostnames, out-of-range IPs) |
| `SetAdapterDnsServersAction` (all 5 contract methods) | Real unit tests against fakes for every dependency |
| `WmiAdapterDnsConfigurationReader` (read) | Real integration test against this machine's real adapter via WMI - read-only |
| `SqlitePrivilegedOperationAuditLog` | Real integration test, real temp SQLite file |
| `ElevatedHelperOperationLauncher` launching the real host with real `runas` elevation | **Not run.** Doing so would show a real UAC prompt and, if approved, make a real DNS change on this machine. The coding agent that built this is not permitted to perform that action even with standing authorization - only a person, deliberately, running the finished application, can approve that UAC prompt. |
| `NetPulse.PrivilegedAgent.Host`'s actual `SetDNSServerSearchOrder` call | **Not run**, for the same reason. |

This is not a gap to "get around" later with a different kind of test - an elevated, real
system-modifying action verified by an unattended coding session defeats the purpose of
requiring a UAC prompt at all. **As of the post-Phase-7 completion pass, the UI entry point
exists**: `DashboardViewModel.ApplyDnsRecommendationAsync`, reached by selecting an adapter
and clicking "Apply suggested DNS servers" once the `SlowDnsResolutionRule` recommendation
fires (§5a). The correct verification is: build the solution, run it as a normal user,
select an adapter, trigger that command, and personally watch the confirmation dialog, the
UAC prompt, the result, and the rollback button. No automated session should do this.

### Helper deployment: resolved by the Phase 7 installer

`ElevatedHelperOperationLauncher` looks for `NetPulse.PrivilegedAgent.Host.exe` next to the
running `NetPulse.App.exe` (`AppContext.BaseDirectory`). The two projects still build to
separate `bin/` folders during normal development, but `installer/build.ps1` (Phase 7, see
`documentation/04-phase7-distribution.md`) publishes both and merges them into one payload
directory before packaging - an MSI-installed copy of the app has the helper sitting right
next to it, verified by checking `Test-Path` on the installed helper after a real install.
Running either executable straight from its own `bin\Debug\...` folder during development
still won't find the other one there; that is expected, not a regression.

## 11. How to build and run

```bash
dotnet build NetPulseOptimizer.slnx
dotnet test tests/NetPulse.UnitTests/NetPulse.UnitTests.csproj
dotnet test tests/NetPulse.IntegrationTests/NetPulse.IntegrationTests.csproj
dotnet run --project src/NetPulse.App/NetPulse.App.csproj
```

Verified in this environment: solution builds with 0 errors/0 warnings, 104/104 unit tests pass,
35/35 integration tests pass against the real adapter stack/ICMP/TCP/DNS/SQLite/WMI (read-only), 5/5
safety-lifecycle tests pass, 4/4 performance benchmarks pass, and the app launches and reaches
the dashboard (`MainWindowTitle` confirmed via a running process check, idle CPU/RAM measured
in `03-phase6-security-performance.md`). Not verified: the elevated write path described in
§10, and the FPS/game-impact protocol (no game/PresentMon available in this environment).
