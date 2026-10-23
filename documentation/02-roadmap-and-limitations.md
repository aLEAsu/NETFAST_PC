# Roadmap and known limitations

## Phase status

| Phase (per brief §16) | Status |
|---|---|
| 1. Research & requirements | Done for this slice (see `01-architecture.md` §2) |
| 2. Architecture design | Done for this slice |
| 3. Diagnostics-only prototype | **Implemented** — adapter inventory, ICMP latency diagnostic, dashboard, local history |
| 4. Recommendation engine | **Implemented** — deterministic rules, risk levels, full descriptor metadata, dashboard panel. No change is simulated or applied yet; see below. |
| 5. Controlled modifications (privileged agent) | Not started — contract exists in `Domain.Optimization`, no implementation |
| 6. Security & performance validation | Not started — no FPS/CPU/RAM benchmarking harness exists yet |
| 7. Distribution (installer, signing) | Not started |

## Phase 4 scope note: "simulation of changes"

The brief's Phase 4 (§16) asks for a recommendation engine plus "simulación de cambios"
(change simulation). This build's rules never propose or preview an actual system change -
every rule either (a) recommends collecting more evidence, (b) recommends an external
diagnostic step (checking the router, running a load test that does not exist yet), or
(c) explicitly recommends no change. Each still carries the full
`OptimizationDescriptor` contract (risk, preconditions, verification method, success/failure
criteria, rollback method, limitations) so the UI and the user see everything a real,
Phase-5 `IOptimizationAction` would have to declare - that metadata-level preview **is** the
"simulation" this phase ships. No rule here can execute anything: `RollbackMethod` on every
current rule reads "Not applicable" because nothing is ever applied. A rule that proposes an
actual Windows setting change is deliberately deferred to Phase 5, once there is a reviewed
`IOptimizationAction` implementation behind it.

## Known limitations of the current build

- **UI framework substitution.** WPF is used instead of WinUI 3 because this machine has no
  Visual Studio / Windows App SDK workload. See `01-architecture.md` §3 for the reasoning and
  the migration path. This does not affect `Domain`/`Application`/`Infrastructure.Windows`/
  `Persistence`, which have no UI dependency.
- **ICMP only.** The latency probe uses ICMP echo (`Ping`). It does not establish a TCP
  connection or measure DNS resolution time yet, so it cannot by itself distinguish "ICMP
  blocked" from "application traffic also fails." The classification logic already states this
  limitation on every finding where it matters.
- **No bufferbloat test.** There is no load-generation code, so latency-under-load cannot yet
  be measured; §6 of the brief (bufferbloat) is unimplemented.
- **No adapter write operations.** Advanced NIC properties (RSS, interrupt moderation, offloads,
  EEE) are neither read nor written in this phase — only the read-only basics exposed by
  `NetworkInterface` are surfaced.
- **Fixed classification thresholds.** The 2% loss / 30 ms jitter / 10-sample cutoffs in
  `ConnectionDiagnosticUseCase.Classify` are explicit starting points, not a validated standard.
  They should be revisited once real historical data exists.
- **No performance benchmarking yet.** The brief's FPS/CPU/RAM validation protocol (§11–12) has
  not been run against this build. The resident memory observed during a manual smoke test on
  this machine was noticeably above the brief's informal ~150 MB reference figure for a typical
  .NET WPF process at startup — expected for a non-trimmed Debug build, but it means the
  resource-usage claims in the original brief are **not yet verified** for this codebase and
  must not be repeated as fact until measured with PresentMon/dotnet-counters per §11.
- **No installer, no signing.** Phase 7 work is entirely unstarted.
- **Test coverage.** `NetPulse.UnitTests` has 14 fake-based tests covering `LatencyStatistics`
  and `ConnectionDiagnosticUseCase`. `NetPulse.IntegrationTests` now has 21 tests that exercise
  the **real** `NetworkAdapterInventoryService`, `IcmpLatencyProbeService`,
  `SqliteDiagnosticHistoryRepository`, and the full pipeline wired together — no fakes, real
  adapters, real ICMP traffic (loopback + the RFC 5737 `203.0.113.0/24` reserved block for a
  guaranteed non-response), and a real temp SQLite file, with cleanup in `Dispose()`.
  `SafetyTests` and `PerformanceTests` are still scaffolded but empty — there is nothing yet in
  `PrivilegedAgent` or performance-sensitive code that justifies populating them.
- **Real bug found and fixed by the integration tests.** The loopback adapter reports MTU as
  `-1` through `IPInterfaceProperties.GetIPv4Properties().Mtu` instead of throwing or returning
  a sane value. `NetworkAdapterInventoryService.SafeMtu` now clamps any non-positive result to
  `0` (the project's existing "unknown" marker), so the dashboard never shows a negative MTU.
  This is exactly the kind of thing fakes would never have caught.
- **Recommendation rules are fixed thresholds, not learned baselines.** `HighMedianLatencyRule`
  (100 ms), `HighJitterBufferbloatFollowUpRule` (30 ms), and `PacketLossInvestigateRule` (2%)
  reuse/mirror the same starting cutoffs as `ConnectionDiagnosticUseCase.Classify`. They are a
  reasonable default, not a validated standard - see the note above about fixed classification
  thresholds.
- **No rule ever recommends a Windows setting change.** All five current rules
  (`InsufficientSamplesRule`, `PacketLossInvestigateRule`, `HighJitterBufferbloatFollowUpRule`,
  `HighMedianLatencyRule`, `HealthyNoActionRule`) only recommend gathering more evidence,
  investigating outside this PC, or doing nothing. This is intentional for Phase 4; see the
  scope note above.

## Immediate next steps (suggested, not started)

1. Add a TCP-connect and DNS-resolution probe alongside ICMP, so loss classification can
   distinguish "no ICMP reply" from "no application-level connectivity."
2. Design and review the first 1–2 `IOptimizationAction` implementations (e.g. a DNS-server
   recommendation) with their full capture/apply/verify/rollback contract, then add the first
   recommendation rule that actually proposes one of them (Phase 5).
3. Run the brief's FPS/network validation protocol (§11–12) once there is an actual
   optimization to measure, rather than against the diagnostics-only build.
4. Once `PrivilegedAgent` gains real capabilities, populate `SafetyTests` (rollback correctness,
   capability allow-list enforcement) and `PerformanceTests` (resource usage under repeated
   diagnostic runs).
