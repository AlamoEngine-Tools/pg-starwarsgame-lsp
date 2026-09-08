// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Assets;

namespace PG.StarWarsGame.LSP.Server.Icons;

/// <summary>
///     Answers "does a mega texture hold this art?" for XML diagnostics, by asking the very catalog
///     the encyclopedia preview draws from.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately a thin adapter over <see cref="IconCatalog.Resolve" /> rather than a second
///         index of its own. The catalog already owns the precedence - a workspace mega texture
///         REPLACES the baked baseline wholesale, raw sources come next, and the baseline answers
///         only when the workspace ships no mega texture at all - and a diagnostic that disagreed
///         with the preview about whether art exists would be the same class of bug this fixes.
///     </para>
///     <para>
///         Blocking, and building the catalog when nothing has warmed it yet. That is affordable
///         only because of where it is called from:
///         <c>AssetFileExistenceHandlerBase</c> consults it after the asset file lookup has already
///         failed, so a document whose textures all resolve never reaches it, and the catalog is
///         cached per project root thereafter. The alternative - answering "unknown" until some
///         other feature warms the catalog - would make the warning appear and then disappear on its
///         own, which is worse than a one-off pause.
///     </para>
/// </remarks>
public sealed class IconCatalogNameIndex(
    IWorkspaceIconCatalog catalog,
    ILogger<IconCatalogNameIndex> logger) : IIconNameIndex
{
    /// <inheritdoc />
    public bool Contains(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        try
        {
            return catalog.GetAsync(CancellationToken.None).GetAwaiter().GetResult()
                ?.Resolve(reference) is not null;
        }
        catch (Exception ex)
        {
            // Never let an unreadable atlas turn into a wrong diagnostic. Answering "no" leaves the
            // file lookup's own verdict standing, which is exactly the behaviour before this existed.
            logger.LogDebug(ex,
                "Could not consult the icon catalog for '{Reference}'; leaving the asset file " +
                "lookup to decide.", reference);
            return false;
        }
    }
}
