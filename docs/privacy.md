---
layout: default
title: Privacy policy
permalink: /privacy/
---

<nav class="site-nav" aria-label="Documentation navigation">
  <a href="{{ '/' | relative_url }}">Overview</a>
  <a href="{{ '/guide/' | relative_url }}">User guide</a>
  <a href="{{ '/gallery/' | relative_url }}">Gallery</a>
  <a class="active" href="{{ '/privacy/' | relative_url }}">Privacy</a>
  <a href="https://github.com/gamba69/KeePassXC-Card-Print">GitHub</a>
</nav>

# Privacy policy

**KeePassXC Card Print** is a portable, locally operated desktop application
for reading a user-selected KeePassXC-compatible KDBX file and preparing
printed account-information cards. It is an independent open-source project;
it does not provide a hosted vault or cloud synchronization service.

## What the application processes

When requested by the user, the application may process KDBX entries,
passwords, TOTP secrets, recovery codes, notes, UUIDs and other selected fields
in process memory to render cards. A master password, optional key file or
supported YubiKey challenge-response may be required to unlock the vault.

The preview and print pipeline necessarily handle decrypted values. The
application is not designed to send the user's vault content to a server
operated by this project.

## Local configuration and history

Depending on the selected settings, local application data can include
file paths to KDBX databases and key files, per-database selection identifiers,
entry UUIDs, custom mapping labels, templates, printer names, calibration and
print history. The application does not intentionally store decrypted entry
passwords or TOTP values in its configuration files.

Settings can be stored beside the portable application in `data/` or in
`%LOCALAPPDATA%\keepassxccardprint` on Windows. Settings backups may contain
the same identifying metadata and must be protected.

## Printing, files and third parties

Printed cards, PDF files and raster exports can contain credentials in
plaintext. The Windows spooler, printer drivers, printer firmware and
user-chosen export destinations are external systems that may retain or
transmit job data. Their behavior is outside the application's control.
Use a trusted local computer and printer, store your documents securely, and
avoid untrusted PDF printers or online conversion services.

## Site and repository

This GitHub Pages site publishes static documentation and synthetic screenshots.
The application has no project-operated account system, registration form or
central collection of vault contents. GitHub may process website traffic under
its own policies. The site's source does not include advertising or analytics
scripts maintained by this project.

## User control

The user chooses which vault to open, which entries to select, which printer
to use and whether to export files. Users may close the vault and remove local
settings and exported files from their own computer. Removing application
configuration does not remove printer spool data or copies created elsewhere.

## Security and contact

Do not publish live credentials or unredacted vault screenshots in public
issues. For security reporting guidance, see the
[security policy](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/SECURITY.md).
The source and issue tracker are available at the
[project repository](https://github.com/gamba69/KeePassXC-Card-Print).
