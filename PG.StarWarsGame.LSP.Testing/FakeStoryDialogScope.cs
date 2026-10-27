// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     The story-dialog scope from an in-memory map: which dialog names resolve to which script, and
///     the chapters every script holds. Enabled with no files unless a test says otherwise, so a
///     dialog reference finds nothing and a document is never in scope.
/// </summary>
public sealed class FakeStoryDialogScope : IStoryDialogScope
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<int> Chapters { get; init; } = [];
    public bool Enabled { get; init; } = true;

    public bool IsInScope(string canonicalUri)
    {
        return Files.ContainsValue(canonicalUri);
    }

    public string? ResolveDialogFile(string dialogName)
    {
        return Files.GetValueOrDefault(dialogName);
    }

    public IReadOnlyCollection<int> GetChapters(string canonicalUri)
    {
        return Chapters;
    }
}