# Release readiness

Reviewed 2026-08-12.

| Check | Evidence |
| --- | --- |
| Runtime dependencies | None beyond .NET 8; the PNG writer/reader uses the BCL `System.IO.Compression`. |
| Test-only dependencies | Top-level: xUnit 2.9.2, xUnit Visual Studio runner 2.8.2, and Microsoft.NET.Test.Sdk 17.11.1. Resolved transitives: Microsoft.CodeCoverage 17.11.1, Microsoft.TestPlatform.ObjectModel 17.11.1, Microsoft.TestPlatform.TestHost 17.11.1, Newtonsoft.Json 13.0.1, System.Reflection.Metadata 1.6.0, xunit.abstractions 2.0.3, xunit.analyzers 1.16.0, xunit.assert/core 2.9.2, and xunit.extensibility.core/execution 2.9.2. These test-only packages are compatible with the repository's MIT license and are not shipped with the app. |
| Build and tests | Run `dotnet build PixelSteg.sln -c Release` and `dotnet test PixelSteg.sln -c Release`. |
| Formatting | Run `dotnet format PixelSteg.sln --verify-no-changes --no-restore`. |
| Credential and path scan | Run a case-insensitive credential-token and personal-path scan, excluding generated build directories. |
| Manual smoke test | Encode and decode `docs/images/synthetic-sample.txt`; compare the decoded bytes with the source. |

No binaries, packages, Git tags, releases, pushes, or publication actions are part of this repository preparation.
