# Phase 7 — Distribution (installer, signing)

Status: **Real installer built, tested end-to-end (install/upgrade/uninstall), and
signed with a self-signed test certificate.** No certificate from a trusted CA exists for
this project - that is a real-world purchase/organizational step the user must take; this
phase establishes and demonstrates the *procedure*, exactly as the brief asks ("establece
un procedimiento de firma de binarios"), not a finished trust relationship.

## 1. Installer technology: WiX (MSI), not MSIX

The brief asks to evaluate MSIX given this project's privileged-operation needs (brief §9/§17).
MSIX was considered and **not used**, because:

- `SetAdapterDnsServersAction` depends on launching a separate, per-operation elevated
  process (`NetPulse.PrivilegedAgent.Host`, see `01-architecture.md` §10) via
  `Process.Start(..., Verb="runas")`. MSIX packages run in a containerized/virtualized
  environment with restrictions on launching arbitrary elevated child processes that don't
  apply to a traditionally-installed app; getting this exact elevation pattern working
  reliably inside an MSIX package would need the "Desktop Bridge" full-trust extensions and
  non-trivial extra packaging work, for no benefit relevant to this project.
- MSIX's main advantages (store distribution, automatic updates, stronger sandboxing) are
  not goals of this project yet - there is no store listing planned and no auto-update
  mechanism built.
- A traditional MSI, built with the open-source WiX Toolset, supports the per-operation
  "runas" elevation pattern with no special handling at all - it is just a normal
  Win32 process launch, identical to running the app outside any package.

**Installed as a WiX 5 MSI**, built via a dotnet global tool (`dotnet tool install --global
wix --version 5.0.2`) - no Visual Studio or Windows SDK required, consistent with how the
rest of this project was built in an environment that has only the .NET SDK. WiX v7 was
tried first but requires accepting a "maintenance fee" EULA before use; this project
deliberately did not accept any agreement with financial implications on the user's behalf
and used WiX v5 instead, which has no such requirement.

## 2. Install scope: per-user, not per-machine

`NetPulseOptimizer.wxs` sets `Scope="perUser"`, installing to
`%LocalAppData%\Programs\NetPulseOptimizer\` (the same convention VS Code and many modern
per-user Windows apps use) rather than `Program Files`. This was a deliberate choice, for
two reasons:

1. It matches the brief's "normal privileges by default" principle (`01-architecture.md`
   §7) - **installing or uninstalling the app never requires a UAC prompt**. Only
   `SetAdapterDnsServersAction`'s own per-operation elevation (unrelated to installation)
   ever prompts for admin.
2. It is why this phase's own installer work could be verified for real, end to end, inside
   this automated session: a per-machine install would need the same kind of interactive UAC
   approval that blocks testing the DNS write path (`01-architecture.md` §10), and this
   project's policy does not let an automated session perform that. Per-user installs need
   no such approval, so every claim below was actually run, not estimated.

## 3. A real bug found by testing the installer, not assumed to work

The first MSI built installed and uninstalled files correctly (verified), but **did not
create any visible entry in Windows' "Apps & Features" / Add-Remove-Programs list** -
confirmed by checking `HKCU:\...\Uninstall\{ProductCode}` directly after a real install and
finding nothing there, even though the product's own internal self-registration (under
`HKCU:\Software\Microsoft\Installer\Products`) was present. Left as-is, an end user would
have no standard way to uninstall the app - they would need to keep the original `.msi` file
and run `msiexec /x` manually, which most users would never do and most would not even have
the file for anymore.

**Fixed** by adding an explicit `ArpRegistration` component (`NetPulseOptimizer.wxs`) that
writes the standard `DisplayName`/`DisplayVersion`/`Publisher`/`UninstallString`/
`DisplayIcon`/`NoModify`/`NoRepair` registry values by hand under
`HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NetPulseOptimizer` - the
well-known workaround for this exact WiX/perUser gap. Verified afterward: the entry appears
with the correct name/version/publisher, and running the exact `UninstallString` value
(`MsiExec.exe /X{ProductCode}`) - the same command Windows itself runs when a user clicks
"Uninstall" - removes everything cleanly.

## 4. What was actually tested (real commands, real results, this machine)

| Scenario | Result |
|---|---|
| Install (`msiexec /i ... /quiet`) | Exit code 0; `NetPulse.App.exe` and `NetPulse.PrivilegedAgent.Host.exe` present side by side under `%LocalAppData%\Programs\NetPulseOptimizer\`; Start Menu shortcut created |
| Launch the installed app | Runs correctly, `MainWindowTitle` confirmed via a running-process check |
| Add/Remove Programs entry | Present after the fix in §3, with correct name/version/publisher |
| Uninstall via the registered `UninstallString` | Removes the install directory, the Start Menu shortcut, and the ARP entry completely |
| Major upgrade (install v0.1.0.0, then v0.1.1.0 over it) | Single ARP entry afterward, `DisplayVersion` updated to 0.1.1.0, no side-by-side duplicate, app still launches |
| Signing both executables and the MSI with a self-signed test certificate | Each file is genuinely signed (`Get-AuthenticodeSignature` reports `UnknownError` with the expected message "chain terminates in a root certificate not trusted by the trust provider" - correct for a self-signed cert deliberately not added to Trusted Root) |
| Install → launch → uninstall cycle using the **signed** artifacts | Same clean result as the unsigned cycle - signing did not break anything |

A real byproduct of building this installer: **`NetPulse.PrivilegedAgent.Host.exe` is now
actually deployed next to `NetPulse.App.exe`** by `installer/build.ps1`, closing the "Known
gap: helper deployment" that `01-architecture.md` §10 flagged back in Phase 5.

## 5. Code signing procedure (established, not a real trust relationship)

`installer/sign.ps1` signs any file with a certificate from `Cert:\CurrentUser\My`, using
PowerShell's built-in `Set-AuthenticodeSignature`/`Get-AuthenticodeSignature` - **no
`signtool.exe` or Windows SDK installation needed**, which matters because this environment
has neither.

For this phase, a self-signed test certificate was generated
(`New-SelfSignedCertificate -Type CodeSigning`, subject explicitly labeled
`"NetPulse Optimizer (TEST SIGNING ONLY - NOT FOR DISTRIBUTION)"`, thumbprint
`5B4442A937F7A79C376BE3DEA4C86956D29C4B77`, stored only in `Cert:\CurrentUser\My` - never
added to Trusted Root, which would have been a real system trust change this project's
policy does not permit). This certificate is **not suitable for real distribution** - it
will never show as trusted on any other machine, or even this one, because no one's Windows
installation trusts it. It exists only to prove the signing *procedure* works mechanically.

**What real distribution needs instead:** a code-signing certificate purchased from a
Certificate Authority Windows already trusts (e.g. DigiCert, Sectigo, SSL.com), or an EV
code-signing certificate for the strongest SmartScreen reputation. That purchase and the
identity verification it requires is the user's own step to take - not something this
session can do on their behalf. Once they have one, `installer\build.ps1
-SigningThumbprint <real-thumbprint>` runs the exact same procedure already demonstrated
here, using that certificate instead.

This also strengthens the UAC prompt `NetPulse.PrivilegedAgent.Host.exe` shows every time
`SetAdapterDnsServersAction` runs (`01-architecture.md` §10's threat-model note, added
during the Phase 6 review): today that prompt shows "Unknown publisher"; once signed with a
real certificate, it shows a verified publisher name instead.

## 6. Recovery

- **Failed/interrupted install.** Windows Installer's transacted install is a platform
  guarantee, not something this project's thin WiX authoring could break: if any step fails
  partway through, MSI automatically rolls back every change already made in that
  transaction. This was not force-tested (deliberately corrupting an install to prove
  rollback works would itself risk leaving test state behind) - it is the standard,
  well-established behavior of the Windows Installer engine for any `.msi`, not a claim
  specific to this package.
- **Recovering from a bad install.** Because this is a per-user install with no UAC
  requirement, the user can always re-run the same `.msi` (`msiexec /i`) to repair/reinstall,
  or uninstall via the Add/Remove Programs entry (§3) or `msiexec /x` directly, without
  needing elevation or administrator help.
- **This is separate from `SetAdapterDnsServersAction`'s own rollback** (its `CaptureAsync`/
  `RollbackAsync`, see `01-architecture.md` §10 and `tests/NetPulse.SafetyTests`) - installer
  recovery is about the *application files*, action rollback is about a *network setting
  change*. Uninstalling the app does not revert any DNS change that action may have applied;
  that is why the action carries its own independent rollback contract.

## 7. What this phase did not do

- No real, CA-issued code-signing certificate - see §5.
- No installer UI (license page, install-location picker, etc.) - the MSI installs silently
  to a fixed per-user location with no dialogs, which is standard for simple per-user tools
  but a choice worth revisiting if the brief's §14 UX polish is pursued later.
- No automatic-update mechanism - re-running a newer installer is a manual action today.
- No MSIX package was actually built for comparison - §1 is a reasoned-through decision
  with concrete technical justification, not an empirical side-by-side.
- No forced-failure test of MSI's rollback guarantee - see §6.
