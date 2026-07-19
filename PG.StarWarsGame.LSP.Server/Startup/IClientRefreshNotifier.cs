// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Pushes the workspace-wide refresh requests that tell the client to re-request state it has
///     already cached. Inlay hints and code lenses are pull-based: the client asks once per document
///     and renders the answer until something invalidates it. A document edit does that implicitly,
///     but server-side data changes (the initial index landing, a localisation reload) do not - so
///     without an explicit refresh the client keeps showing the stale set.
/// </summary>
public interface IClientRefreshNotifier
{
    /// <summary>
    ///     Asks the client to re-request inlay hints and code lenses for all open documents.
    ///     Failures are swallowed and logged: a missing refresh degrades rendering, it must never
    ///     take down the operation that triggered it.
    /// </summary>
    void RefreshDerivedState();
}
