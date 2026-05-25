// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

public interface IPreOpenBuffer
{
    void RecordOpen(string uri, string text, int version);
    IReadOnlyList<(string Uri, string Text, int Version)> DrainAndClose();
}
