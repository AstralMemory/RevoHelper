# Third-Party Notices

## LibreHardwareMonitorLib

RevoHelper uses `LibreHardwareMonitorLib` for read-only CPU and GPU sensor discovery.

- Upstream: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Package: https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6
- Exact package source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701
- License: Mozilla Public License 2.0

RevoHelper consumes the unmodified NuGet library as a separate dependency.
Its MPL-covered source remains available under MPL 2.0 at the exact source
link above; RevoHelper's MIT license does not replace that license.

The upstream original third-party notice is preserved verbatim at
`packaging/ThirdParty/LibreHardwareMonitor-0.9.6-THIRD-PARTY-NOTICES.txt` and is
included in the generated `Third-Party.txt`. It contains the Aga.Controls BSD
notice and PawnIO.Modules LGPL 2.1 notice. Retaining the complete upstream
notice does not mean RevoHelper includes LHM's desktop GUI or Aga.Controls.

The library embeds twelve PawnIO.Modules 0.2.2 binaries. Their provenance and
corresponding source archive are documented in `THIRD_PARTY_NOTICES.md` at the
repository root; both 0.2.2 and the separately used 0.2.10 source archives are
supplied in the release's combined third-party notice.

## PresentMon 2.6.0

RevoHelper embeds the unmodified standalone x64 console collector, not the
PresentMon GUI, installer, service, or driver.

- Release: https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0
- Source: https://github.com/GameTechDev/PresentMon/tree/v2.6.0
- License: MIT; original text at `packaging/PresentMon/LICENSE.txt`
- Provenance: `packaging/PresentMon/README.md`

The original MIT notice is included in `Third-Party.txt` by default.
