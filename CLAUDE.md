# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution layout (Clean Architecture + CQRS)

Five .NET 10 projects, all siblings at repo root, solution file `NorcusSheetsManager.slnx`:

| Project | Role |
| --- | --- |
| `NorcusSheetsManager.SharedKernel` | `Result`/`Result<T>`, `Error`, `ErrorType`, `ValidationError`. No external deps. |
| `NorcusSheetsManager.Application` | CQRS messaging (`ICommand`, `IQuery`, `ICommandHandler<T>`, `IQueryHandler<T,TResp>`), service port interfaces (`INameCorrector`, `IScanService`, `IWatcherControl`, `IFolderBrowser`, `IAccessControl`, `IAppLifecycle`), DTO/model interfaces (`IRenamingTransaction`, `IRenamingSuggestion`, `INorcusUser`, `ITransactionResponse`), configuration POCOs (`AppConfig`, `ConverterSettings`, `ApiServerSettings`, `DatabaseConnection`), and one folder per feature slice with command/query + handler. Uses Scrutor for handler auto-registration. |
| `NorcusSheetsManager.Infrastructure` | Implementations for Application's ports. `Manager/*` (FileSystemWatcher orchestrator `Manager` implementing `IScanService` + `IWatcherControl`, PDF→image `Converter`, `GDriveFix`, `Logger`, `ManagerHostedService`), `NameCorrector/*` (`Corrector` implementing `INameCorrector`, `Transaction`/`Suggestion`, `DbFileLoader`/`MySQLLoader`, `IDbLoader`), `Services/*` (`FolderBrowser`, `AccessControl`, `AppLifecycle`), `Configuration/ConfigLoader.cs`, `DependencyInjection.cs` (`AddInfrastructure`). |
| `NorcusSheetsManager.Web.Api` | REST endpoints (`IEndpoint` pattern — one file per route), `EndpointExtensions` (assembly-scan registration + `MapEndpoints`), `CustomResults.Problem` error mapper, `JWTAuthenticator` + `AuthContext` helper. Each endpoint is a thin adapter: auth check → build command/query → `handler.Handle` → `result.Match(Results.Ok, CustomResults.Problem)`. |
| `NorcusSheetsManager` (host + exe) | Composition root. `Program.cs` (WebApplication entry, `--install-service`/`--uninstall-service`/`--daemon`/`--no-console` flags, interactive vs daemon branch), `appsettings.json`, `NLog.config`, `api_doc.txt`, `version`, `gsdll64.dll`/`gswin64c.exe` (Windows). References Application, Infrastructure, SharedKernel, Web.Api. |

Project dependencies:
- `SharedKernel` — no deps.
- `Application` → SharedKernel.
- `Infrastructure` → Application, SharedKernel.
- `Web.Api` → Application, SharedKernel.
- `NorcusSheetsManager` (host) → all four.

No cycles. Web.Api does **not** depend on Infrastructure — endpoints only talk to Application interfaces. Infrastructure does **not** know Web.Api exists.

Central Package Management: versions in `Directory.Packages.props`, csproj `PackageReference` entries only name the package.

## Build & Run

```bash
dotnet restore
dotnet build NorcusSheetsManager.sln -c Release           # or -c Debug
dotnet build NorcusSheetsManager.sln -c Release -p:Platform=x64
dotnet run --project NorcusSheetsManager                  # runs the interactive console
```

There is no test project — `dotnet test` has nothing to run.

### Publishing a single-exe release

Framework-dependent single-file publish (requires .NET 10 runtime on the target):

```bash
# Windows
dotnet publish NorcusSheetsManager -c Release -r win-x64 --no-self-contained -o publish

# Linux
dotnet publish NorcusSheetsManager -c Release -r linux-x64 --no-self-contained -o publish
```

