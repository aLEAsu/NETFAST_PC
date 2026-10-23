# Roadmap and known limitations

## Phase status

| Phase (per brief §16) | Status |
|---|---|
| 1. Research & requirements | Done for this slice (see `01-architecture.md` §2) |
| 2. Architecture design | Done for this slice |
| 3. Diagnostics-only prototype | **Implemented** — adapter inventory, ICMP latency diagnostic, dashboard, local history |
| 4. Recommendation engine | **Implemented** — deterministic rules, risk levels, full descriptor metadata, dashboard panel. No change is simulated or applied yet; see below. |
| 5. Controlled modifications (privileged agent) | **Implemented, write path unverified** — first real `IOptimizationAction` (`SetAdapterDnsServersAction`), fixed capability allow-list, per-operation elevation via a separate helper process, audit logging. The actual elevated write has never been executed - see `01-architecture.md` §10. |
| 6. Security & performance validation | **Done for what's measurable here** — security audit, real idle CPU/RAM measurement, diagnostic-pipeline CPU benchmarks, traffic-bound validation, one real regression found and fixed. FPS/game-impact protocol not run (no game/PresentMon available). See `03-phase6-security-performance.md`. |
| 7. Distribution (installer, signing) | **Implemented and tested** — real WiX MSI, per-user install (no UAC needed to install/uninstall), verified install/upgrade/uninstall cycle, signing procedure established and exercised with a self-signed test certificate. No CA-issued certificate exists. See `04-phase7-distribution.md`. |

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

## Phase 5 verification pass (post-implementation review)

Before starting Phase 6, Phase 5's logic, structure, and integration were reviewed end to
end (not just "it compiles and the happy-path tests pass"). This found and fixed four real
issues, all now covered by regression tests:

1. **Rollback-to-automatic-DNS was impossible.** `DnsServerAddressValidator` rejected empty
   lists. But `CaptureAsync` on an adapter using DHCP-assigned DNS (the common case - most
   adapters have no static DNS configured) captures an empty list. `RollbackAsync` would
   then try to send that empty list through the same validator and get rejected, meaning the
   single most common starting configuration could never be rolled back to. Fixed: an empty
   list is now valid and means "clear the static override, use DHCP-assigned DNS" -
   documented in the validator's own doc comment, not just in this file.
2. **Complementary WMI fix.** `SetDNSServerSearchOrder` is documented to clear the static
   override when given `null`, not necessarily when given an empty array. `NetPulse.PrivilegedAgent.Host`
   now passes `null` explicitly for the empty-list case rather than relying on an empty
   array behaving the same way.
3. **`VerifyAsync` could report a successful change as inconclusive.** It compared DNS server
   lists as raw strings (`SequenceEqual`). WMI can hand back an IPv6 address in a different
   but equal canonical form (e.g. `::1` vs `0:0:0:0:0:0:0:1`) than the one requested, which
   would fail a textual comparison even though the change took effect exactly as asked -
   directly undermining the brief's "never claim success/failure without a real check"
   principle, just in the failure direction. Fixed: comparison now parses both sides to
   `IPAddress` and compares by value, in order.
4. **An unhandled exception could escape `ElevatedHelperOperationLauncher`.** If the elevated
   helper's result file were missing, truncated, or malformed JSON (e.g. the process was
   killed mid-write), `JsonSerializer.Deserialize` or the file read could throw a type the
   existing `catch` block didn't cover, propagating an unhandled exception up through
   `SetAdapterDnsServersAction.ApplyAsync`. Fixed: a broad catch now converts any such failure
   into a clean `Failed` result, while still letting `OperationCanceledException` propagate
   as cancellation (not an error).

A fifth issue was a minor robustness fix rather than a correctness bug:
`WmiAdapterDnsConfigurationReader.GetDnsServersAsync` ran its blocking WMI query synchronously
behind a `Task`-typed signature; it now runs on the thread pool via `Task.Run` so a future UI
caller's thread isn't blocked for the duration of the query.

