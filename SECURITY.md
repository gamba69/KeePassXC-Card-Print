# Security policy

## Scope

KeePassXC Card Print handles locally decrypted credential material for the
purpose of creating physical cards and exports. Its security depends on the
host machine, operating system, KDBX protection, printer, document handling
and the user's operational practices.

## Protecting sensitive data

- Keep KDBX databases, key files and master passwords private.
- Treat printed cards, PDFs, raster exports, printer spool data and backup
  sheets as confidential.
- Use trusted printers. Printer firmware, printer queues and Windows spooler
  behaviour are outside the app's process and may retain job data.
- Use the app's copyable technical diagnostics rather than collecting memory
  dumps or account screenshots. Do not share unredacted personal paths or
  printer names if they identify your environment.
- Do not reuse exposed secrets; rotate credentials if a print job was lost or
  sent to the wrong device.

## Reporting vulnerabilities

Contact the project's maintainers privately through an appropriate security
reporting channel when one is available. Do not post a proof of concept with
real secrets in public issues. Explain the affected functionality, expected
security boundary, and steps to reproduce with synthetic data. There is no
public response-time or support guarantee stated in this source package.

## Storage summary

The application intentionally persists no decrypted passwords or OTP tokens
as app settings. Optional state can contain database/key-file **paths**,
entry UUIDs, hashed selection markers, print history and printer names. OS
spoolers and files created by explicit export actions are independent data
handling surfaces and must be secured by the user.

## Publishing repository content

See [Repository privacy](docs/repository-privacy.md) for the automated scanner,
Git exclusions, release-package checks and the limits of this review.
