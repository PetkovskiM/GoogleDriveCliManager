[![CI](https://github.com/PetkovskiM/GoogleDriveCliManager/actions/workflows/ci.yml/badge.svg)](https://github.com/PetkovskiM/GoogleDriveCliManager/actions/workflows/ci.yml)

# Google Drive CLI Manager

A command-line tool for managing your Google Drive from the terminal: synchronize files locally with parallel downloads, search across cloud and local state, and upload to specific folders.

> **Status:** authentication works. The functional commands (`sync`, `search`, `upload`) ship in subsequent branches.

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
