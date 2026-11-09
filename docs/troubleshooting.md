# Troubleshooting

## The build stops before C# compilation

Start with the first `[FAIL]` line. PowerShell 5.1 requires `tools/build.ps1`
to retain its UTF-8 BOM. The bootstrap script `tools/check_build_syntax.ps1`
checks both encoding and parsing before the main build starts. Do not paste a
partial script into a new non-BOM file.

If an architecture or UI-contract assertion fails, inspect the named file and
fix the underlying mismatch; do not suppress the check. Remember that the
`Program.cs` entry point resides under `platforms/windows/`.

## Native AOT or MSVC failures

Verify the patched .NET 10 SDK, MSVC x64 build tools and Windows SDK. A normal
managed build is necessary but not sufficient. The XPS COM bridge is compiled
separately to `KpcXpsBridge.dll` and is distributed alongside the native EXE.
Package restore may include non-Windows transitive assets; these are not
necessarily distributed in the Windows binary.

## Wrong or stale application icon

The EXE/titlebar/taskbar icon is the supplied light `CardPrint_white.ico`.
Inside the client area, the dark/light variants change with the application
theme and select native ICO frames by display scaling. If only Explorer shows
a previous icon, the Windows icon cache or an old shortcut may be responsible.
Do not resample the entire 256-pixel icon for a 16-pixel UI target.

## Empty preview

The preview intentionally has no account cards when no entries are selected.
Select at least one entry before assuming that preview rendering failed. If a
card remains after deselection, report the sequence of UI actions without
including entry names or a screenshot of readable credentials.

## A printer cannot print vectors or duplex

The selected driver controls availability. Confirm printer capabilities and
paper settings. An automatic raster fallback is permitted only if the vector
attempt definitely did not submit a spooler job. In an ambiguous failure,
check the print queue and output before retrying. Use a non-sensitive test
sheet when calibrating a new printer.

## Settings are not found

Check which storage mode is active. The portable folder is `data/`; system
settings live in `%LOCALAPPDATA%\keepassxccardprint`. The selector is stored
in that system folder, not in Registry. New 6.0.9 settings do not import older
application files. Do not overwrite an installation's active configuration
without backing it up first.

## Sending a safe diagnostic report

Use the About tab's safe copy feature and supply the first `[FAIL]` line,
Windows version, display scale, the problem's steps to reproduce and the
selected output mode. Redact all paths, account names, device serials,
credentials and generated cards. Never upload a real KDBX database.
