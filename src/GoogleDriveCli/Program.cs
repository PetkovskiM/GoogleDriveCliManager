using System.CommandLine;
using GoogleDriveCli.Commands;
using GoogleDriveCli.Services.Auth;
using Microsoft.Extensions.DependencyInjection;

// Composition root: build the DI container, then wire commands onto the root.
var services = new ServiceCollection();
services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
services.AddTransient<LoginCommand>();

await using var provider = services.BuildServiceProvider();

var rootCommand = new RootCommand(
    "Google Drive CLI Manager — sync, search, and upload files in your Google Drive.");

rootCommand.Subcommands.Add(provider.GetRequiredService<LoginCommand>());

return await rootCommand.Parse(args).InvokeAsync();
