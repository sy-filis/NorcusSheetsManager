# Filename normalization

## Goal

Enforce per-folder filename uniqueness for sheet image groups. Files belonging to the same song must occupy unique slots: a song with one image uses the bare name (`song.ext`), a song with two or more images uses sequential numbering (`song-NNN.ext`). The system reconciles automatically whenever the invariant is broken — by user uploads, manual file drops, Google Drive sync, or PDF conversions in mixed-extension folders.

## Algorithm

### Parsing

Each filename in a watched folder is parsed into `(base, page?, ext)`:

1. Greedy match against `^(.+)-(\d+)\.([^.]+)$`. If it matches, the captured groups are `(base, page, ext)`.
2. Otherwise fall back to `^(.+)\.([^.]+)$` and treat `page` as `null`.

Examples:

| File                       | base            | page | ext |
| -------------------------- | --------------- | ---- | --- |
| `song.jpg`                 | `song`          | null | jpg |
| `song-001.jpg`             | `song`          | 1    | jpg |
| `summer-night.png`         | `summer-night`  | null | png |
| `summer-night-001.png`     | `summer-night`  | 1    | png |
| `2024-01-15-summer.png`    | `2024-01-15-summer` | null | png |

PDFs and any extension not in `Converter.WatchedExtensions \ {.pdf}` are excluded.

### Grouping

Files are grouped by `base`. Each group is normalized independently. Groups never cross folder boundaries.

### Target shape per group

- 1 file → target is bare `base.ext` (strip any existing `-NNN` suffix).
- 2 or more files → target is `base-NNN.ext` for each, with `NNN` zero-padded to `Converter.MultiPageCounterLength` and counting up from `Converter.MultiPageInitNumber`.

### Sort within a 2+ group

The sort defines who gets which sequential number:

1. Bare-name files first (no `-NNN` in the source name).
2. Then numbered files in ascending current page order.
3. Tiebreaker: extension, alphabetic, case-insensitive.

Worked examples:

| Source                                    | Result                                       |
| ----------------------------------------- | -------------------------------------------- |
| `song.jpg`, `song-001.jpg`                | `song-001.jpg`, `song-002.jpg`               |
| `song-001.jpg`, `song-001.png`            | `song-001.jpg` (kept), `song-002.png`        |
| `song.jpg`, `song.png`                    | `song-001.jpg`, `song-002.png`               |
| `song-005.jpg`, `song-008.png`            | `song-001.jpg`, `song-002.png`               |
| `summer-night.png`, `summer-night-001.png`| `summer-night-001.png`, `summer-night-002.png`|

### Idempotency

Files already in their target name are left untouched — no rename, no log.

### Rename safety

Naively renaming files in a group can collide (`song-001.jpg → song-002.jpg` while `song-002.jpg` still exists). The pass is two-phase:

1. **Phase 1** — every file whose target differs from its current name is moved to a temp name `<original-filename>.tmp` (e.g., `song-001.jpg` → `song-001.jpg.tmp`). Embedding the original filename in the temp name keeps the file's identity recoverable if the process crashes between phases.
2. **Phase 2** — each `.tmp` file is moved to its computed final target.

This breaks any cycle and prevents target-collision errors mid-pass. **Recovery on next pass:** any file ending in `.tmp` whose stem is otherwise a valid image filename is treated as belonging to its parsed group — `song-001.jpg.tmp` participates as if it were `song-001.jpg`. The next normalization either lands it in its correct target or, if a Phase-1 collision is somehow still pending, leaves it as `.tmp` and logs a Warning. No file is silently lost.

## Component layout

A new infrastructure service `NorcusSheetsManager.Infrastructure.Manager.FileNameNormalizer` — sibling of `Converter`, `GDriveFix`, `Manager`. Stateless. Takes `AppConfig` and `ILogger<FileNameNormalizer>` via primary constructor.

Public interface in `NorcusSheetsManager.Application.Abstractions.Services.IFileNameNormalizer`:

```csharp
public interface IFileNameNormalizer
{
  void NormalizeFolder(string folderPath);
  void NormalizeSong(string folderPath, string baseName);
}
```

