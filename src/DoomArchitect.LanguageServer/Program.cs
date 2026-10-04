using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Server;

namespace DoomArchitect.LanguageServer;

/// <summary>
/// A minimal stdio BCS language server - the first concrete slice of
/// this project's LSP work (see TODO/TODO.md): opens/re-parses <c>.bcs</c>
/// files via <see cref="BcsTextDocumentHandler"/>, reports real syntax
/// errors as diagnostics, and answers <c>textDocument/hover</c> (a
/// diagnostic's own message), <c>textDocument/completion</c> (keywords
/// plus every symbol visible from the cursor's position), and
/// <c>textDocument/definition</c> (resolves the word under the cursor to
/// its declaration). Multi-file `#include` resolution is still
/// explicitly deferred, not built here - see the relevant handlers' own
/// remarks.
/// </summary>
internal static class Program
{
    private static async Task Main()
    {
        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options => options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .WithServices(services => services.AddSingleton<BcsDocumentStore>())
            .WithHandler<BcsTextDocumentHandler>()
            .WithHandler<BcsHoverHandler>()
            .WithHandler<BcsCompletionHandler>()
            .WithHandler<BcsDefinitionHandler>()
        ).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }
}
