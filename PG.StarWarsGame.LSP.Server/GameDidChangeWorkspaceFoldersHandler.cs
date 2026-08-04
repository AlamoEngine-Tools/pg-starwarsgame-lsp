// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     Keeps the open project set in step with the editor's workspace folders. Adding a folder that
///     contains a <c>.pgproj</c> brings that project online without a restart; removing one takes its
///     project (and its diagnostics) away. The expensive shared state - schema, baseline, asset and
///     bone catalogs - is already loaded by this point, so a newly added folder only pays for its own
///     scan.
/// </summary>
public sealed class GameDidChangeWorkspaceFoldersHandler : DidChangeWorkspaceFoldersHandlerBase
{
    private readonly IEnumerable<IDocumentDiagnosticsClearer> _clearers;
    private readonly IGameWorkspaceHost _documents;
    private readonly IProjectRegistry _registry;
    private readonly WorkspaceFolderChangeService _service;

    public GameDidChangeWorkspaceFoldersHandler(
        IModProjectReloadService reloadService,
        IProjectRegistry registry,
        IGameWorkspaceHost documents,
        IEnumerable<IDocumentDiagnosticsClearer> clearers,
        ILogger<GameDidChangeWorkspaceFoldersHandler> logger)
    {
        _registry = registry;
        _documents = documents;
        _clearers = clearers;
        _service = new WorkspaceFolderChangeService(reloadService, logger);
    }

    public override async Task<Unit> Handle(DidChangeWorkspaceFoldersParams request, CancellationToken ct)
    {
        var added = ToPaths(request.Event?.Added);
        var removed = ToPaths(request.Event?.Removed);

        // Captured before the reload: afterwards the removed project is gone from the registry and
        // there is no way left to tell which files it used to own.
        var orphaned = removed.Count > 0 ? UrisUnder(removed) : [];

        await _service.ApplyAsync(added, removed, ct);

        // A removed folder's files keep whatever diagnostics were last published for them until
        // something replaces them - and nothing will, because they are no longer indexed anywhere.
        foreach (var uri in orphaned)
        foreach (var clearer in _clearers)
            clearer.ClearDocument(uri);

        return Unit.Value;
    }

    // Registering this handler is what makes OmniSharp advertise
    // workspace.workspaceFolders.supported, so the client starts sending the notification.
    protected override DidChangeWorkspaceFolderRegistrationOptions CreateRegistrationOptions(
        ClientCapabilities clientCapabilities)
    {
        return new DidChangeWorkspaceFolderRegistrationOptions
        {
            Supported = true,
            ChangeNotifications = true
        };
    }

    private static List<string> ToPaths(Container<WorkspaceFolder>? folders)
    {
        if (folders is null) return [];

        var paths = new List<string>();
        foreach (var folder in folders)
        {
            var path = folder.Uri.GetFileSystemPath();
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        return paths;
    }

    private List<string> UrisUnder(IReadOnlyList<string> removedFolders)
    {
        var orphaned = new List<string>();
        foreach (var workspace in _registry.All)
        foreach (var uri in workspace.Index.Current.Documents.Keys)
            if (removedFolders.Any(folder => IsUnder(uri, folder)))
                orphaned.Add(uri);

        // Open buffers count too: a file can be open (and carrying diagnostics) without having been
        // reachable by the workspace scan.
        foreach (var doc in _documents.All)
            if (removedFolders.Any(folder => IsUnder(doc.Uri, folder)))
                orphaned.Add(doc.Uri);

        return orphaned.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool IsUnder(string uri, string folder)
    {
        // Compared on the path form rather than as URIs: the folder arrives as a filesystem path and
        // escaping differences between the two would make the comparison unreliable. The trailing
        // separator matters - without it, removing '/ws/mod' would also orphan '/ws/moddb'.
        var needle = folder.Replace('\\', '/').TrimEnd('/') + "/";
        var haystack = Uri.UnescapeDataString(uri).Replace('\\', '/');
        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
