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

Open **Template** and choose **Field mapping** on the left. Set field visibility,
labels, ordering and rendering rules. The global print restrictions below the mapping
editor take precedence over per-field rules. Field captions are user-defined values;
changing the UI language does not translate them automatically.

## 4. Design the front and reverse

Select a template at the top. Create an editable personal copy of a built-in template;
Save stores the complete template, including mapping and both sides. Rename and Delete
are available for personal templates.

Choose General, Header, Fields and values, Footer, Background and colors, Recovery
codes, QR codes, Reverse side or Print and layout from the same left navigation.
Related controls share one group and wrap when the pane is narrow. Numerical fields
use physical units, and supported automatic sizes have a separate Automatic toggle.
Inactive options disappear without losing saved values. Edit the seven shared colors
or apply a color scheme in Background and colors. Individual element color controls
are in their relevant groups: choose a shared role, the original default or an
independent RGB/HEX value. Existing explicit colors are preserved; QR modules stay
black and white. Configure duplex usage and decorative artwork in Reverse side.

## 5. Inspect the preview

Use the existing preview toolbar to navigate cards and sheets, zoom and pan. On the
Template tab, hold Ctrl to outline the element under the pointer and see its name.
Ctrl-click fixes the element and opens its settings; ordinary dragging pans. Choose Actual,
Front, Data back or Empty back to inspect the corresponding side. A labelled synthetic
sample is used when the selected records cannot provide that side; it is never printed
or exported. Printing and export are disabled until you return to Actual. Confirm the content,
page count and physical card slots
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
