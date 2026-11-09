# Testing and build contracts

## Mandatory tests in a normal build

Running `build.bat` performs source-level checks, a managed pre-AOT layout
self-test, and tests in the published Native AOT executable. The tests are not
disabled by default. The build exits non-zero on a failure. The release ZIP and its SHA-256
checksum are created only after the self-tests pass; the package is checked
for expected files, exclusions, and decompression integrity.
The tested executable and its runtime files live in `publish/`; successful
release archives and checksums are emitted to `dist/`. The build script also
checks the runner and GitHub Actions output paths to prevent layout regressions.

Each assertion is reported individually using the same format:

```text
[PASS] layout/0001: Windows host registered before card code
[PASS] layout/0002: bundled-font resources available
[PASS] mapping/001: default field loads
[PASS] vault/001: incorrect-key classification
[FAIL] layout/0003: <failing synthetic assertion label>
```

The example lines illustrate formatting; they are not a claim that the current
source has been built or that those are observed test results. A summary line
is in the same `[PASS]` format. An unexpected exception is a `[FAIL]` line.
Source-level architecture/branding/print checks also use `[PASS]` or `[FAIL]`
in the Windows build log.

## Self-test entry points

The published `KeePassXCCardPrint.exe` accepts the following command-line
switches. Each optionally takes a path for its text report:

```text
--self-test-layout
--self-test-mapping
--self-test-vault-errors
--self-test-xps
```

Layout assertions exercise synthetic cards, sheets, vector geometry, fonts,
cache behaviour, QR and duplex layouts, and the Windows XPS page builder.
Mapping tests exercise editor validation. Vault-error tests classify only
artificial KDBX errors; they do not open user files. The XPS test verifies a
synthetic page through the native Windows parser without submitting a job.

## Scope and limitations

A successful static audit does **not** prove that Avalonia/XAML compiles, that
Native AOT linking succeeds, or that a physical printer produces the correct
image. Those checks require the Windows toolchain and hardware. The only
valid end-to-end claim follows a Windows build, self-test log and manual
printer/GUI evaluation.

Do not weaken assertions to obtain a green build. Fix the tested contract or
update an assertion when the intended behaviour intentionally changes. Tests
must not log genuine passwords, user paths, KDBX contents or TOTP values.
