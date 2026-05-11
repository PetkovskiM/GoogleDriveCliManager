using System.CommandLine;

var rootCommand = new RootCommand(
    "Google Drive CLI Manager — sync, search, and upload files in your Google Drive.");

// Subcommands will be added in subsequent feature branches:
//   sync   — download all Drive files in parallel
//   search — query files by name with local download status
//   upload — push a local file to a Drive folder

return await rootCommand.Parse(args).InvokeAsync();
