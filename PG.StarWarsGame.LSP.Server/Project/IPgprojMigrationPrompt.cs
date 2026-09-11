// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>What the user is shown before a project file is rewritten.</summary>
/// <param name="Path">The project file, so the client can open it as the left-hand side.</param>
/// <param name="FileName">Its name, for the title and the question.</param>
/// <param name="ProposedText">The migrated file, verbatim - what will be written if they accept.</param>
/// <param name="Notices">What each migration that ran wants the user told.</param>
public sealed record PgprojMigrationProposal(
    string Path, string FileName, string ProposedText, IReadOnlyList<string> Notices);

/// <summary>
///     Puts a project migration in front of the user and reports what they decided.
/// </summary>
/// <remarks>
///     A question rather than a notification, and a diff rather than a sentence: this rewrites a
///     file the user owns, keeps in version control and shares with a team, so "what exactly
///     changes" is the only useful thing to show. Declining is not a soft no - the client stops the
///     server afterwards, because a project we may not write is one we cannot go on serving
///     consistently.
/// </remarks>
public interface IPgprojMigrationPrompt
{
    Task<bool> ProposeAsync(PgprojMigrationProposal proposal, CancellationToken ct);
}

/// <summary>
///     Asks through the editor: the client opens a diff of the file against the migrated form and
///     answers with the user's choice.
/// </summary>
/// <remarks>
///     The stop on decline is left to the CLIENT rather than done here. A server that exits by
///     itself looks like a crash to <c>vscode-languageclient</c>'s default error handler, which
///     restarts it - straight back into the same proposal, several times over. The client stopping
///     itself is the only version of this that stays stopped.
/// </remarks>
public sealed class WindowPgprojMigrationPrompt(
    ILanguageServerFacade facade, ILogger<WindowPgprojMigrationPrompt> logger) : IPgprojMigrationPrompt
{
    public const string Method = "aet/pgprojMigrationProposal";

    public async Task<bool> ProposeAsync(PgprojMigrationProposal proposal, CancellationToken ct)
    {
        try
        {
            var answer = await facade
                .SendRequest(Method, proposal)
                .Returning<PgprojMigrationAnswer?>(ct);

            return answer?.Accepted == true;
        }
        catch (Exception ex)
        {
            // A client that cannot be asked has not agreed to anything, so nothing is written.
            logger.LogWarning(ex, "Could not put the project migration to the user (non-fatal).");
            return false;
        }
    }
}

/// <summary>The client's answer. Anything other than an explicit yes leaves the file alone.</summary>
public sealed record PgprojMigrationAnswer(bool Accepted);
