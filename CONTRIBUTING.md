# Contributing

Use English for code, tests, documentation, and user-facing text. Keep the application a transparent local container: do not add code execution, network retrieval, automatic file opening, encryption claims, or obfuscation.

Run these checks before opening a contribution:

```powershell
dotnet test PixelSteg.sln -c Release
dotnet build PixelSteg.sln -c Release
dotnet format PixelSteg.sln --verify-no-changes --no-restore
```

Add tests for behavior changes. Decode paths must remain bounded, validate integrity before output, sanitize embedded file names, and require an explicit overwrite decision.
