---
layout: default
title: User guide
permalink: /guide/
---

<nav class="site-nav" aria-label="Documentation navigation">
  <a href="{{ '/' | relative_url }}">Overview</a>
  <a class="active" href="{{ '/guide/' | relative_url }}">User guide</a>
  <a href="{{ '/gallery/' | relative_url }}">Gallery</a>
  <a href="{{ '/privacy/' | relative_url }}">Privacy</a>
  <a href="https://github.com/gamba69/KeePassXC-Card-Print">GitHub</a>
</nav>

# User guide

This guide describes the current Windows desktop workflow. The application is
portable and compiled for Windows x64; Linux and macOS builds are not provided.

## 1. Open a vault or explore demo mode

Download a portable build from the [GitHub releases](https://github.com/gamba69/KeePassXC-Card-Print/releases),
extract it into a directory you control and run `KeePassXCCardPrint.exe`.
On **Database and entries**, select your `.kdbx`, an optional key file and the
supported authentication method, then unlock it locally. Alternatively, use
**Demo** to load synthetic records without opening your own database; this is
suitable for exploring the interface or making screenshots.

![Database and entries tab with demo entries]({{ '/images/database-and-entries.png' | relative_url }})

## 2. Select the entries to print

Use the search box and selection controls to choose individual entries. The
status column reports print tracking separately from selection. With no
selected entries, no account cards are shown in the preview and output is
disabled. Only confirm printing after checking the actual printed pages.

## 3. Review field mapping

Use **Field mapping** to control which source fields are rendered and how they
are labelled, grouped and ordered. Field captions, including those explicitly
configured in mapping, are user-defined values; changing the UI language does
not translate them automatically.

![Field mapping editor]({{ '/images/field-mapping.png' | relative_url }})

## 4. Design the front and reverse

Choose a built-in template as a starting point, or save an editable personal
template. The **Appearance** editor covers dimensions, headers and footers,
fonts, field visibility, icons, colors, backgrounds, TOTP and recovery codes.
The **Duplex** editor controls the reverse side, including an empty reverse
with optional artwork and text.

<div class="image-pair">
  <figure><img src="{{ '/images/appearance-fonts.png' | relative_url }}" alt="Font and typography options"><figcaption>Typography and field spacing.</figcaption></figure>
  <figure><img src="{{ '/images/duplex-background-icon.png' | relative_url }}" alt="Reverse artwork controls"><figcaption>Reverse-side artwork options.</figcaption></figure>
</div>

## 5. Inspect the preview

Use the preview toolbar to navigate cards and sheets, switch between front and
reverse, zoom and pan. Confirm the content, page count and physical card slots
before committing paper. For dense secrets and recovery codes, verify
readability at the printed size rather than relying solely on a zoomed preview.

## 6. Configure printing and export

Choose the printer and check its reported duplex and resolution capabilities.
Confirm the paper size, sheet slots, feed direction, manual flipping and
front/reverse calibration. Test with non-sensitive data before producing
real credential cards. Windows printing uses XPS vector output where supported,
with GDI raster fallback under the application's supported failure rules.
PDF and raster exports contain sensitive information and should be handled
like plaintext secret documents.

## 7. Protect the output

- Keep printed cards out of sight and store them securely.
- Retrieve printouts immediately; do not assume the operating system or
  printer clears its spool files.
- Avoid sharing screenshots from a real unlocked vault.
- Back up your settings intentionally, and remember that settings may include
  local file paths, entry identifiers or print tracking metadata.

Read the more detailed references on
[printing](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/docs/printing.md),
[configuration](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/docs/configuration.md),
[troubleshooting](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/docs/troubleshooting.md)
and [security](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/SECURITY.md).
