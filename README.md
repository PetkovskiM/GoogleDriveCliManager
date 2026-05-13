[![CI](https://github.com/PetkovskiM/GoogleDriveCliManager/actions/workflows/ci.yml/badge.svg)](https://github.com/PetkovskiM/GoogleDriveCliManager/actions/workflows/ci.yml)

# Google Drive CLI Manager

A command-line tool for managing your Google Drive from the terminal: synchronize files locally with parallel downloads, search across cloud and local state, and upload to specific folders. .NET 10, single produced binary (`gdrive`).

> **Status:** all four commands (`login`, `sync`, `search`, `upload`) are implemented and tested. 29 unit + integration tests, including a 16-way concurrent stress test for the parallel statistics class.

## Quick start

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Do the one-time **[Google Cloud setup](#setting-up-google-cloud)** (~5 min) to get a `client_secret.json` and place it at the repo root.
3. From the repo root:

   ```bash
   dotnet run --project src/GoogleDriveCli -- login        # browser opens for consent
   dotnet run --project src/GoogleDriveCli -- sync         # downloads your Drive into ./Downloads
   dotnet run --project src/GoogleDriveCli -- search foo   # search by name with sync status
   dotnet run --project src/GoogleDriveCli -- upload <local-file> "Folder/Subfolder"
   ```

   Or install as a standalone `gdrive` binary — see [Build, test, install](#build-test-install).

## Commands at a glance

| Command | Usage | Description |
|---|---|---|
| `login` | `gdrive login` | OAuth 2.0 browser flow; persists the token for all subsequent commands. |
| `sync` | `gdrive sync [--dry-run]` | Parallel download of all Drive files into `./Downloads` (folder structure mirrored). Incremental on repeat runs. `--dry-run` prints what would happen without writing. |
| `search` | `gdrive search <query>` | Drive-side query by name. Renders matches in a table with location and `Downloaded` / `[Not Downloaded]` status. |
| `upload` | `gdrive upload <local-path> <drive-path>` | Upload a local file; creates any missing folder segments in Drive automatically. Pass `""` or `"/"` to upload to My Drive root. |

`gdrive --help` and `gdrive <command> --help` print the same information at the terminal.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100 or later). Verify with:

  ```bash
  dotnet --list-sdks
  ```

## Setting up Google Cloud

The CLI talks to the Google Drive API via OAuth 2.0. Every user — including a reviewer — needs their own Google Cloud project and a downloaded `client_secret.json`. This is a one-time setup, ~5 minutes.

> **A note on the "client secret":** despite the file name, `client_secret.json` for an installed (desktop) OAuth app is an *application identifier*, not a runtime credential. Google's own documentation says it's "not treated as a secret" in this context. The actual session credential is the OAuth refresh token, which the CLI persists DPAPI-encrypted at `%APPDATA%\GoogleDriveCliManager\tokens\` (Windows) / `~/.config/GoogleDriveCliManager/tokens/` (Linux, macOS). The `.gitignore` still excludes `client_secret.json` — best practice — but the security model does not depend on it staying private.

1. **Create a project** at <https://console.cloud.google.com/projectcreate>. Name it anything; the project is private to your Google account.

2. **Enable the Drive API.** Navigate to **APIs & Services → Library**, search for *Google Drive API*, click **Enable**.

3. **Configure the OAuth consent screen.** Navigate to **APIs & Services → OAuth consent screen** and choose **External** (or **Internal** if you have a Google Workspace organization).
   - App name: e.g. `GoogleDriveCliManager`. Fill in your own email for support / developer contact.
   - Scopes: leave empty. The application requests scopes at runtime.
   - **Test users:** add the Google account you intend to authenticate with. While the app stays in *Testing* status this is required, and you do not need verification. **Skip this step and you'll get `Error 403: access_denied` when you try to log in.**

4. **Create OAuth client credentials.** Navigate to **APIs & Services → Credentials → Create credentials → OAuth client ID**.
   - Application type: **Desktop app**.
   - Name: anything (e.g. `GoogleDriveCliManager Desktop`).
   - Click **Create**.

5. **Download `client_secret.json`.** On the Credentials page, click the download button next to your new OAuth client. Rename the file to `client_secret.json` and place it in the repository root, alongside `GoogleDriveCliManager.slnx`:

   ```
   GoogleDriveCliManager/
   ├── client_secret.json          <- here
   ├── GoogleDriveCliManager.slnx
   ├── src/
   └── tests/
   ```

   The CLI also looks in the directory containing the executable as a fallback — useful if you publish a standalone `gdrive` binary (see below) and want to keep `client_secret.json` next to it instead of pinning your working directory.

## Build, test, install

### Develop (run from source)

```bash
dotnet build
dotnet test
dotnet run --project src/GoogleDriveCli -- --help
```

### Install as a standalone `gdrive` binary (optional)

The dev loop above works fine indefinitely. To use `gdrive` directly without `dotnet run`, publish the project and put the output on your `PATH`:

```bash
# Framework-dependent (small; requires .NET 10 runtime on the target machine)
dotnet publish src/GoogleDriveCli -c Release -o ./publish

# OR self-contained for Windows x64 (~70 MB; runs on any Windows 10/11 machine)
dotnet publish src/GoogleDriveCli -c Release -r win-x64 --self-contained true -o ./publish-win
```

Then add the chosen output directory to your `PATH`. On Windows, the simplest two options:

**Option A — Windows GUI:**
1. Press <kbd>Win</kbd>, type *environment variables*, choose **Edit the system environment variables**.
2. In the System Properties dialog, click **Environment Variables...**
3. Under **User variables for your account**, select **Path**, click **Edit...**
4. Click **New** and paste the full path, for example `D:\GIT\GoogleDriveCliManager\publish`.
5. Click **OK** through all three dialogs.
6. **Close every open terminal.** New `PATH` only applies to terminals opened after the change.

**Option B — PowerShell one-liner (persists across sessions):**

```powershell
[Environment]::SetEnvironmentVariable("PATH", $env:PATH + ";D:\GIT\GoogleDriveCliManager\publish", "User")
```

Open a new terminal and verify:

```cmd
gdrive --help
```

After install, `gdrive login`, `gdrive sync`, etc. work from any directory — as long as `client_secret.json` is either in your current directory or next to `gdrive.exe`. The latter is the more "installed" feel.

## Architecture

The project is intentionally small and layered. Everything below ships under `src/GoogleDriveCli/` and is wired together in `Program.cs` (the composition root).

```
Program.cs                      composition root: DI container, root command, subcommand registration
  │
  ├─ Commands/                  thin CLI surface — parse args, delegate to handlers
  │     LoginCommand
  │     SyncCommand     →  SyncCommandHandler
  │     SearchCommand   →  SearchCommandHandler
  │     UploadCommand   →  UploadCommandHandler
  │
  ├─ Services/                  logic; each behind an interface for DI + testability
  │     IGoogleAuthService → GoogleAuthService     OAuth installed-app flow + FileDataStore token persistence
  │     IDriveClient       → DriveClient           Drive API wrapper (list, search, download, upload)
  │     ILocalFileStore    → LocalFileStore        writes the Downloads/ tree
  │     IManifestStore     → JsonManifestStore     per-file download state, JSON-persisted
  │     DrivePathResolver                          pure function: Drive parents chain → relative path
  │
  ├─ Models/                    record-typed DTOs
  │     DriveFile, ManifestEntry, SyncStatistics, FailedFile
  │
  └─ Common/                    cross-cutting utilities
        ByteFormatter
```

### Parallel download strategy

The `sync` command uses `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 8`. Three things to know about that choice:

- **Bounded, not unbounded.** `Task.WhenAll(files.Select(download))` would issue thousands of concurrent requests instantly and crash against Google's quota (1000 requests / 100 seconds per user). `Parallel.ForEachAsync` caps in-flight work.
- **Not `Environment.ProcessorCount`.** That's the right reasoning for *CPU-bound* parallelism. Sync is *I/O-bound* — workers spend ~99% of their time waiting on the network. 8 keeps a typical home connection saturated while leaving generous quota headroom. The constant lives in one place and is trivially tunable.
- **Not `System.Threading.Channels`.** A producer-consumer channel between the lister and the downloaders would let downloads start before listing finishes — but for this shape of work there's nothing meaningful to overlap. Drive's paged listing returns in milliseconds and the bulk of wall-clock time is the parallel downloads. A channel would add machinery (channel writer/reader plumbing, completion signaling, error propagation across the producer/consumer boundary) without buying a measurable win. `Parallel.ForEachAsync` is the smaller surface for this workload.

### Thread-safe statistics — `SyncStatistics`

Hot path for the parallel sync, designed to be hit by every worker simultaneously **without any locks**:

- 64-bit counter fields mutated only via `Interlocked.Increment` and `Interlocked.Add` — single atomic CPU instructions, roughly 10× cheaper than `lock` because there is no thread context switch.
- Failure records collect into a `ConcurrentBag<FailedFile>`, optimized for the "many writers, one reader at the end" access pattern this command has.
- Reads use `Interlocked.Read` so the values are torn-read-safe on 32-bit platforms (where a regular `long` read is two 32-bit operations).
- No `lock` keyword and no `volatile` field anywhere in the codebase.

The 16-way concurrent stress test in `SyncStatisticsTests` validates this: 160 000 increments across 16 workers must produce exactly 160 000 — and they do, because every increment is atomic.

The manifest (`JsonManifestStore`) uses a `ConcurrentDictionary<string, ManifestEntry>` internally so worker tasks can `AddOrUpdate` concurrently. Its `LoadAsync` and `SaveAsync` run once each, single-threaded, at sync boundaries.

`DriveClient` initializes the underlying `Google.Apis.Drive.v3.DriveService` lazily via **double-checked locking** with a `SemaphoreSlim` — concurrent first-callers don't both build a service, and the steady-state read is a single non-locked field check.

### State management — manifest + filesystem hybrid

Both `sync` and `search` need to answer "is this Drive file already downloaded locally?" Two simple approaches each fall short:

| Approach | Problem |
|---|---|
| Filesystem only — check `File.Exists(localPath)` | Loses the `modifiedTime` comparison; incremental sync re-downloads unchanged files. |
| Manifest only — JSON is the source of truth | Wrong if the user manually deletes from `Downloads/`. |

The implementation uses **both**: a manifest entry says "downloaded, with this modified-time at the time"; a filesystem check confirms the local file is still there. Both must hold for sync to skip (or for search to render as `Downloaded`). This is the case the `HandleAsync_FileInManifestButLocalCopyMissing_RendersAsNotDownloaded` test exercises directly.

### Error handling

- **Per-file failures during sync** are caught inside the parallel lambda, recorded in `SyncStatistics`, and shown in the summary table. One bad file does not abort the whole sync.
- **Transient HTTP errors** (5xx, network blips) are retried automatically by the Google SDK's built-in `ExponentialBackOffPolicy`. We deliberately did not add Polly on top — it would be duplicative.
- **Cancellation** flows through every async hop via `CancellationToken`. On Ctrl+C the parallel loop unwinds, the manifest is persisted in a `finally`, and the next run resumes from the partial state.
- **Corrupted manifest on load** (interrupted write, hand-edit, disk glitch) falls back silently to an empty in-memory manifest with a stderr warning, rather than crashing the command. The next sync rebuilds and re-saves.
- **User-input errors** (missing local file, empty search query, missing `client_secret.json`) print a clear single-line message pointing at the relevant README section and exit non-zero rather than throwing a stack trace.

## Command details

### `login`

```bash
gdrive login
```

First invocation opens your default browser to Google's consent screen; after you approve, the credential is persisted to `%APPDATA%\GoogleDriveCliManager\tokens\` (DPAPI-encrypted on Windows) and reused by every subsequent command. Subsequent `login` runs print `Authenticated as <email>.` and return instantly — useful as a "who am I" / "is auth still valid" check.

To forget the credential and force re-authentication, delete the token directory:

| OS | Path |
|---|---|
| Windows | `%APPDATA%\GoogleDriveCliManager\tokens\` |
| macOS / Linux | `~/.config/GoogleDriveCliManager/tokens/` |

### `sync`

```bash
gdrive sync [--dry-run]
```

Lists every file in your Drive and downloads them into a local `Downloads/` folder at the repository root, mirroring Drive's folder structure — a file at `My Drive/Work/proj-a/notes.txt` lands at `Downloads/Work/proj-a/notes.txt`. With `--dry-run`, the listing and decision logic still runs (so you see counts and what *would* download), but no files are written to disk and the manifest is not modified.

| Aspect | Behaviour |
|---|---|
| **Parallelism** | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 8`. The work is I/O-bound (HTTP requests), not CPU-bound, so the limit is not `Environment.ProcessorCount` — it is chosen to keep the network pipeline saturated while staying well under Google's quota of 1000 requests / 100 seconds per user. |
| **Thread safety** | Statistics use `Interlocked` primitives on 64-bit counter fields plus a `ConcurrentBag` for failure records — completely lock-free. The manifest uses a `ConcurrentDictionary` so worker tasks can update it without external synchronization. |
| **Resilience** | Each download is wrapped in `try/catch`; one failed file does not abort the sync. Transient HTTP errors are retried automatically by the Google SDK's built-in exponential-backoff policy. |
| **Incremental** | A JSON manifest at `%APPDATA%\GoogleDriveCliManager\manifest.json` records every successful download. On subsequent runs, files whose Drive `modifiedTime` matches the manifest entry (and whose local copy is still present) are skipped. |
| **Cancellation** | Ctrl+C cleanly stops the sync. The manifest is persisted in a `finally` block, so a re-run resumes from where the previous one stopped. |
| **Native Google files** | Docs, Sheets, Slides, and Forms have no direct binary content (they live in Google's proprietary format) and would require `Files.Export` to download as Office formats. To keep scope focused, this implementation skips them with a one-line warning. |

Example output:

```
Listing files from Google Drive...
Found 42 downloadable file(s); skipping 3 Google native file(s).
Downloads root: D:\GIT\GoogleDriveCliManager\Downloads
Syncing  ━━━━━━━━━━━━━━━━━━━━━━━━━━ 100% 00:00:00
╭──────────────────────┬───────────╮
│ Metric               │ Value     │
├──────────────────────┼───────────┤
│ Downloaded           │ 42        │
│ Skipped (up-to-date) │ 0         │
│ Failed               │ 0         │
│ Total bytes          │ 12.4 MB   │
│ Elapsed              │ 00:00:09  │
╰──────────────────────┴───────────╯
```

### `search`

```bash
gdrive search <query>
```

Queries Google Drive itself (not just the local cache) by file or folder name, then renders every match in a table with its local sync status. The **Location** column shows each result's parent folder in Drive, so files with the same name in different folders are immediately distinguishable.

Each row's **Status** column is one of:

| Status | Meaning |
|---|---|
| `Downloaded` | A manifest entry exists for this file **and** the local copy is still on disk. |
| `[Not Downloaded]` | The file matches in Drive but isn't on disk locally (either never synced, or the local copy was deleted). |
| `—` | Folders and Google-native files (Docs/Sheets/Slides) — these aren't downloaded in our model, so the status is not applicable. |

Example:

```
Found 4 result(s) for "notes":
╭───────────────────┬──────────┬────────┬───────────────┬──────────────────╮
│ Name              │ Type     │ Size   │ Location      │ Status           │
├───────────────────┼──────────┼────────┼───────────────┼──────────────────┤
│ Notes             │ Folder   │ —      │ My Drive      │ —                │
│ meeting-notes.txt │ txt      │ 2.1 KB │ Work/proj-a   │ Downloaded       │
│ trip-notes.md     │ md       │ 4.7 KB │ Personal      │ [Not Downloaded] │
│ Project Notes     │ document │ —      │ Work          │ —                │
╰───────────────────┴──────────┴────────┴───────────────┴──────────────────╯
```

### `upload`

```bash
gdrive upload <local-path> <drive-path>
```

Uploads a local file to a specific folder in Google Drive. If any segment of the destination path doesn't exist, it is created automatically — you'll see a `Creating folder: <name>` line for each one.

| Argument | Description |
|---|---|
| `local-path` | Path to the file on your machine. Must exist; otherwise the command exits with a clear error. |
| `drive-path` | Destination folder in Drive. Use `/` as the segment separator (backslashes are accepted and normalized). Pass `""` or `"/"` to upload to My Drive root. |

Examples:

```bash
# Upload to a nested folder; "Reports" is created if missing
gdrive upload C:\reports\Q1.xlsx "Work/Reports"

# Upload to My Drive root
gdrive upload .\notes.txt ""
```

Behavior notes:

- **Drive file name** = the local filename. There is no rename flag.
- **Name collisions** in the target folder do not raise an error — Drive permits multiple files with the same name (each has its own immutable file ID). The upload proceeds and creates a new Drive file.
- **MIME type** is sent as `application/octet-stream`; Drive infers the displayed type from the filename extension.

## Project layout

```
GoogleDriveCliManager/
├── GoogleDriveCliManager.slnx                  # solution (.NET 10 XML format)
├── global.json                                 # pins to stable .NET 10 SDK
├── LICENSE                                     # MIT
├── .github/workflows/ci.yml                    # CI: build + test on push and PR
├── src/GoogleDriveCli/
│   ├── Program.cs                              # composition root, DI + commands
│   ├── Commands/
│   │   ├── LoginCommand.cs
│   │   ├── SyncCommand.cs / SyncCommandHandler.cs
│   │   ├── SearchCommand.cs / SearchCommandHandler.cs
│   │   └── UploadCommand.cs / UploadCommandHandler.cs
│   ├── Services/
│   │   ├── Auth/      (IGoogleAuthService, GoogleAuthService)
│   │   ├── Drive/     (IDriveClient, DriveClient)
│   │   ├── Local/     (ILocalFileStore, LocalFileStore, DrivePathResolver)
│   │   └── Manifest/  (IManifestStore, JsonManifestStore)
│   ├── Models/        (DriveFile, ManifestEntry, SyncStatistics, FailedFile)
│   ├── Common/        (ByteFormatter)
│   └── GoogleDriveCli.csproj
└── tests/GoogleDriveCli.Tests/
    ├── SyncStatisticsTests.cs                  # 16-way concurrent stress test
    ├── ManifestStoreTests.cs
    ├── DrivePathResolverTests.cs
    ├── SyncCommandHandlerTests.cs              # integration; mocked IDriveClient
    ├── SearchCommandHandlerTests.cs            # integration; TestConsole captures output
    ├── UploadCommandHandlerTests.cs
    ├── GoogleAuthServiceTests.cs
    └── GoogleDriveCli.Tests.csproj
```

## Branch history

The project was built incrementally; each PR was a coherent unit of work merged into `main`:

| Branch | Scope |
|---|---|
| `chore/scaffold` | Solution, projects, CI, README skeleton |
| `feat/auth` | OAuth 2.0 flow, `gdrive login`, token persistence |
| `feat/sync` | Parallel download of all Drive files, manifest, statistics, cancellation, `--dry-run` |
| `feat/search` | Drive query with location column and local sync-status markers |
| `feat/upload` | Upload a local file to a Drive folder (creating the path if needed) |
| `chore/polish` | Architecture write-up, code consistency fixes, license, robustness pass |

## License

[MIT](LICENSE).
