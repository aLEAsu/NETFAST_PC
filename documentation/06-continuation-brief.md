# NetPulse Optimizer — Continuation Brief

**Read this before touching the code.** It is written as a self-contained brief for
whoever (human or agent) picks this project up next, so it does not assume you have read
any prior conversation. It supersedes nothing in `01-architecture.md` through
`05-code-audit-and-recommendations.md` - it synthesizes them into a prioritized plan and
adds the parts that were deliberately deferred.

## 1. What this project is

NetPulse Optimizer is a Windows desktop app that diagnoses, measures, and - under explicit
user authorization - optimizes a PC's network path. It is built as a layered .NET 10
solution (Domain → Application → Infrastructure.Windows/Persistence/PrivilegedAgent → App),
with a WPF dashboard (WinUI 3 was the original target; WPF was substituted because this
environment has no Visual Studio/Windows App SDK - see `01-architecture.md` §3). Read
`README.md` first for the solution layout, then `01-architecture.md` for the design.

## 2. What is actually true right now (verified, not claimed)

- Builds clean, 0 errors/0 warnings. 211 tests pass across four suites: 134 unit (fakes),
  68 integration (real adapter/ICMP/TCP/DNS/SQLite/WMI-read/IP-Helper/loopback-HTTP, plus
  `NetPulse.App`'s `FindingRowViewModel`), 5 safety (recovery lifecycle, fakes), 4 performance
  (real CPU/allocation benchmarks). Re-run all four before trusting this number - it will
  have moved by the time you read this.
- The dashboard UI is now a `TabControl` (Adaptadores / Diagnóstico / Red por proceso /
  Bufferbloat / Captura avanzada) instead of one long vertically-stacked screen - the
  original single-screen layout was reported as cluttered/unintuitive, and adding Modules
  E/F/G to it would have made that materially worse.
- Diagnostics (ICMP + TCP confirmation + DNS resolution timing) are real and tested.
- The recommendation engine (6 rules) is real and tested. Exactly one rule
  (`SlowDnsResolutionRule`) has an applyable action behind it.
- `SetAdapterDnsServersAction` (the one real, reversible optimization this build has) is
  fully implemented, fully unit/safety-tested against fakes, and **reachable from the
  running app's UI** (select an adapter, run a diagnostic, click "Apply suggested DNS
  servers" if the rule fires). **Its actual write has never been executed against a real
  adapter by any automated session.** That is not a historical footnote - it is the single
  most important unverified fact about this codebase. See §4 before changing that.
- A real WiX MSI installer exists, installs per-user (no admin needed), and the full
  install/upgrade/uninstall cycle has been tested for real. Signing works mechanically with
  a self-signed test certificate; there is no certificate any real machine would trust.
- Profiles (Module H) exist: 6 fixed profiles adjusting diagnostic parameters and
  recommendation ordering only.
- **Module C (advanced NIC properties) is now implemented**, read-only, exactly per §6.4's
  plan: `IAdapterAdvancedPropertiesReader`/`WmiAdapterAdvancedPropertiesReader` query
  `MSFT_NetAdapterAdvancedPropertySettingData` in `root\StandardCimv2` (note: that class has
  no `InterfaceGuid` property - the adapter GUID is the prefix of `InstanceID`, e.g.
  `{GUID}::*EEE` - verified against this machine's real Ethernet adapter via
  `Get-CimInstance` after the first query attempt failed with "invalid query"). The
  dashboard shows the selected adapter's properties (RSS, EEE, offloads, etc.) in a new
  read-only grid, refreshed automatically on selection change. No write path exists at all,
  per the brief's explicit instruction not to add one in this pass. 6 new real integration
  tests in `WmiAdapterAdvancedPropertiesReaderTests.cs`.
- **Module E (bufferbloat) is now implemented**, with a deliberate scope decision: load is
  generated against Cloudflare's public speed-test endpoints (`speed.cloudflare.com/__down`
  and `/__up`) rather than a self-hosted endpoint - the user chose this explicitly, aware it
  is a third party outside this project's control. The request/response shape was verified
  once, manually, via `curl` before writing `CloudflareLoadGenerationService` (not guessed).
  Reachable only from its own dedicated "Bufferbloat" tab/button - never bundled into
  "Ejecutar diagnóstico" - per the non-negotiable constraint that only a human may trigger a
  real load run; this automated session never did so itself. A real cancellation bug was
  found by the new integration tests (an already-cancelled caller token made the load loop's
  condition false on its very first check, so it returned silently instead of throwing) and
  fixed with an explicit `ThrowIfCancellationRequested()` before the loop. Grading thresholds
  (`BufferbloatSeverity`) are this project's own bucketing, explicitly not presented as an
  industry standard.
