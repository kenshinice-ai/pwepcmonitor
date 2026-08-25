# Third-party notices

## LibreHardwareMonitor — MPL-2.0

PWE PC MONITOR uses `LibreHardwareMonitorLib` 0.9.6 for read-only hardware sensor discovery and sampling.

Project: <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>

Licence: [licenses/MPL-2.0.txt](licenses/MPL-2.0.txt)

No LibreHardwareMonitor source files are modified in this repository. The NuGet package and its transitive dependencies retain their own notices and licences.

LibreHardwareMonitor's Windows GPU backends use the vendor interfaces exposed by
the installed graphics driver: NVIDIA NVAPI, AMD ADL/ADL2 and Intel Graphics
Control Library (IGCL). PWE only consumes the managed library's read-only sensor
surface; it does not redistribute `nvapi64.dll`, `atiadlxx.dll` or
`ControlLib.dll`. Those binaries remain part of their respective driver
installations and are governed by their vendor terms.

## PawnIO — external optional prerequisite

PWE PC MONITOR does not bundle the PawnIO kernel driver or its installer. When a
machine needs the extra hardware-access layer, the app opens the official
installer published at <https://github.com/namazso/PawnIO.Setup/releases> and
the user approves installation in Windows. PawnIO remains a separate optional
component with its own source, binary and licensing terms.

## Inter — SIL Open Font License 1.1

Inter is bundled for interface text and numeric readouts.

Copyright 2016 The Inter Project Authors.

## Playfair Display — SIL Open Font License 1.1

Playfair Display is bundled for the wordmark and brand voice. “Playfair Display” is a Reserved Font Name.

Copyright 2017 The Playfair Display Project Authors.

The full SIL Open Font License 1.1 text is included at [licenses/SIL-OFL-1.1.txt](licenses/SIL-OFL-1.1.txt).