Output layouts:
- **Windows (~53 MB)** — `NorcusSheetsManager.exe` (~28 MB, bundles managed deps + `Magick.Native-Q8-x64.dll`), `gsdll64.dll` (~24 MB), `gswin64c.exe`, plus `NLog.config`, `api_doc.txt`, `version`. The two Ghostscript files are loaded from disk by `MagickNET.SetGhostscriptDirectory` and `Process.Start` in `Converter`, so they stay alongside the exe (marked `ExcludeFromSingleFile`).
- **Linux (~43 MB)** — `NorcusSheetsManager` (no extension, bundles managed deps + `Magick.Native-Q8-x64.dll.so`), plus the 3 config/data files. No Ghostscript files: the `Content Include` entries for `gsdll64.dll`/`gswin64c.exe` are conditional on `IsWindowsBuild`, and `Converter` calls `gs` from `PATH` instead. Install system Ghostscript (`apt install ghostscript`).

For a fully self-contained build, set `<SelfContained>true</SelfContained>` and re-add `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>` in the csproj (compression requires self-contained).

### Docker

A `Dockerfile` at repo root produces a Linux container image. Multi-stage: SDK image for `dotnet publish -r linux-x64`, then `mcr.microsoft.com/dotnet/aspnet:10.0` + `ghostscript` for the runtime layer.

```bash
docker build -t norcus-sheets-manager .
docker run -p 4434:4434 -v /path/to/sheets:/sheets -v /path/to/config:/app/config norcus-sheets-manager
```

The default URL `http://0.0.0.0:4434` comes from `AppConfig.ApiServer.Url`. The app writes `appsettings.json`, `debug.log`, and `error.log` next to the binary (`/app` in the container), so mount a volume to persist them across restarts. The `FileSystemWatcher` requires `Converter:SheetsPath` to be accessible inside the container — mount the host folder and point `SheetsPath` to the mount.

### Cross-cutting web concerns

- **API versioning** — `Asp.Versioning.Http` + `Asp.Versioning.Mvc.ApiExplorer`. URL-segment reader (`/api/v{version:apiVersion}/...`), default version `1.0`, `AssumeDefaultVersionWhenUnspecified = true`. Configured in `Web.Api/DependencyInjection.AddWebApi` (`AddApiVersioning` + `AddApiExplorer`). `Program._BuildApp` creates one `ApiVersionSet` and maps all endpoints under a single versioned `RouteGroupBuilder`:
  ```csharp
  ApiVersionSet versionSet = app.NewApiVersionSet()
      .HasApiVersion(new ApiVersion(1, 0))
      .ReportApiVersions()
      .Build();
  RouteGroupBuilder apiGroup = app
      .MapGroup("api/v{version:apiVersion}")
      .WithApiVersionSet(versionSet);
  app.MapEndpoints(apiGroup);
  ```
  Existing clients hitting `/api/v1/...` continue to work unchanged — the `{version:apiVersion}` constraint happily matches `v1`. Adding a new major version: build a second `ApiVersionSet` (or extend the existing one with `.HasApiVersion(new ApiVersion(2, 0))`), write v2-specific endpoints, map them on a group that specifies `.HasApiVersion(2.0)`. The Swagger UI and the `IEndpoint` interface don't need to change.
- **Swagger / OpenAPI** — `Swashbuckle.AspNetCore` generates `/swagger/v1/swagger.json` and serves a Swagger UI at `/swagger`. The UI has a Bearer auth button that feeds `Authorization: Bearer <jwt>` into every "Try it out" request. Registered in `Web.Api/DependencyInjection.AddWebApi` (`AddEndpointsApiExplorer` + `AddSwaggerGen` with the Bearer security scheme) and `Program._BuildApp` (`UseSwagger` + `UseSwaggerUI`). Currently a single v1 doc — when v2 lands, add a second `SwaggerDoc("v2", ...)` call and a second `SwaggerEndpoint` in the UI.
- **Global exception handler** — `Web.Api/Infrastructure/GlobalExceptionHandler.cs` implements `IExceptionHandler`. Registered via `AddExceptionHandler<GlobalExceptionHandler>()` + `AddProblemDetails()` + `app.UseExceptionHandler()`. Unhandled exceptions become `application/problem+json` with status 500; the stack trace lands in the `detail` field only when `IHostEnvironment.IsDevelopment()` is true.
- **Health checks** — `/health` returns a JSON report of every registered `IHealthCheck`. Two checks shipped: `SheetsFolderHealthCheck` (in `Infrastructure/HealthChecks`) probes `Directory.Exists(config.Converter.SheetsPath)`; `DatabaseHealthCheck` calls `IDbLoader.ReloadDataAsync()` with a 5-second timeout and reports song count. Both are tagged `"ready"` so a future `/health/ready` filter can pick just the readiness probes.

