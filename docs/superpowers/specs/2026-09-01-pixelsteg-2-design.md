# PixelSteg 2.0 design

## Purpose

PixelSteg 2.0 turns the existing deterministic file-to-PNG container into a
real cover-image system. A user chooses a lossless PNG and a message, one file,
or a small file bundle. PixelSteg shows the capacity of four embedding profiles
and creates a visually equivalent PNG. On recovery it automatically identifies
the profile and returns typed bundle entries as bytes or UTF-8 text. It never
opens or executes recovered files.

The existing `encode` and `decode` commands remain available for version-1
containers. The new workflow is exposed as `embed-file`, `embed-message`,
`extract`, `read-message`, and `inspect`.

## Product boundaries

- PixelSteg is a local privacy and file-packaging utility, not an evasion tool.
- Its format and algorithms are public and documented.
- It does not claim resistance to steganalysis or lossy recompression.
- Version 2 accepts 8-bit, non-interlaced RGB and RGBA PNG covers.
- Storage uses only fully opaque pixels because image tools may rewrite RGB
  values hidden under transparent and partially transparent pixels.
- JPEG, WebP, palette PNG, grayscale PNG, interlacing, network transfer, sample
  execution, and automatic file opening are outside the 2.0 scope.

## User workflows

### Embed

1. Select a cover PNG, message or file bundle, profile, and output PNG.
2. PixelSteg validates the cover and reports usable bytes for all profiles.
3. Compression is enabled by default. A password is optional.
4. PixelSteg refuses insufficient capacity or an existing output.
5. The result reports profile, changed channels, capacity ratio, maximum channel
   delta, MSE, PSNR, and SSIM.

### Extract

1. Select a PixelSteg carrier and destination directory.
2. PixelSteg reads a common locator and selects the body profile automatically.
3. Encrypted carriers request a password. Authentication completes before any
   recovered file is written.
4. The bundle parser validates entry counts, names, sizes, media types, and
   SHA-256 digests.
5. Every filename is contained inside the selected directory and overwrite is
   explicit. Message entries may be displayed or saved as text.

### Inspect

Inspection reports dimensions, color mode, per-profile capacity, whether a
version-2 locator is present, selected profile, payload size, compression,
encryption, and error-correction mode. It does not recover or execute content.

## Architecture

`PngCodec` is a dependency-free PNG boundary. It validates signatures, chunk
CRCs and ordering, enforces allocation limits, concatenates IDAT data, supports
PNG filters 0-4, and returns an in-memory `PngImage`. The writer preserves RGB
versus RGBA mode and emits a standards-compliant non-interlaced PNG.

`PayloadBundleCodec` produces a bounded versioned bundle containing one UTF-8
message or up to 64 file entries. Each entry records its kind, safe display
name, media type, byte length, SHA-256, and content.

`StegoEnvelope` optionally compresses the bundle with Brotli. When a password is
supplied it derives a 256-bit key with PBKDF2-HMAC-SHA-256 and authenticates the
envelope with AES-256-GCM. Salt, nonce, iteration count, flags, and ciphertext
length live in the common locator. Unencrypted bundles retain per-entry SHA-256
validation.

`StegoCodec` owns a registry of four documented profiles. Each profile starts
with the same locator, stored one bit per stable RGB channel in row order. The
body excludes those locator channels:

- **Balanced** writes one bit per remaining RGB channel in row order. Values
  change by at most one.
- **Dense** writes two bits per remaining RGB channel. Values change by at most
  three and useful capacity nearly doubles.
- **Adaptive** ranks 8×8 blocks by luminance variance calculated after masking
  the two low RGB bits. It writes one bit per channel in the most textured
  blocks and uses a salt-seeded deterministic shuffle to spread adjacent bytes.
- **Resilient** uses the same stable adaptive map plus deterministic interleaving
  and Hamming(12,8) single-bit correction. Recovery reports corrected codewords
  and still requires bundle/AES validation before content is accepted.

The adaptive ranking remains stable after embedding because it ignores every
bit a profile may alter. Profiles are public compatibility choices, not claims
of resistance to steganalysis. `Inspect` reads the locator rather than guessing
from image statistics.

`ImageQuality` compares the decoded cover and result and returns changed-channel
count, maximum channel delta, mean squared error, peak signal-to-noise ratio,
global luminance SSIM, and used-capacity ratio.

The CLI and WPF application compose these core services. Neither layer contains
PNG, cryptographic, bundle, or embedding logic.

## Version-2 locator

All integers are little-endian.

| Field | Bytes | Meaning |
| --- | ---: | --- |
| Magic | 4 | ASCII `PST2` |
| Version | 1 | `2` |
| Profile | 1 | balanced, dense, adaptive, or resilient |
| Flags | 1 | bit 0 compressed, bit 1 encrypted |
| Bits per channel | 1 | `1` or `2`, checked against the profile |
| Locator length | 2 | fixed size for forward rejection |
| Envelope length | 8 | decoded body length |
| PBKDF2 iterations | 4 | zero when unencrypted |
| Salt | 16 | random seed; also used for PBKDF2 when encrypted |
| Nonce | 12 | random when encrypted, otherwise zero |
| Authentication tag | 16 | AES-GCM tag, otherwise zero |
| Locator CRC-32 | 4 | corruption check before profile selection |

The locator is exactly 70 bytes and is embedded MSB-first, one bit per stable
RGB channel. The selected
profile controls only the envelope body. Legacy containers remain independently
decodable through `encode` and `decode`.

## Password handling

The GUI uses a password box and clears its temporary managed copy after the
operation. The CLI accepts `--password-stdin` or `--password-env NAME`; a
literal command-line password is not supported. Empty passwords mean no
encryption. Authentication failures use one neutral error message so corruption
and wrong passwords are not distinguished.

## Error handling

All format, correction, and capacity errors become `PixelStegException` with
actionable, non-sensitive messages. Inputs open read-only. Outputs use
`AtomicFileWriter`, are never silently replaced, and remain absent when
validation, authentication, cancellation, or writing fails.

## Testing

Tests use small synthetic images and inert text. They cover all five PNG
filters, RGB/RGBA round trips, CRC and truncation rejection, transparent-pixel
exclusion, exact capacity boundaries for all profiles, stable adaptive ordering,
dense maximum deltas, Hamming correction, locator auto-detection, wrong
passwords, tampering, compression, multi-entry bundles, legacy compatibility,
filename containment, CLI exit codes, and view-model state. No executable or
real hidden corpus is stored in the repository.

## Documentation and release

The README leads with what the tool does, a screenshot, profile comparison, and
a short example. One compact “Limits” section replaces repeated disclaimers.
The format document specifies locator, bundle, profile, and compatibility
contracts. CI builds, tests, and checks formatting. Tagged releases produce
Windows CLI and desktop archives with SHA-256 checksums.
