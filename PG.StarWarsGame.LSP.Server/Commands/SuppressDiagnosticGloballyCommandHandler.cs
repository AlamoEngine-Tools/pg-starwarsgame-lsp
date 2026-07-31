// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

namespace PG.StarWarsGame.LSP.Server.Commands;

/// <summary>
///     Backs the "Suppress … across the project" quick fix. The other three scopes are text edits
///     the client applies itself; this one writes <c>.aetswg/suppressions.json</c>, so it has to
///     come back to the server.
/// </summary>
public sealed class SuppressDiagnosticGloballyCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = SuppressionCommands.SuppressGlobally;

    private readonly ILogger<SuppressDiagnosticGloballyCommandHandler> _logger;
    private readonly IEnumerable<IDiagnosticsRepublisher> _republishers;
    private readonly IGlobalSuppressionStore _store;

    public SuppressDiagnosticGloballyCommandHandler(
        IGlobalSuppressionStore store,
        IEnumerable<IDiagnosticsRepublisher> republishers,
        ILogger<SuppressDiagnosticGloballyCommandHandler> logger)
    {
        _store = store;
        _republishers = republishers;
        _logger = logger;
    }

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        var raw = request.Arguments is { Count: > 0 } args ? args[0]?.Value<string>() : null;
        if (!SuppressionMatcher.TryParse(raw, out var matcher))
        {
            _logger.LogWarning("{Cmd}: '{Raw}' is not a diagnostic id.", CommandName, raw);
            return Unit.Value;
        }

        _store.Add(matcher, "suppressed from the editor");
        _logger.LogInformation("{Cmd}: {Matcher} suppressed project-wide.", CommandName, matcher);

        // Published diagnostics do not know the store changed, so nothing would disappear until the
        // next edit to each file. Every language is refreshed, not just the one the quick fix came
        // from: the store is project-wide, so the same id may be on screen in all three.
        foreach (var republisher in _republishers)
            try
            {
                await republisher.RepublishAllAsync(ct);
            }
            catch (Exception ex)
            {
                // The suppression is already stored, so one language failing to refresh must not
                // take the others down with it.
                _logger.LogWarning(ex, "{Cmd}: {Publisher} failed to refresh after suppressing {Matcher}.",
                    CommandName, republisher.GetType().Name, matcher);
            }

        return Unit.Value;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}
