# Roadmap and known limitations

## Phase status

| Phase (per brief §16) | Status |
|---|---|
| 1. Research & requirements | Done for this slice (see `01-architecture.md` §2) |
| 2. Architecture design | Done for this slice |
| 3. Diagnostics-only prototype | **Implemented** — adapter inventory, ICMP latency diagnostic, dashboard, local history |
| 4. Recommendation engine | **Implemented** — deterministic rules, risk levels, full descriptor metadata, dashboard panel. No change is simulated or applied yet; see below. |
| 5. Controlled modifications (privileged agent) | **Implemented, write path unverified** — first real `IOptimizationAction` (`SetAdapterDnsServersAction`), fixed capability allow-list, per-operation elevation via a separate helper process, audit logging. The actual elevated write has never been executed - see `01-architecture.md` §10. |
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

## Phase 5 scope note: what "implemented" means here

Every piece of Phase 5 compiles, is wired together correctly, and is covered by real tests
**except the one step that actually changes the machine**. Specifically:

- Validation, the action's Check/Capture/Apply/Verify/Rollback sequencing, the audit log, and
  the read-only WMI query are all verified by real tests (81 unit + 28 integration, see
  `01-architecture.md` §9–§10).
- Launching the elevated helper with a real UAC prompt, and the helper's actual
  `SetDNSServerSearchOrder` call, have **never been run** - not by a test, not manually, not
  once - by the session that built this. That is not an oversight to close with "just run it
  once to check": an automated coding session must not perform a real, UAC-gated system
  modification, with or without standing authorization to do so. The first real run of that
  path has to be a person, deliberately, from the finished application.
- There is also no UI entry point yet that would let a person trigger
  `SetAdapterDnsServersAction` from the dashboard, and no deployment step that copies
  `NetPulse.PrivilegedAgent.Host.exe` next to `NetPulse.App.exe` - both are explicitly left for
  the next increment (see "Immediate next steps" below), partly so that wiring isn't rushed
  into place just to make this one action "fully" usable before it has had a first manual
  trial run.

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
- **No rule ever recommends a Windows setting change.** All five Phase 4 rules
  (`InsufficientSamplesRule`, `PacketLossInvestigateRule`, `HighJitterBufferbloatFollowUpRule`,
  `HighMedianLatencyRule`, `HealthyNoActionRule`) only recommend gathering more evidence,
  investigating outside this PC, or doing nothing. Phase 5's `SetAdapterDnsServersAction` is
  not yet reachable from any Phase 4 rule or from the UI - the two are not cross-linked yet.
- **The elevated write path is unverified.** See the Phase 5 scope note above. Do not treat
  `SetAdapterDnsServersAction.ApplyAsync`/`RollbackAsync` as proven to work against a real
  adapter until a person has run them once, deliberately, and watched the result.
- **No deployment step for the elevated helper.** `ElevatedHelperOperationLauncher` expects
  `NetPulse.PrivilegedAgent.Host.exe` next to `NetPulse.App.exe`; nothing copies it there yet
  (see `01-architecture.md` §10's "Known gap: helper deployment").
- **Only one capability exists.** `PrivilegedCapability` has exactly one member
  (`SetAdapterDnsServers`). Adding a second (e.g. an MTU or adapter-property change) means
  adding an enum case, a new `IOptimizationAction`, and a new case in
  `NetPulse.PrivilegedAgent.Host`'s `Run` switch - never a generic "run this with these args"
  escape hatch.

## Immediate next steps (suggested, not started)

1. Add a TCP-connect and DNS-resolution probe alongside ICMP, so loss classification can
   distinguish "no ICMP reply" from "no application-level connectivity."
2. Wire a UI entry point (a button, likely next to the recommendations grid) that lets a
   person actually trigger `SetAdapterDnsServersAction` and, critically, perform the **first
   real manual verification** of the elevated write path described in `01-architecture.md` §10.
3. Add the deployment step (post-build copy, or installer work in Phase 7) that places
   `NetPulse.PrivilegedAgent.Host.exe` next to `NetPulse.App.exe`.
4. Once step 2 has been run manually at least once, populate `NetPulse.SafetyTests` with
   whatever that manual run revealed needs automated coverage (rollback correctness,
   capability allow-list enforcement at the host's argument boundary).
5. Run the brief's FPS/network validation protocol (§11–12) once there is a verified
   optimization to measure, rather than against the diagnostics-only build.
6. Add a second recommendation rule that actually proposes `SetAdapterDnsServersAction`,
   closing the gap between Phase 4 and Phase 5.
