using System.CommandLine;
using GoogleDriveCli.Commands;
using GoogleDriveCli.Services.Auth;
using GoogleDriveCli.Services.Drive;
using GoogleDriveCli.Services.Local;
using GoogleDriveCli.Services.Manifest;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

// Composition root: build the DI container, then wire commands onto the root.
var services = new ServiceCollection();

// Services — singletons because they hold per-process state
// (auth token, DriveService instance, in-memory manifest).
services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
services.AddSingleton<IDriveClient, DriveClient>();
services.AddSingleton<ILocalFileStore, LocalFileStore>();
services.AddSingleton<IManifestStore, JsonManifestStore>();
services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);

// Commands + their handlers — transient because each command invocation
// is a fresh request and they hold no shared state of their own.
services.AddTransient<LoginCommand>();
services.AddTransient<SyncCommandHandler>();
services.AddTransient<SyncCommand>();
services.AddTransient<SearchCommandHandler>();
services.AddTransient<SearchCommand>();
services.AddTransient<UploadCommandHandler>();
services.AddTransient<UploadCommand>();

await using var provider = services.BuildServiceProvider();

var rootCommand = new RootCommand(
    "Google Drive CLI Manager — sync, search, and upload files in your Google Drive.");

rootCommand.Subcommands.Add(provider.GetRequiredService<LoginCommand>());
rootCommand.Subcommands.Add(provider.GetRequiredService<SyncCommand>());
rootCommand.Subcommands.Add(provider.GetRequiredService<SearchCommand>());
rootCommand.Subcommands.Add(provider.GetRequiredService<UploadCommand>());

return await rootCommand.Parse(args).InvokeAsync();
