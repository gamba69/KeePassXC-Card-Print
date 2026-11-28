# Scan — QR recognition

## ONVIF (11.4.0)

In camera management, use the ONVIF action or Link ONVIF in the network camera editor. Discover IPv4 cameras on local interfaces or enter the device-service URL. Connect using the ONVIF account, choose a media profile, then review the stream URL and transport consent in the ordinary network camera editor. ONVIF and stream credentials can differ. Existing direct URLs remain supported.

The Scan tab exposes ONVIF controls for a linked camera. Available imaging and encoder fields come from the device's reported options and current configuration. Changes to persistent camera settings and presets require explicit confirmation. Restore returns the settings read when the section was opened. The video reader is independent and continues using SharpRTSP or FFmpeg. Control calls use HTTP Digest and/or WS-Security UsernameToken, verified HTTPS, explicit consent for HTTP, bounded responses, and cancellable timeouts. Credentials and endpoints stay in the device-local DPAPI store. Discovery does not send credentials. Devices reporting a different host for a service or stream must be configured using their actual same-host service endpoint; redirects are not followed.

Windows validation: discover cameras on multiple interfaces; test manual endpoints, invalid credentials and untrusted TLS; select both Media and Media2 profiles; test imaging and encoder readback, PTZ and presets; disconnect ONVIF during streaming and verify the independent video path; check cancellation and unchanged legacy profiles. Hardware support and writable fields depend on advertised capabilities. No ONVIF certification is claimed.

The scanner is an independent full-width workspace. It does not require an open
KDBX and does not modify a vault. Open an image/PDF, paste text or an image, drop
an image/PDF into the viewport, or start a camera. The camera uses the Windows
Media Foundation backend of FlashCap, not DirectShow. Select a device and capture
resolution before starting it. Only the current source is retained in memory.

By default, automatic dialogs accept TOTP and KeePassXC Passkey JSON. Enable
**All QR codes** to include HTTP(S) URLs, plain text and supported binary QR byte
segments. A single eligible code opens a result dialog; multiple eligible codes
require a click. Closing a dialog does not discard static source annotations.
Click an annotated code to reopen it without a sound. Rotation and zoom preserve
annotations; a new PDF page or source triggers detection. The live camera pauses
recognition while the dialog is open and suppresses repeated automatic dialogs
until three analyzed frames show other decoded codes or no finder candidates. Leaving the scanner
releases the camera and clears its source. PDF rendering is local, at up to 300
DPI (450 DPI in Maximum sensitivity) and 5500 pixels on the longest edge. Password-protected PDFs are not supported.

The scanner does not write recognized data to settings, logs, the print ledger,
or cloud storage. Only preferences are saved. Result files require explicit
confirmation because they can contain unencrypted secrets. Clipboard copies are
scheduled for clearing only if the clipboard has not changed. As with other
managed UI code, immutable strings and third-party buffers cannot be guaranteed
securely erased from memory. Text QR detection uses ZXing's decoded text; binary
classification uses byte segments and is heuristic, not a version-12 recovery
format. Unreadable QR candidates cannot always be outlined by the decoder.

## Recognition quality and diagnostics (11.0.3)

Auto capture prefers a camera mode close to 1920 × 1080. The independent
preview worker and recognition worker each accept at most one frame; obsolete
frames are not queued. A marked result is shown on its original analyzed frame.
Fast uses the original image, Adaptive adds bounded recovery attempts, and
Maximum uses additional attempts and higher-resolution PDF rendering.
The static-source Scan again action repeats recognition without changing source.

The recognition status in the sidebar distinguishes finder candidates, geometric QR
location, successful decoding, invalid payloads and ineligible formats. The
second line shows actual analyzed resolution, elapsed time, number of attempts,
last strategy, first successful strategy, estimated maximum module width in
source pixels, percentile luminance range and a relative image-detail metric.
Finder candidates are not proof of a real QR. Detail is not an autofocus score;
module estimates are approximate. No payload, source path or frame is logged.

Recovery uses the original resolution first, then alternate global/local
thresholding, percentile contrast normalization, mild sharpening, inversion,
overlapping crops and resampling. Time and pass limits are soft (checked between
attempts), and strategies rotate across live frames. Digital enlargement cannot
restore lost optical detail. Hold a printed card steady with its quiet zone
visible and aim for at least 3–4 actual camera pixels per QR module.

