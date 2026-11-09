# Repository privacy and publication safety

This project handles credentials, but **the Git repository must never contain**
real vault data, passphrases, key files, personal settings, print history,
exports, user screenshots or diagnostic dumps.

## Scope of the pre-publication source review

Before each public release, review the source, test fixtures, build scripts,
documentation, bundled fonts and licenses, image metadata, and ZIP metadata.
The included privacy scanner checks for common embedded secrets and private
files. Deterministic synthetic test data uses reserved `.invalid` domains.
Copyright holders named in bundled font metadata are third-party public
credits, not application users.

This is a source-package review, **not** a verification of an external Git
repository's previous commits, GitHub secrets, local build outputs or operating
system printer queues. A clean source checkout does not establish that those
other locations contain no secrets.

## Automatic protections

- Run `tools/verify_repository_privacy.ps1` with Windows PowerShell 5.1.
- `build.bat` runs the privacy check before restore/publish; a failure stops
  the build and returns a nonzero exit code.
- The manual draft-release GitHub Actions workflow also validates the exact
  tag checkout before compilation or artifact upload.
- `.gitignore` excludes likely vaults, key files, local settings, print
  tracking, user backups, PDFs, screenshots and generated outputs.
- `tools/verify_repository_privacy.ps1` also inspects Git-tracked paths when Git is
  available, including files force-added despite `.gitignore`.
- `tools/verify_release_package.ps1` refuses sensitive filename patterns
  and local configuration directories in the final executable ZIP.
- Diagnostic reports intentionally contain only allowlisted technical fields.

The scan reports the **file path, line number and category**, never the matched
secret. It uses a conservative combination of filenames and high-confidence
secret patterns. No scanner can prove that arbitrary text or an image is free
of all confidential data; review every newly added test fixture and asset.

## Before pushing to GitHub

1. Use **synthetic fixtures only**; `.invalid` is the recommended test domain.
2. Check `git status --short` and `git diff --cached --name-only` and inspect
   all new text, image and binary files before committing.
3. Run the privacy check locally, then `build.bat` (or `build.bat --no-pause`).
4. Inspect `publish/` and `dist/` manually before shipping; both are ignored.
5. Do not publish source screenshots showing vault contents or print previews
   unless the contents are entirely synthetic.
6. If a real secret was ever committed, **rotate/revoke it**, then assess and
   clean Git history and remote forks; deleting the file from the latest
   revision does not erase prior commits.

The bundled fonts and their SIL OFL 1.1 licenses are safe for public source
redistribution; copyright and designer credits must be retained.
