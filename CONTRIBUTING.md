# Contributing

Thank you for helping improve KeePassXC Card Print.

## Development principles

- Use English for documentation, source comments, scripts and build logs.
  Application strings may be localized in the existing RU/UK/EN dictionaries.
- Keep all source directories lowercase.
- Add no platform-specific APIs outside `platforms/windows/`. Shared code
  accesses the operating system only through `IPlatformServices` contracts.
- Keep the UI and physical print layout independent of the Windows printer
  backend. Preserve Native AOT compatibility and synthetic self-tests.
- Do not add migration code for historic application versions. There is one
  current configuration format in this codebase.
- Update dependency versions in the project/Windows props, not in the About
  UI. Keep the single application version in the project file.
- Preserve `[PASS]` and `[FAIL]` per assertion in all automated tests.

## Before submitting a change

Run `build.bat` on a supported Windows build machine. Review every test line,
not just the summary. Check both dark and light UI themes, display scaling,
keyboard focus, vector/raster output and safe no-selection preview behaviour
when they are relevant. If printer output changes, include a non-sensitive
test-sheet comparison and explain whether geometry or font shaping changed.

Never submit real passwords, KDBX files, key files, recovery codes, TOTP
secrets or identifying screenshots. Use synthetic examples and redacted logs.

## Reporting defects

Provide observed behaviour, expected behaviour, reproducible steps, platform
and dependency versions, and safe logs. Never assume a Windows build passes
merely because a source-only static inspection succeeds.
