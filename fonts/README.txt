Portable fonts for KeePassXC Card Print 6.0.9 (SIL OFL 1.1)

These eight original variable TTFs and their eight license texts are committed
in the source repository, rather than downloaded during every build. A fresh
checkout already contains all application-specific specialist fonts.

The same Recursive variable TTF serves both Recursive Mono (MONO=1) and
proportional Recursive (MONO=0), giving nine choices from eight files.

PORTABLE_FONT_FILES.txt contains the exact eight filenames in the release.
PORTABLE_FONT_LICENSES.txt contains the exact eight accompanying OFL texts.
FONT_SHA256.txt pins the byte-for-byte source revisions of the eight TTFs.
The offline verifier tools/verify_fonts.ps1 checks all these files and licenses.

The Windows build copies only the approved eight TTFs and eight OFL license
texts to publish/fonts/; that directory is included in the release ZIP.
No application font installation, network font download, or Python is needed.

Windows UI font families (Segoe UI, Arial, Calibri, Tahoma, Verdana and
Trebuchet MS) are resolved from Windows and are not redistributed.

The license for each font is SIL Open Font License 1.1. Bundling font files
under this license does not change the application's separate MIT license.
The same license texts accompany the redistributable font binaries.

Note: Recursive has limited Cyrillic coverage. When necessary, Windows
font fallback handles unsupported glyphs.
