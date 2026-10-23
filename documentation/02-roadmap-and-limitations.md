# Roadmap and known limitations

## Phase status

| Phase (per brief §16) | Status |
|---|---|
| 1. Research & requirements | Done for this slice (see `01-architecture.md` §2) |
| 2. Architecture design | Done for this slice |
| 3. Diagnostics-only prototype | **Implemented** — adapter inventory, ICMP latency diagnostic, dashboard, local history |
| 4. Recommendation engine | Not started |
| 5. Controlled modifications (privileged agent) | Not started — contract exists in `Domain.Optimization`, no implementation |
| 6. Security & performance validation | Not started — no FPS/CPU/RAM benchmarking harness exists yet |
| 7. Distribution (installer, signing) | Not started |

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
- **Test coverage.** Only `NetPulse.UnitTests` has real tests (14, covering `LatencyStatistics`
  and `ConnectionDiagnosticUseCase`). `IntegrationTests`, `SafetyTests`, and `PerformanceTests`
  are scaffolded but empty — there is nothing yet in `Infrastructure.Windows`, `PrivilegedAgent`,
  or performance-sensitive code that justifies populating them.

## Immediate next steps (suggested, not started)

1. Add a TCP-connect and DNS-resolution probe alongside ICMP, so loss classification can
   distinguish "no ICMP reply" from "no application-level connectivity."
2. Flesh out `NetPulse.IntegrationTests` against the real `NetworkAdapterInventoryService` and
   `IcmpLatencyProbeService` (these need a live network, so keep them separate from the
   fake-based unit tests).
3. Design and review the first 1–2 `IOptimizationAction` implementations (e.g. a DNS-server
   recommendation) with their full capture/apply/verify/rollback contract before writing any
   code that touches the registry or adapter settings.
4. Run the brief's FPS/network validation protocol (§11–12) once there is an actual
   optimization to measure, rather than against the diagnostics-only build.
