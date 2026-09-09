// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Persistence;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>Per-workspace editor preferences (not game data). Extend with new fields as needed.</summary>
public sealed record WorkspaceSettings
{
    /// <summary>Skip the "delete story event?" confirmation modal (user ticked "don't ask again").</summary>
    public bool SkipStoryDeleteConfirmation { get; init; }

    /// <summary>Show the per-thread swimlane overlay in the story graph.</summary>
    public bool ShowThreadLanes { get; init; }

    /// <summary>Show the per-chapter swimlane overlay in the story graph.</summary>
    public bool ShowChapterLanes { get; init; }
}

/// <summary>
///     What <c>.aetswg/settings/workspace.settings.json</c> is, as a versioned document.
/// </summary>
/// <remarks>
///     Adding a field here is a shape change: bump <see cref="Version" />, add a migration from the
///     version before it, and re-pin <see cref="Shape" />. A new preference that simply defaults to
///     false still needs the bump - the file it lands in must be able to say which shape it holds.
/// </remarks>
public static class WorkspaceSettingsDocument
{
    public const string TypeName = "aetswg.WorkspaceSettings";

    public static readonly TypeVersion Version = TypeVersion.Of("aetswg", 1);

    public static readonly DocumentShapePin Shape = new(
        TypeName, Version, "d6f035c085149ab369b8a80f00f4a9345f87ef5729e12c53a67030374c4f269b");

    public static readonly IReadOnlyList<IDocumentMigration> Migrations = [new AdoptUnversionedFile()];

    /// <summary>
    ///     Version zero is the same three properties without an envelope, so this adopts the file
    ///     rather than rewriting it.
    ///     <para>
    ///         The plan had this file simply discarded - one free break, on the assumption that
    ///         versioning it meant restructuring it. It does not: the envelope sits beside the
    ///         properties it already had, so adopting costs nothing and the user keeps the toggles
    ///         they set.
    ///     </para>
    /// </summary>
    private sealed class AdoptUnversionedFile : IDocumentMigration
    {
        public string TypeName => WorkspaceSettingsDocument.TypeName;
        public TypeVersion From => TypeVersion.Zero("aetswg");
        public TypeVersion To => Version;
        public string? UserNotice => null;

        public JsonNode Migrate(JsonNode document)
        {
            return document;
        }
    }
}

/// <summary>Reads/writes the workspace's editor preferences.</summary>
public interface IWorkspaceSettingsStore
{
    WorkspaceSettings Get();
    void Set(WorkspaceSettings settings);
}

/// <summary>
///     JSON sidecar under the project's <c>.aetswg/settings/workspace.settings.json</c>, held by a
///     <see cref="SidecarStore{T}" /> - editor preferences that belong with the workspace, not the
///     mod's xml tree. Without a .pgproj the store degrades to in-memory (the preference survives
///     the session only). A corrupt file is treated as defaults, never fatal.
/// </summary>
public sealed class WorkspaceSettingsStore : IWorkspaceSettingsStore
{
    private readonly SidecarStore<WorkspaceSettings> _store;

    public WorkspaceSettingsStore(
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<WorkspaceSettingsStore> logger)
    {
        _store = new SidecarStore<WorkspaceSettings>(
            "settings/workspace.settings.json", WorkspaceSettingsDocument.TypeName,
            WorkspaceSettingsDocument.Version, WorkspaceSettingsDocument.Migrations,
            new AetswgSidecarLocator(reloadService), fileHelper, logger,
            () => new WorkspaceSettings());
    }

    public WorkspaceSettings Get()
    {
        return _store.Load().Value;
    }

    public void Set(WorkspaceSettings settings)
    {
        // A refused write is the newer-file case: the preference stays in memory rather than
        // overwriting settings this build cannot read.
        _store.TrySave(settings, out _);
    }
}