- `NormalizeFolder` — enumerates the folder once, groups everything by base name, applies the algorithm. Used by the scan-end hook.
- `NormalizeSong` — operates on just one base-name group within a folder. Used by the watcher hook (and any future caller that already knows which song's files just changed, e.g., an upload endpoint).

Both methods are synchronous: rename operations are fast, the existing Converter / scan code is already synchronous in its hot path, and async would add complexity without payoff.

DI registration in `Infrastructure/DependencyInjection.cs`:

```csharp
services.AddSingleton<IFileNameNormalizer, FileNameNormalizer>();
```

Injected into `Manager` (the watcher handlers and scan methods live there).

Internal helpers, all `private static` on `FileNameNormalizer`:

- `ParseName(fileName)` → `(string Base, int? Page, string Ext)?`
- `BuildTargetName(baseName, page?, ext, counterLength, initNumber)` → string
- `PlanRenames(group, settings)` → `IReadOnlyList<(string From, string To)>` — pure function, easy to reason about.

The class stays small (~150 lines) and has a single responsibility, separate from `Converter` (PDF→image) and `GDriveFix` (deduping `(N)` suffixes).

## Hook points

Two integration points. The Converter post-convert hook is intentionally omitted — see the rationale at the end of this section.

### 1. Watcher `Created` and `Renamed` handlers in `Manager`

When the event is for an image extension (i.e., in `Converter.WatchedExtensions \ {.pdf}`):

```csharp
private void OnFileChanged(object sender, FileSystemEventArgs e)
{
  if (!_IsImage(e.FullPath)) { /* existing PDF logic */ return; }

  string folder = Path.GetDirectoryName(e.FullPath)!;
  string baseName = _ParseBaseName(e.Name);

  StopWatching(folder);
  try { _normalizer.NormalizeSong(folder, baseName); }
  finally { StartWatching(folder); }
}
```

Same `StopWatching` / `StartWatching` bracket pattern that `GDriveFix` already uses. Per-subfolder watchers mean only the affected subfolder's watcher pauses, not all of them.

### 2. Scan end

At the tail of `FullScan`, `DeepScan`, and `ForceConvertAll`, after all conversions complete, iterate the top-level subfolders and call `_normalizer.NormalizeFolder(subfolder)` on each. Scans already run with the watcher off, so no loop.

### Why no Converter post-convert hook

When the Converter overwrites images for a PDF, it cleans up its own files for that base name *and* output format — so the only invariant violation that survives a conversion is the mixed-extension case (a manual `song.jpg` sitting next to a Converter-produced `song.png` group). For scan-driven conversions that's a non-issue — scan-end normalization runs in the same operation. For watcher-triggered conversions in mixed-extension folders, the violation persists until the next manual scan, but mixed-extension layouts are uncommon and the consequence is purely cosmetic file naming. The simplicity of two hooks instead of three is worth the small window.

## Edge cases and error handling

- **File locked or in use.** Rename can fail with `IOException`. Log a warning with file path and exception message, abort that group's pass, leave the folder in a partial state. The two-phase rename means partial state is a mix of original names and `<guid>.tmp` names — recoverable on next normalization. Logged at Warning, not Error: this is recoverable transient state, not a system failure.
- **File deleted between enumeration and rename.** Catch `FileNotFoundException` / `DirectoryNotFoundException`, log at Debug, skip that file, continue.
- **Folder doesn't exist.** Short-circuit cleanly: log Debug, return.
- **Empty group / lone canonical file.** No work to do. Skip silently — no rename, no log.
- **Stale watcher events.** `Renamed` reports a path that may have already been renamed by our prior call. Same handling as "file deleted between enumeration and rename" — Debug-log, return.
- **Concurrent watcher events for the same group.** Existing Manager-level Created/Renamed lock plus normalization idempotency means re-entry is safe. No per-group lock needed.
- **Cross-volume rename.** All operations are intra-folder, so always intra-volume. `File.Move` is atomic.
- **Hyphenated base names.** `summer-night.png` parses as `("summer-night", null, "png")`; `2024-01-15-summer.png` parses as `("2024-01-15-summer", null, "png")`. The greedy regex anchors `-NNN.<ext>` only at the very end, so no false positives.
- **Logging.** Each successful rename: Information level, structured: `_logger.LogInformation("Normalized {From} → {To} in {Folder}.", from, to, folder)`. A whole-folder summary at Debug: count of groups touched, count of files renamed.

## Verification

No test project. Verification is a manual scenario script run against a scratch folder. The same scenarios serve as a regression checklist for future refactors.

For each scenario: arrange the listed files in a fresh subfolder, hit `POST /api/v1/manager/scan` (or restart the service), list the folder, compare to the expected result.

1. **Bare alone** — `song.jpg` → no rename.
2. **Numbered alone** — `song-001.jpg` → `song.jpg`.
3. **Bare + numbered, same ext** — `song.jpg`, `song-001.jpg` → `song-001.jpg`, `song-002.jpg`.
4. **Same number, two extensions** — `song-001.jpg`, `song-001.png` → `song-001.jpg` (kept), `song-002.png`.
5. **Two bare, two extensions** — `song.jpg`, `song.png` → `song-001.jpg`, `song-002.png`.
6. **Gappy numbering** — `song-005.jpg`, `song-008.png` → `song-001.jpg`, `song-002.png`.
7. **Multiple groups** — `song.jpg`, `song-001.png`, `other.png` → `song-001.jpg`, `song-002.png`, `other.png` (the `other` group is unaffected).
8. **Hyphenated base name** — `summer-night.png` alone → no rename. `summer-night.png` + `summer-night-001.png` → `summer-night-001.png`, `summer-night-002.png`.
9. **Idempotency** — run scenario 8's expected output through normalization a second time → no rename, no Information-level log line.
10. **Watcher trigger** — with the service running, drop a second-extension file into a folder that already has one file → watcher handler fires, both files end up numbered.
11. **Scan trigger after offline drift** — stop the service, manually create a violating layout in a watched subfolder, start the service → `FullScan` on startup ends with the folder normalized.

Failure-path behaviors (locked file, mid-rename interrupt) are covered by code review of the two-phase rename + recovery path rather than a scenario run.

## Out of scope

Deliberately excluded from this design:

- **No upload endpoint.** When/if one lands later, it calls `IFileNameNormalizer.NormalizeSong` directly. The interface is shaped to support it but no endpoint is built here.
- **No config flag for opt-out.** Always-on. YAGNI.
- **No PDF normalization.** PDFs are excluded from grouping; the existing `MovePdfToSubfolder` / archive flow is untouched.
- **No cross-folder operations.** Each top-level subfolder is its own normalization scope. No deduplication across folders.
- **No Converter post-convert hook.** See Hook points → "Why no Converter post-convert hook".
