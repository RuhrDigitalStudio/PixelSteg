# Changelog

## 2.0.0-rc.1 - 2026-09-01

- Added four carrier-based steganography profiles for existing RGB and RGBA PNG images.
- Added typed bundles containing messages, multiple files, media types and per-entry SHA-256 hashes.
- Added optional Brotli compression and authenticated AES-256-GCM password protection.
- Added carrier inspection, exact capacity reporting and MSE, PSNR and SSIM quality metrics.
- Reworked the Windows app around separate hide and reveal workflows.
- Added script-friendly CLI commands for embedding, inspection, extraction and message output.
- Documented the v2 byte layout and deterministic placement rules.

The release candidate is intentionally limited to lossless 8-bit true-colour PNG. Editing or recompressing a carrier may destroy its payload.
