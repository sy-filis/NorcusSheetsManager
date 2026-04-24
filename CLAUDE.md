# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

.NET 10.0 console app using `Microsoft.NET.Sdk.Web` (console UI + embedded Kestrel). Solution: `NorcusSheetsManager.sln`, single project in `NorcusSheetsManager/`. Platforms: `AnyCPU` and `x64`.

```bash
dotnet restore
dotnet build NorcusSheetsManager.sln -c Release           # or -c Debug
dotnet build NorcusSheetsManager.sln -c Release -p:Platform=x64
dotnet run --project NorcusSheetsManager                  # runs the interactive console
```

There is no test project — `dotnet test` has nothing to run.

### Publishing a single-exe release

The project is configured for framework-dependent single-file publish (requires .NET 10 runtime on the target machine):

```bash
dotnet publish NorcusSheetsManager -c Release -r win-x64 --no-self-contained -o publish
```

Output layout (≈53 MB total):
- `NorcusSheetsManager.exe` (~28 MB — bundles all managed deps and `Magick.Native-Q8-x64.dll`)
- `gsdll64.dll` and `gswin64c.exe` — Ghostscript, left as separate files because `MagickNET.SetGhostscriptDirectory` and the `Process.Start` call in `Converter.TryGetPdfPageCount` load them from `AppContext.BaseDirectory`
- `NLog.config`, `api_doc.txt`, `version` — config/data files

For a fully self-contained build (no runtime required, ~85 MB total, compressed exe), edit the csproj: `<SelfContained>true</SelfContained>` and re-add `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>` (compression is only valid when self-contained).

The project ships native Ghostscript binaries (`gsdll64.dll`, `gswin64c.exe`) that are copied to the output directory via `CopyToOutputDirectory=PreserveNewest`. Recent commits added Linux support, so these Windows binaries are not universally required at runtime, but the `TryGetPdfPageCount` path in `Converter.cs` hard-codes `gswin64c.exe` — keep that in mind if changing platform behavior.

### Running the compiled app

Interactive key commands in `Program.cs`:

| Key | Action |
| --- | --- |
| `S` | Full scan (ensures every PDF has at least one image) |
| `D` | Deep scan (compares image file count vs PDF page count, reconverts mismatches) |
| `F` | Force convert all PDFs (prompts `Y/N`) |
| `C` / `N` | Run the name corrector interactively |
| `X` / `T` | Stop |

On first run the app writes a default `NorcusSheetsManagerCfg.xml` next to the executable. `SheetsPath` must be set or startup throws.

## Architecture

### Pipeline
The app is a long-running PDF→image sync daemon over a folder of sheet music. One `Manager` instance orchestrates everything and is constructed once in `Program.Main`.

`Manager` (`Manager.cs`) on construction:
1. Loads `IConfig` via `ConfigLoader` (XML serialization; re-saves deserialized config to migrate old schemas).
2. Instantiates a `Converter` (Magick.NET + Ghostscript wrapper).
3. Creates one `FileSystemWatcher` **per top-level subdirectory** of `SheetsPath` (not recursive — deliberate, see `_CreateFileSystemWatchers`). Filters come from `Config.WatchedExtensions`.
4. Picks an `IDbLoader` implementation: `DbFileLoader` if `DbConnection.Database` points to an existing `.txt` file (one song name per line), otherwise `MySQLLoader` (tables `songs` and `musicians`).
5. Builds a `Corrector` for filename→song matching using `F23.StringSimilarity` QGram(2).
6. If `APISettings.RunServer`, starts the Grapevine REST server on `APISettings.Port` (default 4434).

After construction `Main` calls `FullScan()`, `StartWatching()`, and optionally `AutoFullScan(60000, 5)` (5 scans at 60 s intervals on startup).

