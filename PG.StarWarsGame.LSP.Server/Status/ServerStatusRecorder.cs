// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Versioning;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Status;

/// <summary>Where a downloaded artefact - the baseline, the icon pack - actually came from.</summary>
public enum StatusAssetSource
{
    /// <summary>Not asked for yet. The icon pack loads on the first preview, not at startup.</summary>
    NotLoaded,
    Network,

    /// <summary>The download failed or was stale, and the cached copy was used.</summary>
    Cache,
    Local,

    /// <summary>Nothing usable anywhere; the empty fallback is in use.</summary>
    Empty
}

/// <summary>Where the schema came from.</summary>
public enum StatusSchemaSource
{
    NotLoaded,

    /// <summary>The newest official schema release in range, resolved at this start.</summary>
    Release,

    /// <summary>Releases could not be listed; the official release cached by an earlier start.</summary>
    CachedRelease,

    /// <summary>No official release resolvable or cached; the schema repository's default branch.</summary>
    Branch,

    CustomUrl,
    Local
}

/// <summary>How far startup indexing got.</summary>
public enum StatusIndexState
{
    Building,
    Complete,
    Failed
}

/// <summary>
///     What the server learned about itself at startup, for the bug report.
/// </summary>
/// <remarks>
///     <para>
///         Every one of these is decided once, logged, and otherwise thrown away by the code that
///         decides it - the baseline loader knows it fell back to the cache, the pipeline knows it
///         failed, and nothing keeps either. This is where they are kept. Written by the startup
///         steps, read by <c>aet/getServerStatus</c>.
///     </para>
///     <para>
///         Outcomes only, never a path or a name: the report is pasted into public issues.
///     </para>
/// </remarks>
public sealed class ServerStatusRecorder
{
    public StatusSchemaSource SchemaSource { get; private set; }

    /// <summary>The schema's own version check, or null when none ran.</summary>
    public SchemaVersionCheck? SchemaCheck { get; private set; }

    public StatusAssetSource BaselineSource { get; private set; }

    public StatusAssetSource IconPackSource { get; private set; }

    public StatusIndexState IndexState { get; private set; }

    /// <summary>How long the startup stages took, once they have finished.</summary>
    public TimeSpan? IndexDuration { get; private set; }

    /// <summary>Whether a <c>.pgproj</c> was found under the workspace roots. Null until startup asks.</summary>
    public bool? ProjectDetected { get; private set; }

    /// <summary>Why the project could not be used, or <see cref="ProjectProblem.None" />.</summary>
    public ProjectProblem ProjectProblem { get; private set; }

    /// <summary>
    ///     Whether the schema came from its local cache (true) or was rebuilt from the network
    ///     (false). Null for a local schema, or when the load never got that far.
    /// </summary>
    public bool? SchemaFromCache { get; private set; }

    public void RecordSchema(StatusSchemaSource source, SchemaVersionCheck? check, bool? fromCache = null)
    {
        SchemaSource = source;
        SchemaCheck = check;
        SchemaFromCache = fromCache;
    }

    public void RecordBaseline(StatusAssetSource source)
    {
        BaselineSource = source;
    }

    public void RecordIconPack(StatusAssetSource source)
    {
        IconPackSource = source;
    }

    public void RecordIndexFinished(bool failed, TimeSpan duration)
    {
        IndexState = failed ? StatusIndexState.Failed : StatusIndexState.Complete;
        IndexDuration = duration;
    }

    public void RecordProject(bool detected, ProjectProblem problem)
    {
        ProjectDetected = detected;
        ProjectProblem = problem;
    }
}