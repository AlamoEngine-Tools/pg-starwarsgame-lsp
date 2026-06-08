// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Fires the one-shot client signals emitted once the startup pipeline has finished and the
///     gate is open: the <c>$/workspaceScanComplete</c> notification and a code-lens refresh so
///     reference counts requested during startup are recomputed against the now-populated index.
/// </summary>
public interface IStartupNotifier
{
    void NotifyScanComplete();
}