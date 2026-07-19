// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Holds the story-chain problems found by the startup scan so the XML diagnostics pipeline
///     can surface them on the documents that contain the offending references. Replaced
///     wholesale on every scan; consumers look up by canonical document URI.
/// </summary>
public interface IStoryChainProblemStore
{
    /// <summary>Replaces all stored problems with the given set (clears previous scan results).</summary>
    void Replace(IReadOnlyList<StoryChainProblem> problems);

    /// <summary>Problems anchored in the given document (canonical URI), or an empty list.</summary>
    IReadOnlyList<StoryChainProblem> GetForDocument(string canonicalUri);
}