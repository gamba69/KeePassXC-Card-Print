# Template editor regression checklist

The automated layout tests cover descriptor ownership, dependency rules, automatic
size values, built-in protection, localization, and hit-region geometry. The build
script also checks the single preview, compact field layout and navigation glyphs.

Complete the following on the Windows build before release. Use Demo or a disposable
synthetic database; do not capture real credentials.

| Area | Acceptance criteria |
| --- | --- |
| Layout | At minimum and maximized window widths, each section is accessible. No clipping, overly stretched numeric fields, or inaccessible mapping rows. |
| Themes and languages | Switch between light/dark/system and RU/UK/EN without restarting. Check all section labels, tooltips, choice lists and navigation glyphs. |
| Template lifecycle | Built-in fields remain read-only; create, rename, save, switch and delete personal templates. Cancel a dirty-template switch. |
| Existing templates | Load a template from 9.2.5 or 10.0.x, compare its printed appearance, save and reload. Missing new color properties must retain the original output. Toggle parent controls and verify hidden values are retained. |
| Palette | Edit each shared role and apply all four schemes. Check linked colors on every face, custom HEX overrides, switching between roles, cancellation, read-only presets and unchanged noncolor settings. Verify the QR contrast. |
| Entry icon coloring | Apply each scheme to a colored synthetic icon. Check original, grayscale, palette and individual overrides with all existing processing modes; verify alpha, tonal details, persistence, read-only state and matching preview/print/export. |
| Mapping | Deny a category globally; confirm a per-field allow cannot override it. Restore the restriction and verify mapping rules still apply. |
| Preview | Move with Ctrl held over header, labels, values, QR, recovery codes, UUID, date, background, frame and reverse art at several zoom levels and in sheet mode. Only the hovered element is outlined; Ctrl-click fixes the element and opens its settings. Verify Ctrl-hover and the localized viewport label from every main tab; Ctrl-click must navigate to Template and fix the chosen element. Releasing Ctrl clears hover without discarding selection. Escape, a plain left-click in preview, a repeated Ctrl-click on the selected element, a Ctrl-click on empty space, switching preview mode or leaving Template clear the fixed outline and caption without changing the selected section or dirty state. Ordinary clicks must not select and ordinary dragging must still pan. Verify pointer exit and drag cancellation. |
| Forced sides | Test all three faces with zero selection, one selected entry and a duplex continuation. Uncheck records during rendering; old account data must disappear immediately. |
| Export and print | Return to Actual; verify identical selected cards, PDF and raster output, duplex positioning and print tracking. No editor highlights or synthetic samples may enter the output. |
| Cloud | Save a personal template in each supported storage mode. Synchronize changes, restart, reload and test unsaved-edit protection. |
| Documentation | Capture the current interface using synthetic records only, including compact typography, mapping, background, and reverse-side sections. Replace the archived gallery images after verifying the running Windows build. |

Record the build and test results as release evidence. The checklist itself does
not imply that these manual tests have passed.