81 unit tests grew to 83 (two new regression tests); 28 integration tests are unaffected and
still pass. None of this touches the still-unverified elevated write path itself (see the
scope note below) - these are logic fixes in code that was already reachable by real tests,
found by reading the code again deliberately rather than by running it.

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
- **No CA-issued code-signing certificate.** The installer and both executables are
  signed only with a self-signed test certificate (see `04-phase7-distribution.md` §5) -
  this proves the signing procedure works, not that any real machine would trust the
  publisher. A real distribution needs a certificate purchased from a trusted CA.
- **Test coverage (current).** `NetPulse.UnitTests`: 88 fake/pure-input tests (Domain +
  Application + PrivilegedAgent logic). `NetPulse.IntegrationTests`: 28 tests against the
  **real** adapter stack, ICMP, SQLite, and a real read-only WMI query — no fakes.
  `NetPulse.SafetyTests`: 5 tests covering the full recovery lifecycle against fakes.
  `NetPulse.PerformanceTests`: 4 real, executed CPU/allocation benchmarks. See
  `01-architecture.md` §9–§11 and `03-phase6-security-performance.md` for what each suite
  actually covers and what it deliberately does not (the elevated write path, FPS impact).
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
- **Elevated helper deployment: resolved by the installer.** `ElevatedHelperOperationLauncher`
  expects `NetPulse.PrivilegedAgent.Host.exe` next to `NetPulse.App.exe`; as of Phase 7,
  `installer/build.ps1` publishes both and merges them into one payload directory before
  packaging, so an MSI-installed copy of the app has the helper right next to it. Running
  either executable directly from its own `bin/` folder during development still won't have
  this (see `01-architecture.md` §10) - only the installed, or manually merged, output does.
- **Only one capability exists.** `PrivilegedCapability` has exactly one member
  (`SetAdapterDnsServers`). Adding a second (e.g. an MTU or adapter-property change) means
  adding an enum case, a new `IOptimizationAction`, and a new case in
  `NetPulse.PrivilegedAgent.Host`'s `Run` switch - never a generic "run this with these args"
  escape hatch.

## Phase 7 outcome note

Phase 7 is done and real (see `04-phase7-distribution.md`): a WiX MSI that installs
per-user with no UAC prompt, a working install/upgrade/uninstall cycle verified by actually
running it, and a signing procedure exercised with a self-signed test certificate. It also
closed a previously-open gap: `installer/build.ps1` now deploys
`NetPulse.PrivilegedAgent.Host.exe` next to `NetPulse.App.exe`, which step 3 below used to
ask for. What it did **not** produce is a certificate a real end user's Windows would trust -
that needs a CA purchase, which is the user's own step.

## Immediate next steps (suggested, not started)

1. Add a TCP-connect and DNS-resolution probe alongside ICMP, so loss classification can
   distinguish "no ICMP reply" from "no application-level connectivity."
2. Wire a UI entry point (a button, likely next to the recommendations grid) that lets a
   person actually trigger `SetAdapterDnsServersAction` and, critically, perform the **first
   real manual verification** of the elevated write path described in `01-architecture.md` §10.
3. Once step 2 has been run manually at least once, populate `NetPulse.SafetyTests` with
   whatever that manual run revealed needs automated coverage (rollback correctness,
   capability allow-list enforcement at the host's argument boundary).
4. Run the brief's FPS/network validation protocol (§11–12) once there is a verified
   optimization to measure, rather than against the diagnostics-only build.
5. Add a second recommendation rule that actually proposes `SetAdapterDnsServersAction`,
   closing the gap between Phase 4 and Phase 5.
6. Purchase a real code-signing certificate from a trusted CA and re-run
   `installer\build.ps1 -SigningThumbprint <real-thumbprint>` with it before any real
   distribution - see `04-phase7-distribution.md` §5.