References: [Performance of QR Code Detectors near Nyquist Limits](https://doi.org/10.3390/s22197230),
[ZXing HybridBinarizer](https://zxing.github.io/zxing/apidocs/com/google/zxing/common/HybridBinarizer.html),
[DENSO WAVE module size guidance](https://www.qrcode.com/en/howto/cell.html).

## Verification on Windows

Run `tools/build.ps1` as usual. The Native AOT scanner smoke test validates RFC
6238, classification, QR decoding (including inverted, faint, dense, and multiple QR), blank input diagnostics, and local PDF rendering/decoding. It does not
require a camera or a real vault. In addition, manually check:

- Camera discovery, change device/resolution, start, stop, leave tab and reopen.
- One TOTP, two eligible QR codes in one frame, unrelated URL in application-only
  mode, repeated code suppression and manual re-opening.
- A PDF with several pages and multiple QR codes, rotate, zoom, click after closing
  a dialog, page changes and source switching.
- Image and direct text from the clipboard, drag/drop, Ctrl+V, image file picker.
- TOTP countdown, QR regeneration and a synthetic Passkey JSON round trip.
- URL opens only after confirmation; saving never occurs without confirmation.
- Clipboard replacement by another app is not erased by the expiry timer.
- EN/RU/UK, light/dark themes, narrow windows, and scanner-to-settings navigation.

The next major version can consume the same decoded payload model without using
these per-code result dialogs; archive fragment assembly is not part of 11.0.

## Camera quality (11.0.4)

The scanner reports live relative detail (Laplacian RMS), average luminance,
percentile contrast, and brightness-compensated inter-frame thumbnail change
in the diagnostic sidebar. These are uncalibrated full-frame
heuristics, not measurements of optical focus or motion blur; a sharp background
can hide a blurred QR. The QR module estimate still comes from the decoder.
Compare readings on the same camera, resolution and scene.

A single sampling worker selects the best candidate from up to three recent
frames (350 ms target), using detail, contrast, darkness and inter-frame change.
An independent decoder processes only one selected frame at a time; it does not
queue previous bursts. Preview zoom does not change decoded image resolution.
Freeze and deep scan retains a recent candidate and performs the maximum static
decoding sequence. Resume discards samples and restarts live recognition. The
capture device remains open while frozen but new frames are ignored. All sample
buffers are bounded and cleared on source changes; no files or logs are produced.

The expandable camera control panel probes the selected Media Foundation device
through optional Windows KS filter properties for focus and exposure. Controls
are editable only when the driver reports the corresponding capability; manual
values require an explicit supported stepped range. Apply is explicit, and values
are read back. A driver may refuse a separate control handle or expose only node
or extended camera controls: unavailable does not prove fixed focus. Settings
are not changed automatically or persisted by the application; driver settings
may persist independently. Test auto/manual focus and exposure on supported
hardware, freeze/resume repeatedly, switch sources during decoding, and verify
that unsupported cameras retain full scanning functionality.

References: [Windows camera control properties](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/camera-control-properties),
[KS properties](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/ks-properties).

## Diagnostic workspace (11.0.5)

The source viewport is on the left. The resizable right sidebar contains source
settings, hardware controls, a recognition status, five large quality indicators
with 30-second trend graphs, and the full striped measurement table. The table
shows latest values and nearest-rank P05/P95. The history is bounded to 30 seconds
and 360 samples per metric. Missing measurements are not zeros. Only numeric
telemetry is retained, never image data or QR payloads. Reset clears the history;
source/page/region or hardware changes reset the comparison. Frozen sources retain
a snapshot; static sources have no inter-frame change measurement.

The module size and QR side are estimated in original sensor pixels from finder
geometry and a sampled or inferred QR matrix dimension. A decoded or geometrically
localized QR has its detail, contrast, and brightness measured from original
luminance inside the finder bounds, not from a transformed decoding variant.
The main cards fall back to explicitly labeled selected-area detail and contrast
when a manually selected area has not yielded a localized QR. Whole-frame metrics
remain in the detailed table. A small module (below 2 pixels) is red, 2–3.5 is
marginal, and at least 3.5 is green. Other thresholds are relative heuristics,
not calibrated focus or a guarantee that decoding will succeed. The graph shows
actual measurements on a fixed vertical scale, with time running left to right.

Click Search region and drag a rectangle around the QR including its white quiet
zone. The decoder searches only that region at the original resolution, while the
source image and hit coordinates remain unchanged. Full frame removes the limit.
A new page/source clears the region; rotate and zoom preserve it. Overlapping
half-frame crops are now included in the bounded recovery strategy. The existing
ZXing.Net decoder is retained; no extra native library is required. The self-test
reports clean and blurred dense QR decoding at 1–5 pixels per module, checks the
region crop and original-pixel geometry, and verifies percentile/expiry behavior.
These synthetic results are not a camera benchmark.

## Network cameras (11.1.0)

Choose Network camera, enter the **actual stream URL** supplied by the camera app,
not its web page. Test reads and validates an image without decoding its QR. Save
creates or updates a named profile; changing the name and saving renames it.
The saved profile (including URL tokens and credentials) is encrypted using
Windows DPAPI CurrentUser in the local AppData root, separate from cloud settings.
No frames are intentionally written to disk. Profile settings do not roam.

- HTTP/HTTPS: JPEG snapshots (polling) or multipart MJPEG, Basic/Digest challenge
  authentication. Redirects and proxies disabled. HTTPS system validation by
  default, or exact SHA-256 leaf pin (including self-signed certificates).
  The certificate-only probe shows an **unverified** fingerprint without sending
  HTTP credentials. Verify against the camera before copying it into the pin
  field. Expired certificates and changed pins fail closed.
- RTSP/RTSPS: per-profile engine, **SharpRTSP + Media Foundation** by default.
  SharpRTSPClient 0.8.1 (SharpRTSP transport, SDP/RTP reassembly) receives H.264,
  H.265 and JPEG; the native Windows bridge decodes H.264/HEVC with synchronous
  Media Foundation or direct vendor decoders, with the bundled libde265 HEVC fallback.
  The selected output cadence uses decoded BGRA pixels directly; JPEG is decoded once. The compressed queue is bounded;
  overflow resets the connection rather than dropping arbitrary reference frames.
  Built-in RTSPS uses TCP interleaving and validates the configured hostname and
  system trust, a custom PEM trust anchor, or an explicitly verified leaf SHA-256.
  Pin and custom CA are mutually exclusive; a pin still requires matching host.
- Optional FFmpeg: select FFmpeg in the camera profile, then Manage FFmpeg or
  Settings → FFmpeg. Install/update/reinstall/delete require explicit action.
  The app downloads the pinned LGPL shared build, validates SHA-256 and version,
  retains notices and source links, and swaps the installation only after success.
  No download occurs during Card Print build or ordinary startup. Available version
  is pinned by the Card Print release, not a mutable latest URL. FFmpeg credentials
  in its input URI are visible to local process inspection. FFmpeg RTSPS requests
  TLS verification and TCP but the RTSP demuxer can follow redirects, so transport
  protection is not asserted by Card Print; requires explicit demo-only permission.
  No automatic fallback or removal of an existing TLS pin occurs.
- Plain HTTP/RTSP is blocked unless explicitly permitted per profile. Use only
  synthetic/demo cards on unencrypted streams. Disable WAN/UPnP/cloud/recording
  features in the phone app. Card Print cannot audit third-party phone software.
- OctoStream: enter the RTSP address shown on the phone; explicitly allow plaintext
  for demo tests. IP Camera Lite/Pro: copy the JPEG/MJPEG or RTSP(S) stream URL
  from its own UI. For Pro prefer HTTPS and compare the TLS certificate. Device
  and firmware compatibility must be confirmed in practice.

The right sidebar displays connection status, actual TLS state, reconnect count
 and the existing numeric P05/P95 table. Network FPS and frame interval refer to
frames emitted to Card Print (after configured rate limiting); wait time includes
read/decode pipeline, not network latency. JPEG payload rate is only shown for
HTTP and excludes headers/skipped MJPEG images; RTSP wire bitrate and packet loss are unknown rather than inferred from decoded pixel sizes; the built-in engine reports the negotiated codec. Original resolution
is retained; maximum 24 MiB encoded frame and 32 million pixels. Hardware controls
are unavailable for network sources until a camera control protocol is implemented.
The existing freeze, ROI, best-frame selection and recognition path is shared.
Stop or leave the scanner to close the HTTP stream or terminate FFmpeg.

Build: the Windows packaging script compiles KpcVideoBridge.dll with the same
MSVC/Windows SDK used for the XPS bridge and checks it in the release package.
No FFmpeg is downloaded or included in the main ZIP. The optional installation
lives in device-local AppData, not synchronized configuration; a full settings
reset removes the local profile store. The optional runtime may be removed in
Settings. Neither engine has been validated on every Windows SKU or camera.

### HEVC without Windows codec purchases

SharpRTSP uses Media Foundation first; on a codec/initialization/decoding failure,
HEVC falls back to the bundled CPU libde265 decoder at an independently decodable
access unit. H.264 continues to use Media Foundation; the optional FFmpeg engine
is independent. No network or certificate settings are changed. The frame
format reports the actual backend and bit depth, plus the original MF error.
System decoder inventory is available in the camera dialog. MF tries synchronous,
asynchronous and hardware MFTs that expose NV12/P010. D3D11-aware hardware
transforms use a device manager and read back GPU surfaces; hardware availability
and successful initialization depend on the Windows driver and decoder. Main10 is mapped to 8-bit BGRA for QR
recognition and preview, not HDR tone-mapped. No HEVC extension is needed for
software decoding. libde265 1.1.3 is built as a shared library from the verified
source archive with CMake/MSVC; the exact source and LGPL terms ship alongside it.
HEVC patents and licensing obligations are distinct from LGPL source licensing.

## Decoder selection (11.2.0)

SharpRTSP delivers already-prefixed H.264/H.265 NAL segments. Normalize them once, retain in-band parameter sets and pass raw complete NALs to libde265. If decoding fails on a damaged HEVC access unit, discard the decoder and wait for a new random-access picture; at most two consecutive recovery attempts before trying another permitted backend.

The decoder policy is separate from the RTSP engine: Automatic, Hardware only, Software only, or a named backend. Manual selection has an explicit fallback switch. Hardware candidates include Media Foundation MFTs and direct vendor SDKs associated with individual DXGI adapters. If Windows has no registered compatible hardware MFT, libde265 remains available for HEVC. FFmpeg is still a separate optional engine.

The diagnostics dialog tests real H.264, HEVC Main and Main10 frames, validates dimensions and contrasting test patterns, and reports elapsed processing time to ready BGRA pixels. The small synthetic tests do not certify all real stream resolutions or profiles. Cache is device-local, invalidated by application/OS/adapter/driver/decoder inventory changes and removed on settings reset. Repeat the test manually after driver changes. No camera address or frame is stored in the cache.

## Hardware diagnostics (11.2.1)

Query each DXGI adapter for D3D11 video decoder profiles, NV12/P010 format support and a 1920 × 1080 decoder configuration separately from Media Foundation. Try the adapter-specific hardware MFTs first, then ordinary D3D11-aware MFTs on the selected adapter. For the hardware policy, require an output surface belonging to that device with the decoder binding flag; a CPU buffer is not accepted as verified hardware output. Some drivers may use an intermediate surface, so an unverified result does not establish lack of GPU capability.

The diagnostic table separates missing profiles, missing codec components, initialization failure, unverified GPU output and incorrect pictures. Select a cell for the raw diagnostic and the advertised 1080p configuration. Copy report includes all tested cells. The diagnostic probe uses 24 synthetic 640 × 368 pictures per codec; the original mandatory regression fixtures remain unchanged. Timings include BGRA conversion/readback, not PNG; the 1080p configuration query does not certify real 1080p decoding performance.

## Direct vendor decoders (11.2.2)

The manager now lists a separate direct NVIDIA NVDEC, Intel Quick Sync / oneVPL, or AMD AMF candidate for each corresponding DXGI adapter. The direct SDK bridge loads installed NVIDIA/AMD driver DLLs from System32 and the bundled oneVPL dispatcher from the application directory; Intel legacy Media SDK is attempted when the modern dispatcher path cannot initialize a session. No vendor runtime installers, HEVC Store extension or FFmpeg are required by the direct SDK path. Actual support depends on GPU, driver, codec, profile and stream. The existing test pictures are decoded and inspected by the manager; successful direct candidates participate in automatic selection and manual choice. An unavailable runtime or unsupported profile is not a corrupt-frame error. Direct backends return NV12/P010 readback converted to SDR BGRA; 4:2:0 only, no HDR tone mapping, and bounded allocations. Automatic fallback retains the existing random-access-frame policy and never changes transport security.

The build pins nv-codec-headers, oneVPL dispatcher and AMF SDK source commits, packages the direct bridge and dispatcher and retains the upstream notices. GPU paths must be verified on real Windows drivers; probe success on the small test pictures is not proof of 1080p capability or all stream formats.

## Pixel pipeline and decoder policy (11.2.5)

Native decoders provide owned BGRA pixels; HTTP JPEG/MJPEG is decoded once. FFmpeg uses uncompressed 32-bit BGRA BMP framing over its stdout pipe to retain per-frame dimensions (including resolution changes), without PNG compression or decompression. No files are created. Compressed-input limits and decoded-pixel limits remain separate. The preview, burst selection, freeze and QR recognition share reference-counted pixel buffers; the preview framebuffer is reused when dimensions match. The last lease wipes the owned pixels.

Automatic selection ranks successful decoder probes by time per delivered picture with a P95 call-time penalty, not by GPU or vendor. Failed and untested candidates rank below successful ones, and unavailable profiles/runtimes are excluded from automatic plans. Hardware-only and software-only remain strict classes. A named decoder is strict by default; enable fallback explicitly if desired. Old probe timings are invalidated. The small test measures decoded pixels, not camera latency or 1080p performance. The live dashboard separately reports time spent decoding an emitted access unit (including BGRA conversion/readback), frame interval and QR recognition time; FFmpeg pipe wait is not presented as decoder-only time.

## Scanner workspace (11.3.0)

The camera selector lists detected local cameras followed by saved network cameras,
identified by source type. Selecting a camera never starts a stream. Start opens the
selected camera; the same button stops a running camera, cancels a network connection
or retries a failed network session. Switching cameras stops the previous session.
Use **Cameras…** to add, edit, delete, refresh or explicitly select a camera. The
profile editor tests a connection independently of saving it; cancelling the test
keeps the editor open. A saved profile remains device-local and DPAPI-protected.
Only an opaque network profile ID is remembered in a separate device-local selection
file; the settings reset removes it together with the profiles.

File and clipboard imports prepare the new source before replacing the current one;
a cancelled picker, unreadable image or empty clipboard does not clear the previous
source. Live recognition pauses during import/management; preview can continue. PDF
page changes render the replacement before installing it. Clear stops the stream and
removes the displayed content and results.

The right panel follows the source: local resolution and driver controls, network
format and decoder selection, or static image/PDF metadata. With SharpRTSP, changing
a decoder saves the profile immediately (since 11.3.5), then restarts an active
session. Named decoders keep the explicit fallback policy. Network warnings
remain visible using theme text; technical details and full quality/diagnostic charts
are collapsible. No video-decoding or QR-recognition algorithms are changed here.

## Input modes (11.3.1)

Choose Camera, File or Clipboard before using the corresponding controls. Camera
shows the device selector, the camera manager gear and Start/Stop/Cancel/Retry.
File shows Open file and PDF navigation when applicable. Clipboard shows Paste.
Changing input mode explicitly releases the previous source without starting
anything. Dropping a file or pressing Ctrl+V changes the mode only after a
successful import; cancelled or failed imports retain the existing source. Clear
removes static content or dismisses a failed source without changing the selected
mode. Stop/Cancel manages a running camera. The sidebar contains source settings rather than duplicate actions.

## Diagnostics layout refresh (11.3.2)

The connection action reads **Abort connection** while connecting or retrying.
The sidebar uses compact themed disclosure buttons rather than default Expander
surfaces. Decoder selection shares a row with its label; the apply and test
buttons share a wrapping row. Camera diagnostics are visible without expanding
anything. Seven compact quality cards include frame detail and contrast. The
complete measurement table follows the cards, reflowing to two side-by-side
sets of columns at a usable width of 780 DIPs, and back to one below it.
Recognition, network transport and decoder selection algorithms are unchanged.

## Scanner refinements (11.3.3)

The network sidebar filters decoder choices using completed probes and the actual
H.264 / HEVC profile as soon as it is known. Unknown test results remain eligible
until tested; an unavailable saved preference is not silently replaced. The
measurement table uses one, two or three groups depending on available DIPs.
Whole-frame detail is scene-dependent, not an absolute focus or motion score.
The plaintext permission remains part of the encrypted local camera profile;
changing the endpoint or RTSPS engine requires renewed consent. The unencrypted
transport warning remains visible. Secondary windows are limited to the target
monitor working area, with scrollable bodies for simple forms.

## Scanner refinements (11.3.5)

Camera management uses the mapping table palette. Decoder diagnostics put the
compact result table first, then the summary and selected result details, in a
scrollable body with a fixed footer. Decoder choice and fallback changes are
saved immediately; a failed save restores the previous selection without
reconnecting. Programmatic list refreshes never commit a new choice. Connection
errors and nonzero reconnect counts are shown inline. Image tools below the
preview and camera inventory actions use compact icons with localized tooltips;
primary source and dialog confirmation actions remain labeled.

## Compact scanner settings (11.3.6)

The connection summary shows transport, actual format/decoder, dimensions, FPS and the security state without duplicated labels. The gear beside the decoder selector opens diagnostics. The detailed measurements hide unavailable current values, retain measured zeroes and preserve the rolling history for values that reappear. Duplicate frame brightness is omitted and buffer sizes use MiB. Independent table groups adapt to the sidebar width. Camera video and connection settings are always visible when applicable; the screen-bounded editor scrolls its body while the action buttons remain fixed.
