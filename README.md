[![CI](https://github.com/PetkovskiM/GoogleDriveCliManager/actions/workflows/ci.yml/badge.svg)](https://github.com/PetkovskiM/GoogleDriveCliManager/actions/workflows/ci.yml)

# Google Drive CLI Manager

A command-line tool for managing your Google Drive from the terminal: synchronize files locally with parallel downloads, search across cloud and local state, and upload to specific folders.

> **Status:** authentication, parallel sync, and search work. `upload` ships in a subsequent branch.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100 or later)

Verify your installation:

```bash
dotnet --list-sdks
```

## Build

```bash
git clone https://github.com/<your-username>/GoogleDriveCliManager.git
cd GoogleDriveCliManager
dotnet build
```

## Run

```bash
dotnet run --project src/GoogleDriveCli -- --help
```

Once installed and on the `PATH`, the produced binary is named `gdrive`.

## Setting up Google Cloud

The CLI talks to the Google Drive API via OAuth 2.0. Every user — including a reviewer — needs their own Google Cloud project and a downloaded `client_secret.json`. This is a one-time setup, ~5 minutes.

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

   The `.gitignore` excludes this file. **Never commit it** — it is the secret that identifies your Google Cloud project.

## Authentication

```bash
dotnet run --project src/GoogleDriveCli -- login
```

The first time, your default browser opens to Google's consent screen. After you approve access for the test user you registered, the credential is persisted locally and reused for every subsequent command — `sync`, `search`, and `upload` invoke the same flow internally and will refresh the token silently when needed.

On success you'll see:

```
Authenticated as you@example.com.
```

Token storage location (DPAPI-encrypted on Windows; per-user readable on macOS/Linux):

| OS | Path |
|---|---|
| Windows | `%APPDATA%\GoogleDriveCliManager\tokens\` |
| macOS / Linux | `~/.config/GoogleDriveCliManager/tokens/` |

To forget the credential and force re-authentication, delete that directory.

## Sync

```bash
dotnet run --project src/GoogleDriveCli -- sync
```

Downloads every file from your Google Drive into a local `Downloads/` folder at the repository root. The folder structure mirrors your Drive — a file at `My Drive/Work/proj-a/notes.txt` lands at `Downloads/Work/proj-a/notes.txt`.

| Aspect | Behaviour |
|---|---|
| **Parallelism** | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 8`. The work is I/O-bound (HTTP requests), not CPU-bound, so the limit is not `Environment.ProcessorCount` — it is chosen to keep the network pipeline saturated while staying well under Google's quota of 1000 requests / 100 seconds per user. |
| **Thread safety** | Statistics use `Interlocked` primitives on 64-bit counter fields plus a `ConcurrentBag` for failure records — completely lock-free. The manifest uses a `ConcurrentDictionary` so worker tasks can update it without external synchronization. |
| **Resilience** | Each download is wrapped in `try/catch`; one failed file does not abort the sync. Transient HTTP errors are retried automatically by the Google SDK's built-in exponential-backoff policy. |
| **Incremental** | A JSON manifest at `%APPDATA%\GoogleDriveCliManager\manifest.json` records every successful download. On subsequent runs, files whose Drive `modifiedTime` matches the manifest entry (and whose local copy is still present) are skipped. |
| **Cancellation** | Ctrl+C cleanly stops the sync. The manifest is persisted in a `finally` block, so a re-run resumes from where the previous one stopped. |
| **Native Google files** | Docs, Sheets, Slides, and Forms have no direct binary content (they live in Google's proprietary format) and would require `Files.Export` to download as Office formats. To keep scope focused, this implementation skips them with a one-line warning. |

### Flags

| Flag | Description |
|---|---|
| `--dry-run` | List what would be downloaded without writing any files. Useful before a large sync. |

### Example output

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

## Search

```bash
dotnet run --project src/GoogleDriveCli -- search <query>
```

Queries Google Drive itself (not just the local cache) by file or folder name, then renders every match in a table with its local sync status. This is the spec's "Find everything available in the cloud, mark what's not yet on disk" requirement.

Each row's **Status** column is one of:

| Status | Meaning |
|---|---|
| `Downloaded` | A manifest entry exists for this file **and** the local copy is still on disk. |
| `[Not Downloaded]` | The file matches in Drive but isn't on disk locally (either never synced, or the local copy was deleted). |
| `—` | Folders and Google-native files (Docs/Sheets/Slides) — these aren't downloaded in our model, so the status is not applicable. |

The "manifest entry **and** local file still exists" check is the hybrid state-management approach the task spec asks about: the manifest is the primary source of truth, but each lookup also verifies the local file is still there, so a user who manually deletes from `Downloads/` still sees an accurate Status.

### Example

The **Location** column shows each result's parent folder in Drive, so files with the same name in different folders are immediately distinguishable.

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

## Test

```bash
dotnet test
```

## Project layout

```
GoogleDriveCliManager/
├── GoogleDriveCliManager.slnx           # solution (.NET 10 XML format)
├── src/GoogleDriveCli/                  # main CLI project (produces `gdrive`)
│   ├── Program.cs                       # composition root, command wiring
│   └── GoogleDriveCli.csproj
├── tests/GoogleDriveCli.Tests/          # xUnit test project
│   └── GoogleDriveCli.Tests.csproj
└── .github/workflows/ci.yml             # build + test on push and PR
```

Folders for `Commands/`, `Services/`, and `Models/` will be added as feature branches land.

## Roadmap

| Branch | Commands / capabilities |
|---|---|
| `chore/scaffold` | Solution, projects, CI, README skeleton |
| `feat/auth` | OAuth 2.0 flow, `gdrive login`, token persistence |
| `feat/sync` | Parallel download of all Drive files, manifest, statistics, cancellation, `--dry-run` |
| `feat/search` | Drive query with local sync-status markers |
| `feat/upload` | Upload a local file to a Drive folder (creating the path if needed) |
| `chore/polish` | Architecture write-up, demo recording, error-message refinements |

## License

To be added before submission.
