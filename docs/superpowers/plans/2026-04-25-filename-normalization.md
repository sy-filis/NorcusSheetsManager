# Filename Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce per-folder filename uniqueness for sheet image groups: bare `name.ext` when alone, sequential `name-NNN.ext` when 2+. Reconciliation runs at scan-end and on watcher Created/Renamed events for image files.

**Architecture:** A new infrastructure service `FileNameNormalizer` (behind interface `IFileNameNormalizer`) parses filenames, groups by base name, and applies a two-phase rename (`<original>.tmp` → final). It's injected into the existing `Manager`, called at the tail of every scan and from watcher Created/Renamed handlers (within `StopWatching`/`StartWatching` brackets to avoid event loops). No Converter post-convert hook — the rare mixed-extension drift is corrected by the next scan.

**Tech Stack:** .NET 10, `Microsoft.Extensions.DependencyInjection` singletons, primary constructors, `ILogger<T>`, no test project (manual verification scenarios).

**Spec reference:** `docs/superpowers/specs/2026-04-25-filename-normalization-design.md`

---

## File map

**Created:**
- `NorcusSheetsManager.Application/Abstractions/Services/IFileNameNormalizer.cs` — public interface for the normalizer port.
- `NorcusSheetsManager.Infrastructure/Manager/FileNameNormalizer.cs` — internal sealed implementation; parser, planner, two-phase rename, temp recovery.

**Modified:**
- `NorcusSheetsManager.Infrastructure/DependencyInjection.cs` — register `IFileNameNormalizer` → `FileNameNormalizer` as singleton.
- `NorcusSheetsManager.Infrastructure/Manager/Manager.cs` — accept `IFileNameNormalizer` via constructor, call `NormalizeFolder` at scan tails, call `NormalizeSong` from `Watcher_Created` (image branch) and `Watcher_Renamed` (image branch).

There are no tests to add (no test project exists). Verification is the manual scenario script at the end of this plan.

---

## Task 1: Create `IFileNameNormalizer` interface

**Files:**
- Create: `NorcusSheetsManager.Application/Abstractions/Services/IFileNameNormalizer.cs`

- [ ] **Step 1: Write the interface**

Create the file with this exact content:

```csharp
namespace NorcusSheetsManager.Application.Abstractions.Services;

public interface IFileNameNormalizer
{
  /// <summary>
  /// Normalizes every base-name group in <paramref name="folderPath"/>.
  /// </summary>
  void NormalizeFolder(string folderPath);

  /// <summary>
  /// Normalizes a single base-name group within <paramref name="folderPath"/>.
  /// </summary>
  void NormalizeSong(string folderPath, string baseName);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add NorcusSheetsManager.Application/Abstractions/Services/IFileNameNormalizer.cs
git commit -m "$(cat <<'EOF'
add IFileNameNormalizer abstraction

Application-layer port for the upcoming filename normalization service.
Two methods: NormalizeFolder (every group) and NormalizeSong (single group).
EOF
)"
```

---

## Task 2: Create `FileNameNormalizer` implementation

This task adds the full implementation in one file. It's substantial but cohesive — a single class containing parser, planner, two-phase rename, and temp recovery.

**Files:**
- Create: `NorcusSheetsManager.Infrastructure/Manager/FileNameNormalizer.cs`

- [ ] **Step 1: Write the file**

Create the file with this exact content:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.Manager;

