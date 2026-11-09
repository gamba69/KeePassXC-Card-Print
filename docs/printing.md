# Printing and exporting

## Physical geometry

Cards and paper are modelled in millimetres. The sheet composer positions
cards in physical pockets; an A4 sheet supports a contiguous 3×3 pocket grid
for the configured card size. Paper options include A3, A4, A5, A6, Letter and
Legal where applicable. Reprinting can start at a selected physical slot;
verify the grid and printer margins before using expensive paper.

## Windows printing backends

Windows-native code resides exclusively in `platforms/windows/printing/` and
`platforms/windows/native/`:

- **XPS Object Model:** native Windows bridge `KpcXpsBridge.dll` packages
  vector geometry, with embedded font resources or vector outlines as needed.
- **GDI raster:** uses a driver-selected print device and rasterized output.
  Supported resolution reporting comes from the printer driver; it is not an
  unconditional claim that a printer supports 300/600/1200 DPI.

The shared `PrintDocumentPipeline` forms pages independently of the printer.
The `IPrinterService` adapter owns printer discovery, capabilities, device
properties and submission. The application only retries from vector to raster
when it can establish that the first attempt submitted **nothing**. An
ambiguous failure must not cause an automatic duplicate print.

## Duplex and manual flipping

Choose the appropriate mode after reviewing the printer's actual capabilities.
Hardware duplex is not the same as guided manual flipping. Manual duplex
pairs front and back pages for each physical sheet, with explicit prompts
between passes. Single-sided sequential output prints the selected pages in
physical page order without requiring a sheet-flip procedure. Back-side
mirroring depends on the long-edge or short-edge setting.

The application presents confirmations for print operations that may produce
files, such as PDF virtual printers. After submitting a job, independently
verify the physical pages before marking entries printed.

## Calibration

Front and reverse calibration offsets compensate for mechanical printer or
paper-feed variation. Start with a non-sensitive test sheet, measure actual
alignment and apply small X/Y corrections. A PDF viewer, printing dialog or
driver can silently scale pages; disable fit-to-page scaling where appropriate.

## Export

PDF export is vector where the configured renderer supports it. Raster export
uses the requested resolution. Both forms can contain plaintext secrets.
Export destinations are chosen by the user; do not upload exported documents
to issue trackers, analytics systems or untrusted cloud services.
