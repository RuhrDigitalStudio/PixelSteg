# PixelSteg format v2

This document specifies the `PST2` carrier format written by PixelSteg 2. Integer fields are little-endian unless a section says otherwise.

## Carrier image

The carrier is a non-interlaced PNG with bit depth 8 and colour type 2 (RGB) or 6 (RGBA). Decoders process pixels in row-major order and channels in R, G, B order. Alpha is never modified. A pixel participates only when its alpha value is 255; RGB images are decoded internally with alpha 255.

PNG filters 0 through 4 are accepted. PixelSteg writes filter 0. Ancillary chunks do not affect channel placement.

## Locator

The first 560 eligible RGB channels carry a fixed 70-byte locator. Each channel stores one bit in its least-significant bit. Bytes are written from most-significant bit to least-significant bit.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | ASCII `PST2` |
| 4 | 1 | version: `2` |
| 5 | 1 | profile: balanced `1`, dense `2`, adaptive `3`, resilient `4` |
| 6 | 1 | flags: bit 0 compressed, bit 1 encrypted |
| 7 | 1 | body bits per channel: `2` for dense, otherwise `1` |
| 8 | 2 | locator size: `70` |
| 10 | 8 | protected body length in bytes |
| 18 | 4 | PBKDF2 iterations: `600000` when encrypted, otherwise `0` |
| 22 | 16 | random salt; also seeds adaptive tie ordering |
| 38 | 12 | AES-GCM nonce; zero-filled when unencrypted |
| 50 | 16 | AES-GCM tag; zero-filled when unencrypted |
| 66 | 4 | IEEE CRC32 over bytes 0 through 65 |

When encryption is enabled, locator bytes 0 through 49 are AES-GCM associated data. A password is UTF-8 encoded and expanded to 32 bytes with PBKDF2-HMAC-SHA-256 using the locator salt and iteration count. The body is encrypted and authenticated with AES-256-GCM.

When compression is enabled, the payload bundle is Brotli-compressed before encryption. A decoder must cap decompressed output before allocating or returning it.

## Body placement

Locator channels are excluded from every body sequence.

- **Balanced:** remaining eligible channels in row-major RGB order; one body bit per channel, most-significant bit first.
- **Dense:** the same sequence; two body bits per channel, pairs from bits 7-6 through 1-0.
- **Adaptive:** eligible pixels are divided into 8x8 blocks. Luminance variance is calculated from RGB values with their two low bits cleared, using `(54R + 183G + 19B) >> 8`. Blocks are ordered by descending variance. Equal-variance blocks are ordered by SplitMix64 of the first eight salt bytes XOR the row-major block index. Channels inside a block remain row-major RGB; one bit per channel.
- **Resilient:** adaptive placement with each body byte encoded as Hamming(12,8). Codeword bit 0 is written first. The decoder corrects one changed bit per codeword and reports the number of corrected codewords.

Hamming positions are numbered 1 through 12 and stored in codeword bits 0 through 11. Even-parity bits occupy positions 1, 2, 4 and 8. Source bits 7 through 0 occupy positions 3, 5, 6, 7, 9, 10, 11 and 12 respectively. A non-zero syndrome from 1 through 12 flips that position before the data bits are read.

Clearing two low bits for adaptive scoring keeps the order stable after balanced one-bit or dense two-bit changes. SplitMix64 only makes equal-score ordering deterministic; it is not a confidentiality mechanism.

## Payload bundle

After optional decryption and decompression, the body is a `PBND` bundle:

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | ASCII `PBND` |
| 4 | 2 | version: `1` |
| 6 | 2 | entry count: 1 through 64 |

Entries follow directly. Each has a 45-byte fixed header and three variable fields:

| Relative offset | Size | Field |
| ---: | ---: | --- |
| 0 | 1 | kind: UTF-8 message `1`, file `2` |
| 1 | 2 | UTF-8 name length |
| 3 | 2 | UTF-8 media-type length |
| 5 | 8 | content length |
| 13 | 32 | SHA-256 of content |
| 45 | name length | name |
| next | media-type length | media type |
| next | content length | content bytes |

Names are non-empty, unique without case, and cannot contain path separators, colons, NUL or control characters. Message content must be valid UTF-8. No trailing bytes are allowed.

## Limits and detection

Current readers accept at most 32 million cover pixels, 64 entries, 128 MiB per entry and approximately 130 MiB for the decoded bundle. The carrier must also have enough eligible channels for both locator and protected body.

`inspect` reads and validates only the locator. The public magic and stable location make PixelSteg carriers intentionally detectable. A non-matching first four bytes means no locator; a matching but malformed locator is an error rather than a negative detection.

Any operation that changes RGB low bits or pixel order can corrupt the carrier. The format is not expected to survive cropping, scaling, palette conversion, colour correction or lossy recompression.