internal sealed class FileNameNormalizer(
    AppConfig config,
    ILogger<FileNameNormalizer> logger) : IFileNameNormalizer
{
  private static readonly Regex _NumberedPattern = new(@"^(.+)-(\d+)\.([^.]+)$", RegexOptions.Compiled);
  private static readonly Regex _SimplePattern = new(@"^(.+)\.([^.]+)$", RegexOptions.Compiled);
  private const string _TempSuffix = ".tmp";

  public void NormalizeFolder(string folderPath)
  {
    if (!Directory.Exists(folderPath))
    {
      logger.LogDebug("NormalizeFolder skipped: folder {Folder} does not exist.", folderPath);
      return;
    }

    _RecoverTempFiles(folderPath);

    HashSet<string> imageExts = _GetImageExtensions();
    var grouped = _EnumerateImages(folderPath, imageExts)
        .Select(f => (File: f, Parsed: _Parse(f.Name)))
        .Where(p => p.Parsed.HasValue)
        .GroupBy(p => p.Parsed!.Value.Base, StringComparer.OrdinalIgnoreCase);

    int touched = 0, renamed = 0;
    foreach (var group in grouped)
    {
      int before = renamed;
      renamed += _NormalizeGroup(folderPath, group.Key, group.Select(p => p.File).ToList());
      if (renamed > before)
      {
        touched++;
      }
    }

    logger.LogDebug("NormalizeFolder({Folder}) finished: {Touched} group(s) touched, {Renamed} file(s) renamed.", folderPath, touched, renamed);
  }

  public void NormalizeSong(string folderPath, string baseName)
  {
    if (!Directory.Exists(folderPath))
    {
      logger.LogDebug("NormalizeSong skipped: folder {Folder} does not exist.", folderPath);
      return;
    }
    if (string.IsNullOrEmpty(baseName))
    {
      return;
    }

    _RecoverTempFiles(folderPath);

    HashSet<string> imageExts = _GetImageExtensions();
    List<FileInfo> files = _EnumerateImages(folderPath, imageExts)
        .Where(f =>
        {
          var parsed = _Parse(f.Name);
          return parsed.HasValue && string.Equals(parsed.Value.Base, baseName, StringComparison.OrdinalIgnoreCase);
        })
        .ToList();

    if (files.Count == 0)
    {
      return;
    }

    _NormalizeGroup(folderPath, baseName, files);
  }

  /// <summary>
  /// Returns the number of files renamed.
  /// </summary>
  private int _NormalizeGroup(string folderPath, string baseName, List<FileInfo> files)
  {
    IReadOnlyList<(string From, string To)> renames = _PlanRenames(folderPath, baseName, files);
    if (renames.Count == 0)
    {
      return 0;
    }

    // Phase 1: move each source to <source>.tmp
    var phase1 = new List<(string Tmp, string Final, string OriginalName)>();
    foreach (var (from, to) in renames)
    {
      string tmp = from + _TempSuffix;
      try
      {
        File.Move(from, tmp);
        phase1.Add((tmp, to, Path.GetFileName(from)));
      }
      catch (FileNotFoundException ex)
      {
        logger.LogDebug(ex, "Phase 1 skipped (file missing): {From}.", from);
      }
      catch (DirectoryNotFoundException ex)
      {
        logger.LogDebug(ex, "Phase 1 skipped (folder gone): {From}.", from);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Phase 1 rename failed: {From} → {Tmp}. Aborting group {Base} in {Folder}.", from, tmp, baseName, folderPath);
        return 0;
      }
    }

    // Phase 2: move each .tmp to its final name.
    int renamedCount = 0;
    foreach (var (tmp, final, original) in phase1)
    {
      try
      {
        File.Move(tmp, final);
        logger.LogInformation("Normalized {From} → {To} in {Folder}.", original, Path.GetFileName(final), folderPath);
        renamedCount++;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Phase 2 rename failed: {Tmp} → {Final}. File left as {Tmp} for next-run recovery.", tmp, final, tmp);
      }
    }

    return renamedCount;
  }

  /// <summary>
  /// Computes the (from, to) rename list for a group. Pure: no IO.
  /// </summary>
  private IReadOnlyList<(string From, string To)> _PlanRenames(string folderPath, string baseName, IReadOnlyList<FileInfo> files)
  {
    int counterLength = config.Converter.MultiPageCounterLength;
    int initNumber = config.Converter.MultiPageInitNumber;

    var parsed = files
        .Select(f => (File: f, Parsed: _Parse(f.Name)))
        .Where(p => p.Parsed.HasValue)
        .Select(p => (p.File, P: p.Parsed!.Value))
        .ToList();

    if (parsed.Count == 0)
    {
      return Array.Empty<(string, string)>();
    }

    if (parsed.Count == 1)
    {
      var only = parsed[0];
      string targetName = $"{baseName}.{only.P.Ext}";
      string targetPath = Path.Combine(folderPath, targetName);
      if (string.Equals(only.File.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
      {
        return Array.Empty<(string, string)>();
      }
      return [(only.File.FullName, targetPath)];
    }

    // 2+ files — sort and assign sequential numbers.
    var sorted = parsed
        .OrderBy(p => p.P.Page.HasValue ? 1 : 0)
        .ThenBy(p => p.P.Page ?? -1)
        .ThenBy(p => p.P.Ext, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var result = new List<(string, string)>();
    for (int i = 0; i < sorted.Count; i++)
    {
      int n = initNumber + i;
      string targetName = $"{baseName}-{n.ToString().PadLeft(counterLength, '0')}.{sorted[i].P.Ext}";
      string targetPath = Path.Combine(folderPath, targetName);
      if (string.Equals(sorted[i].File.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      result.Add((sorted[i].File.FullName, targetPath));
    }
    return result;
  }

  /// <summary>
  /// Restores any leftover &lt;original&gt;.tmp files from a previous failed pass.
  /// If the original slot is free, drops the .tmp suffix. If the slot is taken, leaves the .tmp alone and warns.
  /// </summary>
  private void _RecoverTempFiles(string folderPath)
  {
    string[] tmpFiles;
    try
    {
      tmpFiles = Directory.GetFiles(folderPath, "*" + _TempSuffix, SearchOption.TopDirectoryOnly);
    }
    catch (DirectoryNotFoundException)
    {
      return;
    }

    foreach (string tmp in tmpFiles)
    {
      string restored = tmp.Substring(0, tmp.Length - _TempSuffix.Length);
      // restored is the original full path (e.g., ".../song-001.jpg.tmp" → ".../song-001.jpg")
      if (File.Exists(restored))
      {
        logger.LogWarning("Cannot recover temp file {Tmp}: target {Restored} already exists.", tmp, restored);
        continue;
      }
      try
      {
        File.Move(tmp, restored);
        logger.LogInformation("Recovered temp file {Tmp} → {Restored}.", Path.GetFileName(tmp), Path.GetFileName(restored));
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to recover temp file {Tmp} → {Restored}.", tmp, restored);
      }
    }
  }

  private IEnumerable<FileInfo> _EnumerateImages(string folder, HashSet<string> imageExts)
  {
    FileInfo[] files;
    try
    {
      files = new DirectoryInfo(folder).GetFiles("*", SearchOption.TopDirectoryOnly);
    }
    catch (DirectoryNotFoundException)
    {
      return Array.Empty<FileInfo>();
    }
    return files.Where(f => imageExts.Contains(f.Extension));
  }

  private HashSet<string> _GetImageExtensions()
  {
    return new HashSet<string>(
        config.Converter.WatchedExtensions.Where(e => !string.Equals(e, ".pdf", StringComparison.OrdinalIgnoreCase)),
        StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Parses "song-001.jpg" → ("song", 1, "jpg") greedy on the numbered pattern,
  /// falling back to "song.jpg" → ("song", null, "jpg"). Returns null if no extension.
  /// </summary>
  private static (string Base, int? Page, string Ext)? _Parse(string fileName)
  {
    Match m = _NumberedPattern.Match(fileName);
    if (m.Success)
    {
      return (m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[3].Value);
    }
    Match m2 = _SimplePattern.Match(fileName);
    if (m2.Success)
    {
      return (m2.Groups[1].Value, null, m2.Groups[2].Value);
    }
    return null;
  }

  /// <summary>
  /// Public-ish helper for callers (Manager) that have a filename and want the parsed base name.
  /// Returns null if the filename can't be parsed.
  /// </summary>
  internal static string? GetBaseName(string fileName)
  {
    return _Parse(fileName)?.Base;
  }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 0 warnings. (The IDE diagnostic "type is never used" may show in your editor — that's fine, it goes away after Task 3 wires it into DI.)

- [ ] **Step 3: Commit**

```bash
git add NorcusSheetsManager.Infrastructure/Manager/FileNameNormalizer.cs
git commit -m "$(cat <<'EOF'
add FileNameNormalizer service

Implements IFileNameNormalizer. Parses filenames into (base, page?, ext),
groups by base name, and applies a two-phase rename with .tmp suffixes
to safely renumber within a group. Recovers leftover .tmp files from a
prior failed pass on every entry. Not yet wired into DI or Manager.
EOF
)"
```

---

## Task 3: Register the normalizer in DI

**Files:**
- Modify: `NorcusSheetsManager.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add the registration**

Open `NorcusSheetsManager.Infrastructure/DependencyInjection.cs`. Locate this block:

```csharp
    services.AddSingleton<Converter>();
    services.AddSingleton<Manager.Manager>();
```

Replace it with:

```csharp
    services.AddSingleton<IFileNameNormalizer, Manager.FileNameNormalizer>();
    services.AddSingleton<Converter>();
    services.AddSingleton<Manager.Manager>();
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 0 warnings. The "type is never used" hint on `FileNameNormalizer` should now be gone.

- [ ] **Step 3: Commit**

```bash
git add NorcusSheetsManager.Infrastructure/DependencyInjection.cs
git commit -m "$(cat <<'EOF'
register FileNameNormalizer in DI

Singleton registration via the IFileNameNormalizer port so consumers
(currently the upcoming Manager hooks) depend on the abstraction.
EOF
)"
```

---

## Task 4: Inject the normalizer into `Manager`

**Files:**
- Modify: `NorcusSheetsManager.Infrastructure/Manager/Manager.cs:8-30`

- [ ] **Step 1: Add the constructor parameter and field**

In `NorcusSheetsManager.Infrastructure/Manager/Manager.cs`, find the constructor block (lines around 8-30) that currently looks like:

```csharp
internal class Manager : IScanService, IWatcherControl
{
  private readonly ILogger<Manager> _logger;
  private readonly Converter _Converter;
  private readonly List<FileSystemWatcher> _FileSystemWatchers;
  private bool _IsWatcherEnabled;
  private bool _ScanningInProgress;
  public AppConfig Config { get; }

  public Manager(AppConfig config, Converter converter, ILogger<Manager> logger)
  {
    Config = config;
    _logger = logger;
    if (string.IsNullOrEmpty(Config.Converter.SheetsPath))
    {
      Exception e = new ArgumentNullException(nameof(Config.Converter.SheetsPath));
      _logger.LogError(e, "SheetsPath is not configured.");
      throw e;
    }

    _Converter = converter;
    _FileSystemWatchers = _CreateFileSystemWatchers();
  }
```

Replace it with:

```csharp
internal class Manager : IScanService, IWatcherControl
{
  private readonly ILogger<Manager> _logger;
  private readonly Converter _Converter;
  private readonly IFileNameNormalizer _Normalizer;
  private readonly List<FileSystemWatcher> _FileSystemWatchers;
  private bool _IsWatcherEnabled;
  private bool _ScanningInProgress;
  public AppConfig Config { get; }

  public Manager(AppConfig config, Converter converter, IFileNameNormalizer normalizer, ILogger<Manager> logger)
  {
    Config = config;
    _logger = logger;
    if (string.IsNullOrEmpty(Config.Converter.SheetsPath))
    {
      Exception e = new ArgumentNullException(nameof(Config.Converter.SheetsPath));
      _logger.LogError(e, "SheetsPath is not configured.");
      throw e;
    }

    _Converter = converter;
    _Normalizer = normalizer;
    _FileSystemWatchers = _CreateFileSystemWatchers();
  }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 0 warnings. The DI container resolves `IFileNameNormalizer` via Task 3's registration.

- [ ] **Step 3: Commit**

```bash
git add NorcusSheetsManager.Infrastructure/Manager/Manager.cs
git commit -m "$(cat <<'EOF'
inject IFileNameNormalizer into Manager

Adds the dependency without yet calling it. Hooks land in the next commits.
EOF
)"
```

---

## Task 5: Add the scan-end normalization helper

**Files:**
- Modify: `NorcusSheetsManager.Infrastructure/Manager/Manager.cs` — add a new private method.

- [ ] **Step 1: Add the helper method**

In `NorcusSheetsManager.Infrastructure/Manager/Manager.cs`, find this private method block (search for `private void _FixAllGoogleFiles`):

```csharp
  private void _FixAllGoogleFiles()
  {
```

Immediately *before* it, insert this new method:

```csharp
  /// <summary>
  /// Runs filename normalization across every top-level subfolder of <see cref="ConverterSettings.SheetsPath"/>.
  /// Called at the tail of every scan, while the watcher is still off.
  /// </summary>
  private void _NormalizeAllFolders()
  {
    string[] directories;
    try
    {
      directories = Directory.GetDirectories(Config.Converter.SheetsPath!);
    }
    catch (DirectoryNotFoundException)
    {
      return;
    }
    foreach (string dir in directories)
    {
      _Normalizer.NormalizeFolder(dir);
    }
  }

```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 1 hint ("`_NormalizeAllFolders` is never used") — that's fine, it gets called in Task 6.

- [ ] **Step 3: Commit**

```bash
git add NorcusSheetsManager.Infrastructure/Manager/Manager.cs
git commit -m "$(cat <<'EOF'
add Manager._NormalizeAllFolders helper

Unused for now — wired into the three scan methods in the next commit.
EOF
)"
```

---

## Task 6: Call the helper at the tail of every scan

**Files:**
- Modify: `NorcusSheetsManager.Infrastructure/Manager/Manager.cs:110-140` (FullScan), `:145-229` (DeepScan), `:234-293` (ForceConvertAll)

There are three scan methods, each ending with the same closing pattern: log line(s), `_ScanningInProgress = false;`, `StartWatching();`. We insert `_NormalizeAllFolders();` just *before* `_ScanningInProgress = false;` in each.

- [ ] **Step 1: Update `FullScan`**

Find this block at the end of `FullScan()`:

```csharp
    if (convertCounter > 0)
    {
      _logger.LogInformation("{Count} files converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);
    }

    _ScanningInProgress = false;
    StartWatching();
  }
```

Replace it with:

```csharp
    if (convertCounter > 0)
    {
      _logger.LogInformation("{Count} files converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);
    }

    _NormalizeAllFolders();
    _ScanningInProgress = false;
    StartWatching();
  }
```

- [ ] **Step 2: Update `DeepScan`**

Find this block at the end of `DeepScan()`:

```csharp
    _logger.LogInformation("{Count} file(s) converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);
    _ScanningInProgress = false;
    StartWatching();
  }
```

Replace it with:

```csharp
    _logger.LogInformation("{Count} file(s) converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);

    _NormalizeAllFolders();
    _ScanningInProgress = false;
    StartWatching();
  }
```

Note: `DeepScan` and `ForceConvertAll` end with the same three lines. The replacement above is for `DeepScan` — apply the next replacement to `ForceConvertAll`. (The substring is identical, so use a replace-all only if you also adjust the verification: `git diff` should show the change in BOTH methods after both replacements.)

- [ ] **Step 3: Update `ForceConvertAll`**

Find the closing block of `ForceConvertAll()` — same three-line pattern as `DeepScan`:

```csharp
    _logger.LogInformation("{Count} file(s) converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);
    _ScanningInProgress = false;
    StartWatching();
  }
```

Replace with:

```csharp
    _logger.LogInformation("{Count} file(s) converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);

    _NormalizeAllFolders();
    _ScanningInProgress = false;
    StartWatching();
  }
```

(If your `Edit` tool refuses because the string is now ambiguous after Step 2, scope the find with surrounding `ForceConvertAll`-specific context — e.g., include the preceding `foreach (FileInfo pdfFile in pdfFiles)` block or the method signature.)

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 0 warnings. The "never used" hint on `_NormalizeAllFolders` is gone.

- [ ] **Step 5: Verify with `git diff`**

Run: `git diff NorcusSheetsManager.Infrastructure/Manager/Manager.cs`
Expected: three additions of `    _NormalizeAllFolders();` (one per scan method), each immediately before `_ScanningInProgress = false;`.

- [ ] **Step 6: Commit**

```bash
git add NorcusSheetsManager.Infrastructure/Manager/Manager.cs
git commit -m "$(cat <<'EOF'
normalize folder names at the end of every scan

FullScan, DeepScan, and ForceConvertAll now call _NormalizeAllFolders
right before re-enabling the watcher. The watcher is still off during
the renames, so no event-loop concern.
EOF
)"
```

---

## Task 7: Add the watcher Created hook for image files

The current `Watcher_Created` only handles GDrive renaming and PDF conversion. Now we add a parallel branch: when an *image* file is created (extension in `WatchedExtensions \ {.pdf}`), normalize its base-name group within `StopWatching`/`StartWatching` brackets.

**Files:**
- Modify: `NorcusSheetsManager.Infrastructure/Manager/Manager.cs:351-370` (`Watcher_Created`)

- [ ] **Step 1: Replace `Watcher_Created`**

Find the existing `Watcher_Created` method:

```csharp
  private void Watcher_Created(object sender, FileSystemEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    string fullPath = e.FullPath;
    _logger.LogDebug("Detected: {Path} was created.", fullPath);
    if (Config.Converter.FixGDriveNaming && Regex.IsMatch(fullPath, GDriveFix.GDriveFile.VerPattern))
    {
      fullPath = _FixGoogleFile(fullPath);
    }

    var file = new FileInfo(fullPath);
    if (file.Extension == ".pdf")
    {
      _DeleteOlderAndConvert(file);
    }
  }
```

Replace it with:

```csharp
  private void Watcher_Created(object sender, FileSystemEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    string fullPath = e.FullPath;
    _logger.LogDebug("Detected: {Path} was created.", fullPath);
    if (Config.Converter.FixGDriveNaming && Regex.IsMatch(fullPath, GDriveFix.GDriveFile.VerPattern))
    {
      fullPath = _FixGoogleFile(fullPath);
    }

    var file = new FileInfo(fullPath);
    if (file.Extension == ".pdf")
    {
      _DeleteOlderAndConvert(file);
      return;
    }

    _NormalizeImageFile(fullPath);
  }

  /// <summary>
  /// If <paramref name="fullPath"/> is an image (extension in WatchedExtensions \ {.pdf}),
  /// normalize the base-name group it belongs to. Pauses/resumes the watcher around the call.
  /// </summary>
  private void _NormalizeImageFile(string fullPath)
  {
    string ext = Path.GetExtension(fullPath);
    if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }
    if (!Config.Converter.WatchedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
    {
      return;
    }

    string? folder = Path.GetDirectoryName(fullPath);
    if (folder is null)
    {
      return;
    }
    string? baseName = FileNameNormalizer.GetBaseName(Path.GetFileName(fullPath));
    if (baseName is null)
    {
      return;
    }

    bool wasActive = _IsWatcherEnabled;
    if (wasActive)
    {
      StopWatching();
    }
    try
    {
      _Normalizer.NormalizeSong(folder, baseName);
    }
    finally
    {
      if (wasActive)
      {
        StartWatching();
      }
    }
  }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add NorcusSheetsManager.Infrastructure/Manager/Manager.cs
git commit -m "$(cat <<'EOF'
normalize image groups on watcher Created events

Watcher_Created now branches: PDFs go to the existing convert path,
images go through _NormalizeImageFile which normalizes their base-name
group inside a StopWatching/StartWatching bracket.
EOF
)"
```

---

## Task 8: Add the watcher Renamed hook for image files

`Watcher_Renamed` currently handles two cases: GDrive-pattern rename of a PDF (triggers conversion), and a generic rename that propagates to associated images. We append a normalization call for image targets after the existing logic, scoped to the new name's base-name group.

**Files:**
- Modify: `NorcusSheetsManager.Infrastructure/Manager/Manager.cs:327-349` (`Watcher_Renamed`)

- [ ] **Step 1: Replace `Watcher_Renamed`**

Find the existing `Watcher_Renamed` method:

```csharp
  private void Watcher_Renamed(object sender, RenamedEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    _logger.LogDebug("Detected: {OldPath} was renamed to {NewPath}.", e.OldFullPath, e.FullPath);

    // If this was a GDrive-rename fixup, the PDF needs to be reconverted:
    if (Path.GetExtension(e.FullPath) == ".pdf" && Regex.IsMatch(e.OldFullPath, GDriveFix.GDriveFile.VerPattern))
    {
      _DeleteOlderAndConvert(new FileInfo(e.FullPath), true);
      return;
    }

    if (e.OldName is null || e.Name is null)
    {
      return;
    }
    FileInfo[] images = _GetImagesForPdf(new FileInfo(e.OldFullPath));
    _RenameImages(images, e.OldName, e.Name);
  }
```

Replace it with:

```csharp
  private void Watcher_Renamed(object sender, RenamedEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    _logger.LogDebug("Detected: {OldPath} was renamed to {NewPath}.", e.OldFullPath, e.FullPath);

    // If this was a GDrive-rename fixup, the PDF needs to be reconverted:
    if (Path.GetExtension(e.FullPath) == ".pdf" && Regex.IsMatch(e.OldFullPath, GDriveFix.GDriveFile.VerPattern))
    {
      _DeleteOlderAndConvert(new FileInfo(e.FullPath), true);
      return;
    }

    if (e.OldName is null || e.Name is null)
    {
      return;
    }
    FileInfo[] images = _GetImagesForPdf(new FileInfo(e.OldFullPath));
    _RenameImages(images, e.OldName, e.Name);

    // After any image-side rename, normalize the new name's group.
    if (Path.GetExtension(e.FullPath) != ".pdf")
    {
      _NormalizeImageFile(e.FullPath);
    }
  }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build NorcusSheetsManager.slnx -c Debug --nologo -v q`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add NorcusSheetsManager.Infrastructure/Manager/Manager.cs
git commit -m "$(cat <<'EOF'
normalize image groups on watcher Renamed events

Watcher_Renamed now calls _NormalizeImageFile on the new path when
the renamed file isn't a PDF. PDF rename behavior (GDrive fixup +
associated-image propagation) is unchanged.
EOF
)"
```

---

## Task 9: Manual verification scenarios

No test project exists. Verification is a scenario script run against a scratch sheets folder. Pick a scratch path, point `Converter.SheetsPath` at it via `appsettings.json` (or `appsettings.Development.json`), restart the service for each scenario, and inspect the result.

For each scenario: arrange the listed files in a fresh subfolder, hit `POST /api/v1/manager/scan` (Swagger UI at `/swagger` is the easiest), list the folder, compare to the expected result.

Touch-files quickly with PowerShell: `'' > song.jpg` (or `New-Item -Path song.jpg -ItemType File -Force`).

- [ ] **Step 1: Set up a scratch folder**

Create a scratch path, set `appsettings.json` `Converter.SheetsPath` to it, and start the service:

```bash
mkdir /tmp/sheets-scratch && mkdir /tmp/sheets-scratch/test
# edit NorcusSheetsManager/appsettings.json: "SheetsPath": "/tmp/sheets-scratch"
dotnet run --project NorcusSheetsManager
```

Or, on Windows, pick `C:\Temp\sheets-scratch` and use `mkdir`/`New-Item`. Restart the service after each scenario (or hit `POST /api/v1/manager/scan` after rearranging files).

- [ ] **Step 2: Run scenarios 1–9 (file-only)**

For each scenario: empty the `test/` subfolder, place the listed source files, hit `POST /api/v1/manager/scan`, list the folder.

| # | Source files                                  | Expected after scan                                 |
| - | --------------------------------------------- | --------------------------------------------------- |
| 1 | `song.jpg`                                    | `song.jpg` (no rename)                              |
| 2 | `song-001.jpg`                                | `song.jpg`                                          |
| 3 | `song.jpg`, `song-001.jpg`                    | `song-001.jpg`, `song-002.jpg`                      |
| 4 | `song-001.jpg`, `song-001.png`                | `song-001.jpg`, `song-002.png`                      |
| 5 | `song.jpg`, `song.png`                        | `song-001.jpg`, `song-002.png`                      |
| 6 | `song-005.jpg`, `song-008.png`                | `song-001.jpg`, `song-002.png`                      |
| 7 | `song.jpg`, `song-001.png`, `other.png`       | `song-001.jpg`, `song-002.png`, `other.png`         |
| 8 | `summer-night.png`                            | `summer-night.png` (no rename)                      |
| 8b| `summer-night.png`, `summer-night-001.png`    | `summer-night-001.png`, `summer-night-002.png`      |
| 9 | (Run scenario 8b's expected output again)     | No rename. Log shows no `Normalized ... → ...` Information line. |

- [ ] **Step 3: Run scenario 10 (watcher trigger)**

With the service running and the folder already containing `song.jpg` only:

```powershell
copy unrelated.png C:\Temp\sheets-scratch\test\song.png
```

(or `cp` on Linux). Within ~1 second, the watcher's Created handler fires and the folder ends up with `song-001.jpg`, `song-002.png`. Verify by `ls`.

- [ ] **Step 4: Run scenario 11 (offline drift, FullScan on startup)**

Stop the service. In `test/`, manually rename `song.jpg` to `song-005.jpg` and add a fresh `song-002.png`. Start the service. After startup logs `Scanning all PDF files in ...`, the folder should end with `song-001.jpg`, `song-002.png` (gaps closed, both now numbered).

- [ ] **Step 5: Confirm idempotency on a steady state**

After all scenarios, run `POST /api/v1/manager/scan` once more on a folder that's already canonical. Expected: no `Normalized ... → ...` log lines at Information level. Folder unchanged.

- [ ] **Step 6: Restore `appsettings.json` and clean up the scratch folder**

```bash
git checkout NorcusSheetsManager/appsettings.json
rm -rf /tmp/sheets-scratch
```

Or restore your real path manually. Don't commit your scratch path.

- [ ] **Step 7: Final smoke build**

Run: `dotnet build NorcusSheetsManager.slnx -c Release --nologo -v q`
Expected: 0 errors, 0 warnings.

No commit at the end of Task 9 — verification is observational, not a code change.

---

## Done

All scan paths and watcher events for image files now produce a normalized folder. The next time someone wants an upload endpoint, they can call `IFileNameNormalizer.NormalizeSong` from the upload handler with the same shape — no further plumbing.