### Scan semantics
Three scan modes in `Manager` — all gate the watcher with `StopWatching` / `StartWatching` and toggle `_ScanningInProgress` so `AutoFullScan` skips overlapping runs:
- **FullScan** — for each PDF, convert only if no matching images exist or they're older than the PDF.
- **DeepScan** — additionally reconverts when image count ≠ PDF page count (uses Ghostscript CLI in `Converter.TryGetPdfPageCount`, not Magick's `PdfInfo`, which throws).
- **ForceConvertAll** — deletes and reconverts every PDF. If `MovePdfToSubfolder` is enabled, first pulls archived PDFs out of `PdfSubfolder` back to the parent folder.

The converter emits either `<name>.<ext>` for single-page PDFs or `<name><MultiPageDelimiter><NNN>.<ext>` (zero-padded to `MultiPageCounterLength`) for multi-page. `_GetImagesForPdf` relies on this exact pattern plus an optional ` (N)` suffix to locate image siblings of a given PDF.

### GDrive fix
Files synced from Google Drive get `" (1)"`-style version suffixes. `GDriveFix` detects `VerPattern = "\\s\\(\\d+\\)\\."` and renames to the unversioned form (keeping only the highest-version file when multiple exist). It's invoked on scan startup (when `FixGDriveNaming` is on) and from the watcher's `Created` / `Renamed` handlers. The watcher is temporarily disabled around renames to avoid event loops.

### Name corrector
`Corrector` + `Transaction` implement a stateful rename workflow:
- `GetRenamingTransactions(...)` lists files whose name doesn't match any DB song, and ranks song candidates by QGram distance. Each returned `Transaction` gets a fresh `Guid` and is cached in `_RenamingTransactions`.
- Callers later commit by `Guid` with either a suggestion index or a free-form filename (`CommitTransactionByGuid` overloads), or call `DeleteTransaction` to remove the bad file. Committed transactions are dropped from the cache.
- `MAX_SUGGESTIONS_COUNT = 10` is the upper bound regardless of what the API asks for.

### REST API (ASP.NET Core Minimal APIs)
`Server.Initialize` builds a `WebApplication` on Kestrel, registers `ITokenAuthenticator`, `Corrector`, and `Manager` as singletons, wires NLog, and calls each resource's static `MapEndpoints(IEndpointRouteBuilder)`. `Server.Start()` is non-blocking (`app.StartAsync().GetAwaiter().GetResult()`) so the console loop in `Program.Main` keeps running. URL binding uses `http://0.0.0.0:{port}`.

Resources — each is a static class with endpoint handler methods, taking dependencies via parameter injection from the handler lambdas:
- `MasterResource` → `/api/v1/folders`
- `NameCorrectorResource` → `/api/v1/corrector/*`
- `ManagerResource` → `/api/v1/manager/*`

The full route list is in `api_doc.txt` — **keep it in sync** when adding routes.

Authorization flow (`JWTAuthenticator`), now takes `Microsoft.AspNetCore.Http.HttpContext`:
- `ValidateFromContext(context)` — just checks the token signature/lifetime.
- `ValidateFromContext(context, Claim)` — also requires a specific claim, e.g. `new Claim("NsmAdmin", "true")`. Used to gate `ManagerResource` endpoints and admin-only corrector actions.
- `NameCorrectorModel.CanUserRead/CanUserCommit` — non-admin access is scoped to the user's own `Folder` (looked up by the `uuid` claim in `musicians`). `CanUserRead` mutates the `ref sheetsFolder` to the user's folder when they request "all folders".
- **Security note:** if `APISettings.Key` is empty, `JWTAuthenticator._ProcessToken` returns `(true, null)` — every request is accepted. A warning is logged at startup. Don't deploy with an empty key.

CORS is wide open via the default policy (`AllowAnyOrigin/Method/Header`, preflight max-age 86400s). Preflight OPTIONS is handled by the CORS middleware, not by an explicit endpoint.

Manager scan endpoints (`/scan`, `/deep-scan`, `/convert-all`) return `200 OK` immediately and run the scan on a background task so the request thread is freed.

### Logging
`Logger` (`Logger.cs`) is a thin static façade that writes to both NLog and `Console.WriteLine` with a timestamp. Use it — don't call `NLog.Logger` directly for user-visible events — so that console output stays consistent. NLog config (`NLog.config`) writes `debug.log` (rolling, 5 MB × 5) and `error.log`.

## Conventions

- Language: mixed English/Czech. Comments and log messages in `Manager`, `Corrector`, `GDriveFix`, `api_doc.txt` are Czech; identifiers and public API are English. Don't translate existing Czech text unless asked — match the surrounding language when adding new comments.
- Namespace quirk: `Program.cs` sits in `namespace AutoPdfToImage` (legacy name); everything else is under `NorcusSheetsManager`. Leave it alone unless doing a deliberate rename.
- Version is hand-maintained in the `.csproj` (`<AssemblyVersion>`, `<Version>`) and surfaced via `Assembly.GetEntryAssembly().GetName().Version`. The `version` file in the project root appears unrelated (contains a date build stamp) and is copied to output.
- `IConfig` is a serialized contract — adding a property requires giving it a default value so older XML configs still deserialize. `ConfigLoader.Load` re-saves after deserialization to persist migrations.
