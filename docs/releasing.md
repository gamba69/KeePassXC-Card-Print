# Manually preparing a GitHub release

This repository deliberately uses a **manual release gate**. A tag does not
trigger a build, a draft does not trigger a build, and successful GitHub Actions
does **not** publish the release. The only supported binary target for now is
**Windows x64 Native AOT**. No installer, Linux build or macOS build is created.

## 1. Verify the source version

The sole application version is the `<Version>` in
`source/keepassxccardprint/KeePassXCCardPrint.csproj`, e.g. `6.0.13`.
Commit the reviewed code and confirm all required checks have passed.

## 2. Create and push the tag manually

Both `vN.N.N` and `N.N.N` are accepted, for example:

```sh
git tag -a v6.0.13 -m "KeePassXC Card Print 6.0.13"
git push origin v6.0.13
```

Alternatively, create the tag in the GitHub web UI. The prefix is optional,
but the version after removing the optional `v` **must exactly match** the
version of the `.csproj` at the tagged commit. Do not move or overwrite a
published version's tag.

## 3. Create a draft Release manually

On GitHub, open **Releases** → **Draft a new release**. Choose the **already
existing exact tag** (`v6.0.13` and `6.0.13` are different tag names), fill in
the release title and notes, and choose **Save draft**. Do not publish it yet.

## 4. Run the release workflow manually

Open **Actions** → **Build Windows package for existing draft release** →
**Run workflow**. Choose the default branch for the workflow and enter the
existing tag exactly. The workflow must exist on the default branch for its
manual Run button to be available.

The workflow:

1. Verifies that the exact tag exists and that its release is still a draft.
2. Checks out the tagged commit (not the current development branch).
3. Confirms that the tag version matches the project's version.
4. Downloads the required .NET SDK, NuGet dependencies and verified portable
   fonts, compiles the Windows XPS bridge and publishes Native AOT.
5. Runs all mandatory pre-AOT and native self-tests. Any failure stops the job.
6. Produces the portable ZIP and SHA-256 checksum, verifies their contents,
   then attaches **both** files to the existing draft.
7. Leaves the release as a **draft**; no auto-publication happens.

The job is deliberately configured not to overwrite existing release assets.
If an upload partially failed, inspect the draft, remove incomplete assets
manually and rerun the workflow. Never modify the tag to work around a failed
build. Draft releases are visible only to identities with repository push access.
The preflight job therefore requires `contents: write` to read the existing
unpublished draft; the final attachment job also requires `contents: write`.
The compilation job remains read-only. Neither preflight nor attachment
creates, moves, publishes, or deletes a tag or a release.

## 5. Inspect and publish manually

Check the build logs and every `[PASS]`/`[FAIL]` test entry. Download the ZIP
and verify the checksum locally. In PowerShell:

```powershell
$zip = 'KeePassXCCardPrint-6.0.13-win-x64.zip'
$expected = (Get-Content "$zip.sha256" -Raw).Trim().Split(' ')[0]
$actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
if ($expected -ne $actual) { throw 'SHA-256 mismatch' }
```

Extract to a clean folder and verify startup, the About version, opening a
**synthetic/test** KDBX, layout, preview, manual duplex, print calibration,
and the target printer(s) before publishing. Do not use genuine secrets in
screenshots or test reports.

Only after the manual acceptance checks, open the draft on GitHub and click
**Publish release**. The workflow does not perform this action.

## Package contents

The release contains two attached files:

```text
KeePassXCCardPrint-6.0.13-win-x64.zip
KeePassXCCardPrint-6.0.13-win-x64.zip.sha256
```

The ZIP contains the portable executable, any runtime native DLLs copied by
`.NET publish`, `KpcXpsBridge.dll`, eight verified variable TTFs, their font
licenses, `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `README.md`. The package
excludes user settings, KDBX data, debug symbols, test logs, source files and
build caches. The `.sha256` file uses the standard SHA-256 checksum format with
two spaces before the ZIP basename.

## Building locally

`build.bat` always builds, tests and creates the same named ZIP and checksum.
Double-clicking `build.bat` pauses after success **or failure**, leaving its
results visible. Automation can invoke `build.bat --no-pause` (also accepts
`/no-pause` and `-nopause`), which exits immediately and preserves the exit
code. The runnable application is written to `publish/`, and the ZIP plus
`.sha256` are written to `dist/`. Both directories are overwritten by the
next build and excluded by `.gitignore`. Build intermediates use the usual
ignored `bin/` and `obj/` paths; there is no latest-build index.

Running `build.bat` locally never uploads files or creates a GitHub release.

## Bundled font requirement

The exact release tag must contain all eight variable TTFs in `fonts/`, the
matching eight OFL licenses in `fonts/licenses/`, and both manifests and
`FONT_SHA256.txt`. The Windows build verifies them locally and includes them
in the release ZIP. No font network download is part of the release procedure.
