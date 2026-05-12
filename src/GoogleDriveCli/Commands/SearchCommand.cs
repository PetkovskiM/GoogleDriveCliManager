using System.CommandLine;

namespace GoogleDriveCli.Commands;

/// <summary>
/// <c>gdrive search &lt;query&gt;</c> — queries Drive by file/folder name and
/// renders each match with its local download status. The actual work lives
/// in <see cref="SearchCommandHandler"/>.
/// </summary>
public sealed class SearchCommand : Command
{
    public SearchCommand(SearchCommandHandler handler)
        : base("search", "Search Drive by file or folder name; mark results that aren't downloaded locally.")
    {
        var queryArgument = new Argument<string>("query")
        {
            Description = "Substring to search for in file or folder names."
        };
        Arguments.Add(queryArgument);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var query = parseResult.GetValue(queryArgument);
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.Error.WriteLine("A non-empty query is required.");
                return 1;
            }
            return await handler.HandleAsync(query, cancellationToken);
        });
    }
}