- **Module F (per-process network) is now implemented, with a narrower, more honest scope
  than literal "per-process bandwidth."** Windows has no unelevated, non-ETW API for real
  per-process byte counters - Resource Monitor's own figures rely on ETW. Rather than
  fabricate an estimate (splitting adapter throughput across active connections, for
  example), this ships **connection visibility** (`ProcessNetworkConnectionsReader`, via
  `GetExtendedTcpTable` - the same mechanism `netstat -ano` uses, no elevation needed, IPv4
  TCP only) shown **alongside**, never fused with, each interface's real WMI-measured
  throughput rate (`WmiAdapterThroughputReader`). The UI is explicit that this is not a
  bandwidth-per-process number.
- **Module G (pktmon/ETW) is now implemented, scoped down deliberately from full packet
  capture to counters-only.** Verified on this machine that even `pktmon list` refuses
  unelevated ("Acceso denegado"), so every pktmon interaction goes through the same
  elevation boundary as the DNS action (`PrivilegedCapability.CapturePacketCounters`,
  `PacketCounterCaptureAction`, a new branch in `NetPulse.PrivilegedAgent.Host`). It runs
  `pktmon start --counters-only --comp nics`, sleeps for a capped, caller-chosen duration
  (1-60 s, enforced independently inside the elevated host), reads `pktmon counters`, then
  stops and unloads the driver - never capturing packet content or payload, and showing the
  raw, unparsed text output rather than a derived summary this project did not directly
  observe. `--comp nics` (not a specific adapter id) because resolving a specific component
  id itself needs an elevated `pktmon list` call first, deferred to keep the elevated code
  path simple. Like `SetAdapterDnsServersAction`, this automated session has never triggered
  a real elevated pktmon run - that first run must be a deliberate, manual action by a
  person.
- A DNS-action usability bug was fixed: the post-apply/verify status message used to show
  raw WMI-flavored text that a user reported as unclear about whether the change actually
  worked. It now spells out before/requested/now explicitly in plain Spanish.
- WinUI 3 migration remains not implemented (environment constraint, see §7 below).

If any of the above sounds wrong when you check it, trust the code and the test run over
this document - re-verify, don't assume.

## 3. Non-negotiable constraints (carry these forward regardless of who/what continues this)

These held for every phase of this project so far and must keep holding:

1. **No automated session executes a real elevated system change.** Not
   `SetAdapterDnsServersAction.ApplyAsync`/`RollbackAsync` against a real adapter, not any
   future action like it. The reason isn't caution theater: a UAC prompt exists specifically
   so a human approves the change knowingly. An agent approving its own UAC prompt (even if
   it technically could) defeats that control. This applies even if the human operating the
   session says to go ahead - the UAC step itself has to be the human's own click.
2. **No automated session generates substantial, sustained network load** (a bufferbloat
   test's whole point is to saturate the link) without the human explicitly, interactively
   triggering that specific run and knowing roughly how much data and for how long. Design
   and build the load-test code; don't let an agent decide on its own to fire a 30-second
   saturating download as part of "testing the feature."
3. **No fabricated numbers, ever.** Every CPU/RAM/latency/throughput figure in this
   project's documentation was produced by actually running something and reading the real
   result - including the ones that turned out unflattering (idle memory above the brief's
   reference, for instance). Keep that discipline. If a number can't be measured in the
   current environment, say so explicitly rather than estimating and presenting it as
   measured.
4. **Every new capability gets a fixed, reviewed, closed catalog entry - never a generic
   "run this with these parameters" escape hatch.** `PrivilegedCapability` (one enum member
   today), `DefaultRecommendationRules`, `DiagnosticProfileCatalog` - all closed catalogs,
   no plugin loader, no external configuration source. Keep that pattern for anything new
   that executes code or proposes a change.
5. **Build and run all four test suites after every change**, not just the one you think
   you touched - cross-cutting changes (like the `NetworkEvidence` refactor in this pass)
   break call sites in ways that are easy to miss. `dotnet build NetPulseOptimizer.slnx`
   then each `dotnet test tests/NetPulse.<X>Tests/...csproj`.
6. **Update the documentation in the same change that changes the behavior it describes.**
   This project's documentation went stale multiple times purely from not being touched
   when the code moved - see `05-code-audit-and-recommendations.md` §3 recommendation 4.

## 4. Priority 1: get a human to run the DNS action, once, for real

This is not a coding task. Before adding anything else to `PrivilegedAgent`, someone needs
to:

1. Build the solution (`dotnet build NetPulseOptimizer.slnx -c Release`) or run
   `installer\build.ps1` to get a real installed copy.
2. Run `NetPulse.App.exe`, select a real network adapter, run a diagnostic against a
   hostname target whose DNS resolution is artificially slow or that doesn't resolve (to
   make `SlowDnsResolutionRule` fire) - or temporarily lower
   `SlowDnsResolutionRule.ResolutionTimeThreshold` for testing purposes only, then revert it.
