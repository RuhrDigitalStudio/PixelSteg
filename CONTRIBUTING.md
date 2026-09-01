# Contributing

Bug reports, format discussions and focused improvements are welcome. Please describe the use case before proposing a new embedding profile; interoperability and predictable failure modes matter more than adding a large option surface.

Use English for code, tests, documentation and user-facing text. Keep runtime behavior local and explicit: no network retrieval, automatic file opening, content execution or undocumented format changes.

Before opening a pull request, run:

```powershell
dotnet restore PixelSteg.sln
dotnet build PixelSteg.sln -c Release --no-restore
dotnet test PixelSteg.sln -c Release --no-build
dotnet format PixelSteg.sln --verify-no-changes --no-restore
```

Behavior changes need tests. Decoder tests should include malformed and truncated input, declared-length limits, integrity failures and path-safe extraction. Profile changes must cover round trips, exact capacity boundaries and a quality report. Update [docs/format-v2.md](docs/format-v2.md) whenever bytes on disk or channel placement change.

Comments should explain constraints or decisions that are not obvious from the code. Avoid narrating the next line.
