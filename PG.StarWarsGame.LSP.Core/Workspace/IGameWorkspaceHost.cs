// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

public interface IGameWorkspaceHost
{
    IEnumerable<TrackedDocument> All { get; }
    void AddOrUpdate(string uri, string text, int version);
    void Remove(string uri);
    bool TryGet(string uri, out TrackedDocument doc);
}