3. Click "Apply suggested DNS servers," read the confirmation dialog, approve it, and
   **personally watch the UAC prompt appear** for `NetPulse.PrivilegedAgent.Host.exe`.
4. Approve the UAC prompt, confirm the DNS change actually took effect (`ipconfig /all` or
   Windows' own network settings), test connectivity still works, then click "Rollback DNS
   change" and confirm the original DNS configuration is restored.
5. Decline the UAC prompt once too, to confirm the app reports that cleanly instead of
   hanging or crashing.
6. Write down what actually happened - which log lines appeared in the audit table
   (`PrivilegedOperationAudit` in the SQLite database under `%LocalAppData%\NetPulseOptimizer\`),
   whether the helper process window flashed visibly, timing, anything surprising.

Only after this should `NetPulse.SafetyTests` be extended with whatever this manual run
revealed needs automated coverage going forward (per
`02-roadmap-and-limitations.md`'s existing note on this).

## 5. Priority 2: small, cheap completions

- ~~**Surface TCP/DNS probe results in the UI.**~~ **Done.** `FindingRowViewModel` now has
  `TcpCheck`/`DnsCheck` string properties (e.g. "Connected (12 ms, port 443)" / "N/A (target
  is an IP literal)" / "Failed: <error>"), shown automatically by the findings `DataGrid`'s
  `AutoGenerateColumns`. Verified with 5 real tests in
  `tests/NetPulse.IntegrationTests/App/FindingRowViewModelTests.cs` - not
  `NetPulse.UnitTests`, because `NetPulse.App` targets `net10.0-windows` and
  `NetPulse.UnitTests` targets plain `net10.0`, which can't reference a Windows-targeted
  project. If `NetPulse.App` grows more testable logic, keep using
  `NetPulse.IntegrationTests` for it (it already targets `net10.0-windows`) rather than
  retargeting `NetPulse.UnitTests`.
- **Consider exposing `DiagnosticOptions.TcpConfirmationPort`** in the UI if real-world use
  shows 443 is frequently wrong for a given target (e.g. targets that only answer on 80).

## 6. Priority 3: the four modules (historical planning - all four are now implemented)

**Status: all four modules below are now implemented** (see §2's bullets for what actually
shipped and where each deviated from the plan sketched here - notably Module F's honest
scope-down to connection visibility rather than fabricated bandwidth numbers, and Module G's
scope-down to counters-only rather than full packet capture). The subsections below are kept
as the original planning record, not rewritten to match what shipped - read §2 first for the
current truth, and treat anything here that conflicts with §2 as superseded.

### 6.1 Module E — Bufferbloat (active-load latency test)

**What it needs:** measure latency at rest (already have this), then measure latency while
generating controlled upload and/or download load, and compare. The load itself needs a
real data source/sink - either a public speed-test-style endpoint (introduces a dependency
on a third party and its terms of service) or a self-hosted throwaway endpoint (more
control, more setup). Decide this explicitly rather than defaulting to whichever is easiest.

**Suggested shape**, consistent with existing patterns:
- `Domain.Network`: `BufferbloatSample` record (latency-at-rest stats, latency-under-load
  stats, delta, direction tested).
- `Application.Ports`: `ILoadGenerationService` (`GenerateLoadAsync(direction, duration,
  cancellationToken)` - upload or download, bounded duration, cancellable mid-run per the
  brief's "cancel immediately" requirement).
- `Infrastructure.Windows`: a real implementation using `HttpClient` against a configured
  endpoint, respecting the duration bound strictly (never run longer than asked, always
  honor cancellation within one buffer-write cycle).
- A new use case that: captures latency at rest (reuse `ConnectionDiagnosticUseCase`'s own
  ICMP sampling), starts the load generator, samples latency concurrently during the load
  window, stops the load generator, compares before/after.
- **Must require explicit, separate user confirmation before starting** (constraint §3.2
  above) - not bundled into the regular "Run diagnostic" button's existing flow.
- A new recommendation rule reading the comparison (`BufferbloatDetectedRule` or similar),
  informational only (per the existing pattern - recommend checking router QoS/SQM, not a
  Windows setting this app can fix).
- Add it to the "Game protection mode" concept from the brief (§10/§3.2) when that exists -
  bufferbloat testing is exactly the kind of intensive diagnostic that mode should suspend.

### 6.2 Module F — Per-process bandwidth accounting

**Feasibility note first:** Windows doesn't expose simple per-process bandwidth the way
Task Manager's "Network" column implies without using either (a) ETW
(`Microsoft-Windows-TCPIP`/`Microsoft-Windows-Winsock-AFD` providers), which needs an ETW
session (administrator for a real-time session in most configurations) and parsing a
non-trivial event schema, or (b) WMI performance counters
(`Win32_PerfFormattedData_Tcpip_*` or similar), which are coarser but don't need elevation.
**Recommendation: start with (b)** - it directly matches the brief's own instruction ("no
instales filtros de red propios como requisito de la primera versión") and the
unelevated-by-default principle, even though the data will be less precise. Only move to
ETW if WMI counters prove too coarse to be useful, and if so, isolate that ETW session
behind the same kind of elevation boundary `PrivilegedAgent.Host` already establishes for
writes - reading via ETW at this level likely still needs elevation, so treat it with the
same seriousness as a privileged capability, not as "just a read."

**Suggested shape:** a new `IProcessBandwidthReader` port, read-only, clearly labeled in its
own documentation which numbers are exact counters vs. estimates (the brief explicitly
requires this distinction - §6 Module F: "Indica claramente qué métricas son directas y
cuáles son estimaciones"). Never add code that pauses or kills a process - the brief
explicitly limits this module to visibility, not control, for the first version.

### 6.3 Module G — pktmon/ETW advanced diagnostics

**Feasibility note:** `pktmon` is a command-line tool (`pktmon.exe`, built into modern
Windows), not a library - wrapping it means launching it as a process and parsing its
output or its `.etl` capture files (which need a separate parser, e.g. via the
`Microsoft.Diagnostics.Tracing.TraceEvent` NuGet package to read ETW traces). This is a
capture tool, which means it touches the brief's privacy requirements directly (§3.6, §G:
"La captura debe estar desactivada por defecto, limitada en tiempo, tamaño y alcance").

**Suggested shape:**
- A new, separate `PrivilegedCapability` member (`CapturePacketTrace` or similar) - this is
  unambiguously privileged (packet capture needs administrator), so it goes through the
  exact same elevation boundary `SetAdapterDnsServersAction` already established, not a new
  pattern. Reuse `IElevatedOperationLauncher` and add the new capability case to
  `NetPulse.PrivilegedAgent.Host`'s fixed switch.
  on captures: enforce a hard maximum duration and file size in the Host itself (not just
  the UI), reject any request exceeding it before touching `pktmon`, and default the
  capability to filtered/scoped capture (specific adapter, specific duration) - never an
  unscoped "capture everything."
- Never store captured payload content beyond what's needed for the specific diagnostic
  claim being made - the brief is explicit that captures must not retain browsing content.
- This module is the most privacy/security-sensitive item on this entire list. If you are
  not confident you can bound scope/time/size correctly at the elevated boundary, don't ship
  it partially - an unbounded or loosely-bounded packet capture capability is worse than not
  having the feature.

### 6.4 Module C (advanced) — NIC properties (RSS, offloads, EEE, etc.)

**The more tractable of the four.** These are exposed read-only via
`Get-NetAdapterAdvancedProperty`-equivalent WMI classes (`MSNdis_*` or the newer
`MSFT_NetAdapterAdvancedPropertySettingData` in the `root\StandardCimv2` WMI namespace) or
by shelling out to the `NetAdapter` PowerShell module's cmdlets (heavier, needs PowerShell
hosting). **Recommendation: WMI directly**, following the exact pattern already established
by `WmiAdapterDnsConfigurationReader` - a read-only reader, no write path in the unelevated
app, real integration test against this machine's real adapters, and explicit handling for
"this adapter doesn't expose this property" (already proven out for MTU in Phase 3/6 - see
`NetworkAdapterInventoryService.SafeMtu`'s doc comment on the loopback MTU bug that pattern
was built to avoid repeating).

**Do not add a write path for these properties in the same change as the read path.** The
brief is explicit (§6 Module C): no universal "maximum values," keep defaults unless there's
specific diagnosed evidence. Read-only first, let it ship and be used, and only add a write
capability (through the same elevation boundary, with its own descriptor/risk/rollback) once
there's a concrete, evidenced optimization rule that needs it - the same discipline Phase 5
followed for DNS.

## 7. Other open items

- **WinUI 3 migration.** Blocked on this environment lacking Visual Studio / the Windows App
  SDK workload - not a code problem. If/when that's available, `NetPulse.App` can be
  replaced without touching `Domain`/`Application`/`Infrastructure.Windows`/`Persistence`
  (see `01-architecture.md` §3's migration note) - but note the DNS-action UI wiring and
  Profiles ComboBox added in this pass need re-implementing in WinUI 3 XAML/x:Bind, they
  won't carry over automatically.
- **Real code-signing certificate.** A purchase and identity-verification step belonging to
  whoever owns this project, not something any session can do. Once obtained, re-run
  `installer\build.ps1 -SigningThumbprint <real-thumbprint>` - the procedure is already
  proven with the self-signed test certificate.
- **Per-machine install option.** The current installer is per-user only, by deliberate
  choice (see `04-phase7-distribution.md` §2) so the installer itself stayed testable in an
  automated session. A per-machine (`Scope="perMachine"`) variant is straightforward to add
  as a second configuration if ever needed, but its own install/uninstall cycle would need
  the same kind of human-driven UAC verification as §4 above, for the same reason.
- **Second `IOptimizationAction`.** Before adding one, do the small refactor
  `05-code-audit-and-recommendations.md` §3 recommends (a lookup table from recommendation
  id → action factory) rather than growing `if` branches in `DashboardViewModel`.
- **UI is now fully in Spanish, including the Domain layer.** The earlier version of this
  note deferred `ConnectionDiagnosticUseCase.Classify`'s `DiagnosticFinding.Summary`/
  `Evidence`/`Limitations` and the 6 recommendation rules' `OptimizationDescriptor` fields,
  estimating "~100 exact-string test assertions" would need updating in lockstep. That
  estimate turned out to be wrong when actually checked: a full grep across every test
  project found only `finding.Id` (a stable identifier, e.g. `"connectivity.no-response"`)
  was ever asserted on for the diagnostic findings, and only 4 small, specific assertions
  touched rule descriptor prose (`HealthyNoActionRuleTests`, `HighMedianLatencyRuleTests`,
  `InsufficientSamplesRuleTests`, `PacketLossInvestigateRuleTests`) - all four updated
  alongside the translation. All 211 tests still pass. `HighJitterBufferbloatFollowUpRule`'s
  text was also corrected while translating it: it used to say the bufferbloat load test
  "does not exist in this codebase yet," which stopped being true once Module E shipped -
  it now points at the real "Bufferbloat" tab. The word "bufferbloat" itself is kept
  untranslated throughout (titles, tab name, UI) - it is the term actually used in
  Spanish-language networking discourse, not a gap.

## 8. Working method (how this project has been built so far - keep doing this)

- Fakes-based unit tests for business logic; real integration tests (no fakes) for anything
  touching the OS, network, or filesystem, with deterministic targets (loopback,
  self-hosted listeners, RFC 5737/2606 reserved addresses) instead of depending on a third
  party being reachable.
- Every new capability gets a doc comment explaining *why*, not just what - this project's
  code comments carry real design reasoning (elevation boundaries, validation rationale,
  threat-model notes), not restatements of the code.
- Commit messages explain what was found/fixed and why, including real bugs the tests
  themselves caught (see git log for examples - the zero-interval regression, the ARP
  registration gap, the MTU bug).
- When something can't be verified in the current environment (no game for FPS testing, no
  CA certificate, no Visual Studio), say so plainly in the documentation rather than
  skipping the topic or guessing.
