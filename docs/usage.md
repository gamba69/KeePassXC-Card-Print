# Application workflow

## Database and entries

Choose a local KeePassXC-compatible `.kdbx` database. If your configuration
uses a separate key file, select it as well. Enter the master password only in
the application; do not paste it into issue reports. An optional YubiKey
challenge-response configuration is available where supported.

The table contains a group, entry name, print status and selection control.
The text filter narrows the list. Quick filters help find entries that have not
been printed or need additional printing; when enabled, quick filters and text
matching work together. The print selection is explicit: with zero selected
entries, the preview contains no account cards and printing/export are disabled.

Selection and print-tracking status are separate concepts. Selecting an entry
does not automatically mark it printed. Print status must be confirmed after
physical output is verified.

## Mapping

The mapping editor assigns source fields to roles, groups and the card's
relative order. A field can have a label, visibility policy, typography,
formatting and optional numeric grouping. Blank or whitespace-only values are
not printed, regardless of the visibility policy; their labels and headings
are omitted too. Entries with no printable values produce no cards. Long
secrets, recovery codes and TOTP fields receive dedicated layout handling. Compact fractional gaps avoid
ordinary full-space characters in grouped secrets.

## Design

A card preset supplies a complete initial appearance. Built-in presets are
read-only; create a personal template to modify and save custom settings.
Configure typography, card backgrounds, label sizes, icon behaviour,
header/footer spacing and other dimensions. Measurement controls use the
units shown in the interface; printed pages are constructed from millimetre
geometry, not screen pixels.

## Two sides

Configure whether reverse-side data is printed and how an empty reverse is
drawn. Front and back settings are separate where the editor provides an
override. Always inspect both sides before printing or exporting.

## Preview and diagnostics

Use the preview toolbar for page/card navigation, zoom and pan. The preview
renders from a vector display list and prioritizes the visible area. It can
preserve the current viewport while editing layout settings. The About tab
provides dependency versions and performance metrics. Avalonia debug overlays
can be enabled temporarily for troubleshooting; they are not a substitute for
measuring printed output.

## Handling printed cards

Use a trusted printer and keep pages under your control. Confirm exactly what
was printed; do not automatically repeat a job after an ambiguous spooler
failure. If data may have been exposed, consider rotating the affected secrets.
