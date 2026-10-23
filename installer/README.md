# NetPulse Optimizer installer

See `documentation/04-phase7-distribution.md` for the full design decisions and what has
actually been tested. Quick reference for building it yourself:

```powershell
# Unsigned build
.\build.ps1 -ProductVersion 0.1.0.0

# Signed build (needs a certificate thumbprint in Cert:\CurrentUser\My - see sign.ps1's
# own doc comment for what a self-signed test certificate can and cannot prove)
.\build.ps1 -ProductVersion 0.1.0.0 -SigningThumbprint <thumbprint>
```

This produces `NetPulseOptimizer.msi` in this folder. It installs **per-user, no admin
required** to `%LocalAppData%\Programs\NetPulseOptimizer\`:

```powershell
msiexec /i NetPulseOptimizer.msi          # interactive install
msiexec /i NetPulseOptimizer.msi /quiet   # silent install
```

Uninstall either through Windows Settings → Apps → "NetPulse Optimizer", or:

```powershell
msiexec /x NetPulseOptimizer.msi
```

`publish/` and `*.msi`/`*.wixpdb` are build output - not checked into git (see
`.gitignore`). Re-running `build.ps1` regenerates them from source.