## Adding a new endpoint

Four files per route, mirroring the reference layout:

1. **Command or Query** in `NorcusSheetsManager.Application/<Feature>/<Action>/<Action>{Command|Query}.cs` — implements `ICommand`, `ICommand<T>`, or `IQuery<T>`.
2. **Handler** in the same folder — `internal sealed class` implementing `ICommandHandler<TCommand>` / `ICommandHandler<TCommand,TResp>` / `IQueryHandler<TQuery,TResp>`. Returns `Task<Result>` or `Task<Result<T>>`. Auto-registered by Scrutor at `AddApplication()`.
3. **Endpoint** in `NorcusSheetsManager.Web.Api/Endpoints/<Feature>/<Action>.cs` — `internal sealed class : IEndpoint` with `MapEndpoint(IEndpointRouteBuilder)`. Auto-registered via `AddEndpoints(assembly)` scan. Map URL relative to `/api/v1` (the `RouteGroupBuilder` prefix is applied in `Program.cs`). Use `.WithTags(Tags.<Feature>)`.
4. **Error catalog entries** (optional) in `NorcusSheetsManager.Application/<Feature>/<Feature>Errors.cs` if the feature needs typed errors. Use `Error.Problem/NotFound/Conflict/Unauthorized/Forbidden`.

Route-to-HTTP mapping is handled in `CustomResults.Problem` — `ErrorType.NotFound → 404`, `Conflict → 409`, `Unauthorized → 401`, `Forbidden → 403`, `Validation/Problem → 400`, anything else → 500.

### Run model

The app is daemon-only. `Program.Main` has three exit paths:

- `--install-service` / `--uninstall-service` — shells out to `sc.exe` (Windows only). Prints a one-line status to stdout / stderr and exits.
- `--help` / `-h` / `/?` — prints usage + API route map to stdout, exits 0.
- Anything else (including no args) — boots the `WebApplication`, registers the file-system watchers + hosted service, listens on `ApiServer.Url`, blocks in `app.RunAsync()` until SIGTERM / Windows service stop / `POST /api/v1/app/shutdown`.

Registered host lifetimes: `AddWindowsService` (auto-detects when the SCM starts the process) and `AddSystemd` (auto-detects when systemd's notify socket is present). SIGTERM / stop-service triggers `ManagerHostedService.StopAsync`, which stops the watchers; the inner WebApplication winds down naturally.

Everything is driven via HTTP. For live exploration, Swagger UI lives at `/swagger`, and every endpoint is tagged so it's grouped in the Swagger panel. The full route list is in `api_doc.txt` (and in Program.cs's `--help` output).

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
- **Security note:** if `ApiServer.JwtSigningKey` is empty, `JWTAuthenticator._ProcessToken` returns `(true, null)` — every request is accepted. A warning is logged at startup. Don't deploy with an empty key.

CORS is wide open via the default policy (`AllowAnyOrigin/Method/Header`, preflight max-age 86400s). Preflight OPTIONS is handled by the CORS middleware, not by an explicit endpoint.

Manager scan endpoints (`/scan`, `/deep-scan`, `/convert-all`) return `200 OK` immediately and run the scan on a background task so the request thread is freed.

