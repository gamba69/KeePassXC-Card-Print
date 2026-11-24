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

## Template — Field mapping

Select **Template** in the main tab bar, then **Field mapping** in its left navigation.
The mapping editor assigns source fields to roles, groups and the card's
relative order. A field can have a label, visibility policy, typography,
formatting and optional numeric grouping. Blank or whitespace-only values are
not printed, regardless of the visibility policy; their labels and headings
are omitted too. Entries with no printable values produce no cards. Long
secrets, recovery codes and TOTP fields receive dedicated layout handling. Compact fractional gaps avoid
ordinary full-space characters in grouped secrets.

The global print restrictions below the mapping editor apply to every entry.
A prohibited field cannot be printed even if its individual mapping rule allows it.
If allowed globally, the per-field rule decides its visibility and formatting.

## Template — Design

Selecting a template loads its mapping and appearance together. Built-in templates
are read-only; use **Create** to make a personal copy, then **Save** to persist
both the mapping and the appearance. Rename and Delete apply to personal templates.

The left navigation groups controls by their purpose:

| Section | Controls |
| --- | --- |
| General | Card dimensions, spacing density, style and frame |
| Field mapping | Individual field rules and global print restrictions |
| Header | Entry title, group, icon and header geometry |
| Fields and values | Labels, text, spacing, long values and notes |
| Footer | Footer geometry, UUID and date |
| Background and colors | Shared palette and face-specific background inheritance |
| Recovery codes | Columns, checkboxes and automatic font-size limits |
| QR codes | TOTP layout and QR dimensions |
| Reverse side | Duplex behaviour, blank or decorative reverse, captions, emblem and rules |
| Print and layout | Template cut guides and duplex alignment |

Related typography settings are grouped together and wrap onto additional rows
as the pane narrows. Measurement controls show their units. For header height
and reverse caption sizes, select **Automatic** to use the existing calculated
size, or clear it to enter a manual value. Dependent controls are hidden while
inactive; hiding them does not reset their stored values.

## Template — Colors

In Background and colors, edit the seven shared palette colors or choose a color
scheme. Each printable element has its own color control in its relevant group.
Choose a palette role to link the element, Default to retain its original style
behavior, or Individual color to set an independent RGB/HEX value. Applying a
scheme changes the seven shared colors only. Existing personal templates retain
their previous explicit colors. QR modules and their quiet zone remain black and white.

## Template — Reverse side

Enable duplex and choose whether the reverse can contain continued data or a
second entry. Select the empty-side behaviour separately. Background and colors
contains the shared background, front and data-back overrides, and the empty-back
source (shared, front, data back, custom or white). Decorative captions, emblem
and rules are in Reverse side. Always inspect both sides before printing or exporting.

## Preview and diagnostics

Use the existing preview toolbar for page/card navigation, zoom and pan. In Template,
hold Ctrl while moving the pointer to outline one element. Ctrl-click fixes the
selection and jumps to its settings. Release Ctrl to clear temporary hover; the
fixed selection remains. Ordinary dragging pans the preview. The name appears in
a small overlay in the viewport. Neither the outline nor the label is printed.
Use Actual, Front, Data back or Empty back to inspect the desired side. If a selected
record cannot supply the chosen side, a labelled synthetic example is shown. The
sample never enters the print job. Return to Actual to enable printing/export.
Actual mode remains empty with no selected entries.
The preview renders from a vector display list and prioritizes the visible area. It can
preserve the current viewport while editing layout settings. The About tab
provides dependency versions and performance metrics. Avalonia debug overlays
can be enabled temporarily for troubleshooting; they are not a substitute for
measuring printed output.

For release validation, see the [template editor regression checklist](template-editor-validation.md).

## Handling printed cards

Use a trusted printer and keep pages under your control. Confirm exactly what
was printed; do not automatically repeat a job after an ambiguous spooler
failure. If data may have been exposed, consider rotating the affected secrets.
