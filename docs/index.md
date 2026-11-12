---
layout: default
title: KeePassXC Card Print
permalink: /
---

<nav class="site-nav" aria-label="Documentation navigation">
  <a class="active" href="{{ '/' | relative_url }}">Overview</a>
  <a href="{{ '/guide/' | relative_url }}">User guide</a>
  <a href="{{ '/gallery/' | relative_url }}">Gallery</a>
  <a href="{{ '/privacy/' | relative_url }}">Privacy</a>
  <a href="https://github.com/gamba69/KeePassXC-Card-Print">GitHub</a>
</nav>

<p class="eyebrow">LOCAL-FIRST · DESKTOP CARD DESIGNER</p>

# Turn KeePassXC entries into print-ready cards

Prepare compact, readable cards for secure physical storage, with precise
front-and-back control, a live preview and printer-aware output.

![KeePassXC Card Print illustrated interface collage]({{ '/images/hero-collage.png' | relative_url }})

<div class="cta-strip">
  <a class="cta-primary" href="{{ '/guide/' | relative_url }}">Get started</a>
  <a class="cta-secondary" href="{{ '/gallery/' | relative_url }}">Explore screenshots</a>
  <a class="cta-secondary" href="https://github.com/gamba69/KeePassXC-Card-Print/releases">Downloads on GitHub</a>
</div>

<div class="feature-grid">
  <div class="feature-card"><span class="feature-kicker">01 / IMPORT</span><h3>Start from your KDBX</h3><p>Open a local KeePassXC-compatible database and explicitly select which entries to print.</p></div>
  <div class="feature-card"><span class="feature-kicker">02 / DESIGN</span><h3>Control every detail</h3><p>Fine-tune field mapping, typography, QR codes, recovery codes, spacing and card backs.</p></div>
  <div class="feature-card"><span class="feature-kicker">03 / PREVIEW</span><h3>See the physical layout</h3><p>Inspect front, reverse and sheet placement before using paper or sending a job to a printer.</p></div>
  <div class="feature-card"><span class="feature-kicker">04 / OUTPUT</span><h3>Print with confidence</h3><p>Use vector or raster Windows printing, calibration and manual duplex where appropriate.</p></div>
</div>

## An editor built around the printed result

<div class="image-pair">
  <figure>
    <img src="{{ '/images/database-and-entries.png' | relative_url }}" alt="Database entries and live page preview in the desktop app">
    <figcaption>Choose records from an intentionally synthetic demo vault.</figcaption>
  </figure>
  <figure>
    <img src="{{ '/images/appearance-header-footer.png' | relative_url }}" alt="Card header and footer editor with an enlarged card preview">
    <figcaption>Edit front-side layout while viewing the actual card.</figcaption>
  </figure>
</div>

## Key capabilities

| Area | What is available |
| --- | --- |
| Database | Local `.kdbx` reading, optional key file and supported YubiKey challenge-response |
| Layout | Front and reverse templates, field mapping, font choices and custom labels |
| Secrets | TOTP QR codes and recovery-code columns with print checkboxes |
| Paper | A3, A4, A5, A6, Letter and Legal; selectable physical card positions |
| Printing | Windows XPS vector backend, GDI raster fallback, alignment calibration and duplex options |
| Export | Vector PDF and raster export options where available |
| Interface | English, Ukrainian and Russian; dark, light and system themes |
| Packaging | Portable Windows x64 Native AOT application; no installed .NET runtime required |

## Privacy is not a footnote

The application opens vault data locally to prepare the preview and print
output. **Printed cards, exported documents and printer queues can contain
plaintext secrets.** Keep them physically and digitally protected. The screenshots
on this site contain randomly generated **demo data**, not actual credentials.

Read the [privacy policy]({{ '/privacy/' | relative_url }}) and the
[security policy](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/SECURITY.md).

## Explore further

- [User guide]({{ '/guide/' | relative_url }}) — from demo mode through printing and export.
- [Full screenshot gallery]({{ '/gallery/' | relative_url }}) — all major editor sections and reverse-side options.
- [Building](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/docs/building.md) and [printing reference](https://github.com/gamba69/KeePassXC-Card-Print/blob/main/docs/printing.md) — technical details.
- [Release downloads](https://github.com/gamba69/KeePassXC-Card-Print/releases) and [issue tracker](https://github.com/gamba69/KeePassXC-Card-Print/issues) — source and community.

<p class="legal-note">KeePassXC Card Print is an independent project, not affiliated with or endorsed by the KeePassXC project.</p>
