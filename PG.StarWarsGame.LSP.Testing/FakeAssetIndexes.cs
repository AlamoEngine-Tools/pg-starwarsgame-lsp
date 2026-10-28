// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>The icons drawn but not yet repacked: none unless a test names some.</summary>
public sealed class FakeIconRepackStatusProvider : IIconRepackStatusProvider
{
    public HashSet<string> Stale { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> IconsAwaitingRepack => Stale;
}

/// <summary>The textures a model names inside itself, from an in-memory map: none for an unknown model.</summary>
public sealed class FakeModelTextureIndex : IModelTextureIndex
{
    private readonly Dictionary<string, IReadOnlyList<string>> _byModel = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> TexturesOf(string modelReference)
    {
        return _byModel.GetValueOrDefault(modelReference) ?? [];
    }

    public FakeModelTextureIndex With(string modelReference, params string[] textures)
    {
        _byModel[modelReference] = textures;
        return this;
    }
}

/// <summary>The GUI art the mega textures hold: none unless a test adds names.</summary>
public sealed class FakeIconNameIndex : IIconNameIndex
{
    public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Contains(string reference)
    {
        return Names.Contains(reference);
    }
}

/// <summary>Story graph problems per document: none unless a test puts some in.</summary>
public sealed class FakeStoryGraphDiagnosticsSource : IStoryGraphDiagnosticsSource
{
    private readonly Dictionary<string, IReadOnlyList<StoryGraphDiagnostic>> _byUri =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<StoryGraphDiagnostic> GetForDocument(string canonicalUri)
    {
        return _byUri.GetValueOrDefault(canonicalUri) ?? [];
    }

    public FakeStoryGraphDiagnosticsSource With(string canonicalUri, params StoryGraphDiagnostic[] diagnostics)
    {
        _byUri[canonicalUri] = diagnostics;
        return this;
    }
}