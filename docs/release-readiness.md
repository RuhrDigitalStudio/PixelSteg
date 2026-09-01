# Release readiness

Last reviewed: 2026-09-01.

| Check | Required evidence |
| --- | --- |
| Clean source | `git diff --check` and a clean worktree after the release commit |
| Restore | `dotnet restore PixelSteg.sln --locked-mode` when lock files are present; otherwise normal restore |
| Build | `dotnet build PixelSteg.sln -c Release --no-restore` |
| Tests | All Core, CLI and App tests pass in Release configuration |
| Formatting | `dotnet format PixelSteg.sln --verify-no-changes --no-restore` |
| CLI smoke test | Embed, inspect and extract a synthetic message and file; compare recovered bytes |
| Desktop smoke test | Start the published Windows app; complete one hide/reveal cycle |
| Format review | `docs/format-v2.md` matches constants and byte order in Core |
| Dependency review | Runtime projects have no third-party package references; test packages are not published |
| Secret/path scan | No credentials, private keys, user paths or machine-specific build output are tracked |
| Artifacts | Archives contain published output, license and README; checksums match |

The tag workflow builds source again and attaches framework-dependent CLI packages for Windows and Linux plus a self-contained Windows desktop package. A maintainer should verify the generated checksums and smoke-test the downloaded archives before announcing a release.
