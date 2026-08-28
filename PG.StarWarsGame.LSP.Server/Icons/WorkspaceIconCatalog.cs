// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Icons;

/// <summary>The icon catalog for whichever workspace is currently loaded.</summary>
public interface IWorkspaceIconCatalog
{
    /// <summary>
    ///     The catalog, or <see langword="null" /> when icons are unavailable for any reason - no
    ///     provider, no workspace root, or a catalog that would not build.
    /// </summary>
    Task<IconCatalog?> GetAsync(CancellationToken ct);
}

/// <summary>
///     Finds the current workspace root and asks <see cref="IIconCatalogProvider" /> for its catalog.
/// </summary>
/// <remarks>
///     <para>
///         Three callers wanted this and each had written its own copy of the same six lines: the
///         encyclopedia card, the XML diagnostics' mega-texture lookup, and the model preview's
///         ability icons. The copies had already drifted - one asked DI for the CONCRETE
///         <c>ModProjectReloadService</c> while only the interface is registered, so its
///         project-configured icon path was a permanent null and it silently used the conventional
///         one.
///     </para>
///     <para>
///         Every failure answers null rather than throwing. Icons are a convenience everywhere they
///         are used, and no arrangement of missing art should take a request down with it.
///     </para>
/// </remarks>
public sealed class WorkspaceIconCatalog(
    ILspConfigurationProvider config,
    IIconCatalogProvider? icons = null,
    IModProjectReloadService? projects = null) : IWorkspaceIconCatalog
{
    /// <inheritdoc />
    public async Task<IconCatalog?> GetAsync(CancellationToken ct)
    {
        if (icons is null)
            return null;

        var root = projects?.LastWorkspaceRoots?.FirstOrDefault() ?? config.Current.WorkspaceRoot;
        if (string.IsNullOrEmpty(root))
            return null;

        try
        {
            return await icons.GetAsync(root, projects?.LastWorkspaceConfig?.Icons, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
