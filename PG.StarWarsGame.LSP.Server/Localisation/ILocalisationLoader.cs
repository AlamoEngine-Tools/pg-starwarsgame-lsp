// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public interface ILocalisationLoader
{
    Task LoadAsync(WorkspaceConfiguration workspaceConfig, CancellationToken ct);
}