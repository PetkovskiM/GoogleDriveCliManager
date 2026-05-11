# Google Drive CLI Manager

A command-line tool for managing your Google Drive from the terminal: synchronize files locally with parallel downloads, search across cloud and local state, and upload to specific folders.

> **Status:** project scaffolding only. Functional commands (`sync`, `search`, `upload`) ship in subsequent branches.

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
| `chore/scaffold` (this) | Solution, projects, CI, README skeleton |
| `feat/auth` | OAuth 2.0 flow, token persistence |
| `feat/sync` | Parallel download of all Drive files, manifest, statistics, cancellation, `--dry-run` |
| `feat/search` | Drive query with local sync-status markers |
| `feat/upload` | Upload a local file to a Drive folder (creating the path if needed) |
| `chore/polish` | Architecture write-up, demo recording, error-message refinements |

## License

To be added before submission.
