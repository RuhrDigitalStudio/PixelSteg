# Security policy

PixelSteg is a local, non-executing file container. It does not promise secrecy, malware protection, or safe handling of unknown files after extraction.

## Reporting a vulnerability

Do not post suspected vulnerabilities in public issues. Contact the maintainers through the repository's private security-reporting channel and include a minimal, non-malicious reproduction. Do not attach executable payloads.

## Safety boundary

The application validates PNG structure, container limits, and SHA-256 before writing decoded bytes. It never loads, opens, executes, downloads, or interprets decoded content. Users remain responsible for scanning and opening extracted files safely.
