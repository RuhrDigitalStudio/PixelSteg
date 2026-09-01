# Security policy

PixelSteg reads and writes local files. It has no network feature and never opens or executes recovered content.

## Supported versions

Security fixes are made on the current `main` branch and the newest published release. Older source revisions are not maintained.

## Reporting a vulnerability

Please use GitHub's private vulnerability-reporting form for this repository. Include the affected revision, platform, expected result and a small non-sensitive reproducer. Do not attach executable payloads or publish the issue before the maintainers have had a reasonable opportunity to investigate it.

## Security boundary

Password-protected v2 bundles use AES-256-GCM with a key derived by PBKDF2-HMAC-SHA-256 (600,000 iterations and a random 16-byte salt). This protects the bundle content and authenticates its locator metadata. Unprotected bundles rely on locator CRC32 and per-entry SHA-256 for corruption detection; those hashes are not sender authentication.

The decoder applies bounds before allocation, validates PNG chunk CRCs, rejects unsupported PNG layouts, verifies bundle structure and hashes, and reduces recovered entries to safe local names. Extraction does not make unknown content trustworthy. Scan and inspect recovered files before opening them.

The embedding format is documented and detectable. PixelSteg does not promise steganographic anonymity, resistance to statistical analysis, or survival after image transformations.
