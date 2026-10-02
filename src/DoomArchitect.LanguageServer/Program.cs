using OmniSharp.Extensions.LanguageServer.Server;

namespace DoomArchitect.LanguageServer;

/// <summary>
/// A minimal stdio BCS language server - the first concrete slice of
/// this project's LSP work (see TODO/TODO.md): opens/re-parses <c>.bcs</c>
/// files via <see cref="BcsTextDocumentHandler"/> and reports real syntax
/// errors as diagnostics. Hover, completion, go-to-definition, and
/// multi-file `#include` resolution are explicitly deferred, not built
/// here - see that handler's own remarks.
/// </summary>
internal static class Program
{
    private static async Task Main()
    {
        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options => options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .WithHandler<BcsTextDocumentHandler>()
        ).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }
}
