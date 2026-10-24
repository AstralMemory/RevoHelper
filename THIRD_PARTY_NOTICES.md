# Third-party notices

RevoHelper's own code is licensed under MIT. Third-party components retain
their own licenses; the MIT license does not relicense the modules below.

## PawnIO.Modules 0.2.2 embedded by LibreHardwareMonitorLib 0.9.6

The unmodified LibreHardwareMonitorLib 0.9.6 NuGet library embeds these twelve
signed PawnIO modules: `AMDFamily0F`, `AMDFamily10`, `AMDFamily17`, `IntelMSR`,
`IsaBridgeEC`, `LpcACPIEC`, `LpcCrOSEC`, `LpcIO`, `RyzenSMU`, `SmbusI801`,
`SmbusNCT6793`, and `SmbusPIIX4`.

- Project: https://github.com/namazso/PawnIO.Modules
- Release: https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.2
- Corresponding source: https://github.com/namazso/PawnIO.Modules/tree/0.2.2
- License: GNU Lesser General Public License v2.1 or later
- Copyright: namazso and respective upstream contributors
- Local original source archive: `packaging/ThirdPartySource/PawnIO.Modules-0.2.2-source.zip`
- LHM update record: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/commit/4b3ed8bb4623128927480aaa8cdf0d9e09ebc999

All twelve embedded module files were compared byte-for-byte with the official
0.2.2 release archive and match. The old resource README in LHM still refers
to 0.1.6; the update record and actual embedded bytes establish 0.2.2 here.
The complete upstream LHM third-party notice is retained at
`packaging/ThirdParty/LibreHardwareMonitor-0.9.6-THIRD-PARTY-NOTICES.txt`.
The 0.2.2 archive is also included as a separately identified Base64 source
attachment in the generated `Third-Party.txt`.

## PawnIO.Modules IntelMSR 0.2.10

RevoHelper embeds the official signed `IntelMSR.bin` module from PawnIO.Modules
release 0.2.10. The module provides the narrowly scoped `ioctl_read_msr` and
`ioctl_write_msr` entry points used by the Intel RAPL backend; RevoHelper does
not install or replace the PawnIO driver.

- Project: https://github.com/namazso/PawnIO.Modules
- Release: https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.10
- License: GNU Lesser General Public License v2.1 or later
- Embedded file SHA-256: `D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F`

The corresponding source is available in the upstream project. PawnIO itself
is a separate dependency and remains subject to its own license and terms.
The original 0.2.10 source archive is retained at
`packaging/ThirdPartySource/PawnIO.Modules-0.2.10-source.zip` and as a separate
Base64 attachment in the generated `Third-Party.txt`.

## PawnIO.Modules IntelMCHBAR 0.2.10

RevoHelper embeds the official signed `IntelMCHBAR.bin` module from
PawnIO.Modules release 0.2.10 for read-only diagnosis. RevoHelper calls only
`ioctl_get_mchbar_addr` and `ioctl_read_qword`; this module exposes no MCHBAR
write entry point.

- Project: https://github.com/namazso/PawnIO.Modules
- Release: https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.10
- License: GNU Lesser General Public License v2.1 or later
- Embedded file SHA-256: `3F82B832D99B4AAC37D2A20FDB7C9BAA2A3BC0488612C9019C9484EB0E8A6EAE`

The corresponding source is available in the upstream project.
