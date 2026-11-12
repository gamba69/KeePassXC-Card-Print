# Configuration and state

## Locations

The Windows host provides two modes:

- **Portable:** `data/` beside the application root. When running the
  development executable from `publish/` in a repository containing
  `build.bat` and the source project, `data/` is resolved to the repository
  root, preventing rebuilds from deleting settings. For a distributed
  ZIP extracted anywhere else, `data/` remains beside the executable.
- **System:** `%LOCALAPPDATA%\keepassxccardprint`.

The selected mode is persisted in
`%LOCALAPPDATA%\keepassxccardprint\storage-mode.txt`. Without that marker,
`data/` takes precedence if it exists; otherwise system mode is used. The
Windows Registry is not used for this selector. Changing modes through the UI
copies only supported configuration files, with staging and backups if existing
files would be overwritten.

## Files

| File or directory | Purpose | Sensitive metadata |
| --- | --- | --- |
| `ui-shell.json` | Window, language, theme, preview, print device and calibration | Printer names |
| `vault-state.json` | Optional remembered KDBX/key-file paths and hashed selection state | Yes: local file paths |
| `appearance.json` | Current card appearance | No account secrets |
| `mapping.json` | Field mappings and label rules | May include user-defined labels |
| `domains.json` | Configured authentication-subdomain rules | User-entered presentation rules |
| `appearance-template-source.json` | Selected personal template filename | Template filename |
| `templates/*.json` | Personal card designs | User-entered template labels |
| `print-tracking/*.json` | Per-database entry print state and job history | Entry UUIDs, printer names |
| `backups/` | Settings backups when a storage switch replaces target files | May contain all of the above |

Do not commit these files. Their contents are local state rather than product
source. A fresh 6.0.13 installation starts from its own configuration defaults;
**there is no automatic import of earlier application formats**.

## Safe settings transfer

Use the in-app storage switch for a controlled move between portable and
system locations. It stages files, verifies copied bytes and restores prior
destination content on failure. It does not copy KDBX databases, key files,
passwords or TOTP secret values out of their original locations. Nevertheless,
configuration may still reveal locations, entry identifiers and print history.

## Settings validation

The program limits input file sizes, validates JSON and clamps unsupported
numeric values before applying them to the UI or renderer. Template names are
validated and cannot escape the `templates/` directory via path traversal.
Unsupported print/graphics settings fall back to defined defaults where
appropriate. Unexpected data formats are not treated as previous releases to
migrate automatically.