### Logging
`Logger` (`Logger.cs`) is a thin static façade that writes to both NLog and `Console.WriteLine` with a timestamp. Use it — don't call `NLog.Logger` directly for user-visible events — so that console output stays consistent. NLog config (`NLog.config`) writes `debug.log` (rolling, 5 MB × 5) and `error.log`.

## Conventions

- **Primary constructors: use them whenever possible.** New classes and records should declare their dependencies/data through a primary constructor rather than an explicit `public Foo(...)`. This applies to handlers (`internal sealed class FooHandler(IBar bar) : ICommandHandler<...>`), records (`public record Error(string Code, string Description, ErrorType Type)`), and service adapters (`internal sealed class AccessControl(IDbLoader dbLoader) : IAccessControl`). Skip the conversion only when the constructor does real work beyond parameter assignment — conditional logging, side effects on global state (`IdentityModelEventSource.ShowPII = true`), or validation that throws (`Result`'s `isSuccess`/`error` consistency check). Current exceptions on purpose: `JWTAuthenticator` (logs a warning on empty key + sets `ShowPII`), `Result`/`Result<T>` (validation throw), `Corrector` (startup DB-load logging branch), `Manager` (watcher wiring), `Suggestion` (path computation reused across properties), `GDriveFix.GDriveFile` (the `FullFileName` setter triggers `_SetProperties()`; primary-ctor property initializers would bypass that).
- **Logging: inject `ILogger<T>` via the constructor.** Any class managed by DI (`Manager`, `Converter`, `Corrector`, `MySQLLoader`, `ManagerHostedService`, `AppLifecycle`, `JWTAuthenticator`, handlers) takes `ILogger<ClassName>` as a ctor parameter — either as a primary-ctor captured parameter or assigned to a `private readonly ILogger<T> _logger` field when the ctor already does work. Emit structured messages with placeholder templates (`_logger.LogInformation("Scanning {Count} files in {Path}.", count, path)`), never string-interpolated messages. Log levels follow the usual convention: Information for lifecycle/operational events, Debug for per-item/verbose, Warning/Error for problems (with the exception attached as the first argument). The custom `Logger` NLog+console wrapper has been removed — do not reintroduce it.
- **Logging in non-DI contexts** (data types like `Transaction`, static utilities like `GDriveFix`, bootstrap code like `ConfigLoader` that runs before DI is built) falls back to a static `NLog.Logger` field: `private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();`. Still use structured message templates — NLog's `_logger.Debug("File {Path} was renamed.", path)` accepts the same placeholder syntax. The `NLog.Extensions.Logging` bridge (wired up in `Program._BuildApp` via `builder.Logging.AddNLog("NLog.config")`) routes every `ILogger<T>` call through the same NLog rules, so direct-NLog and `ILogger<T>` calls end up in the same targets: the colored-console target (Info+), `debug.log` (Debug+), and `error.log` (Warning+).
- Configuration lives in `appsettings.json` next to the binary (and optional `appsettings.{Environment}.json` overlays plus environment variables) — loaded via `Microsoft.Extensions.Configuration` in `ConfigLoader.Load()`. Three top-level sections: `Converter` (PDF/image/watching/GDrive), `DbConnection` (song/musician store), and `ApiServer` (`RunServer`, `Url`, `JwtSigningKey`). The C# side is POCO classes in `NorcusSheetsManager.Application/Configuration/*`: `AppConfig` → `ConverterSettings` + `DatabaseConnection` + `ApiServerSettings`. Env-var overrides use the standard double-underscore path, e.g. `DbConnection__Password=...`.
- Language: comments and identifiers are English. `api_doc.txt` still has Czech paragraphs (matches the original project's language).
- Version is hand-maintained in the host `.csproj` (`<AssemblyVersion>`, `<Version>`) and surfaced via `Assembly.GetEntryAssembly().GetName().Version`. The `version` file in the project root is an unrelated date stamp, copied to output.
