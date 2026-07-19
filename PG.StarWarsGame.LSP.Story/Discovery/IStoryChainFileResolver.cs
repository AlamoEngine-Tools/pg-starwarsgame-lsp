// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Story.Discovery;

/// <summary>
///     A resolved story-chain file: its content and, when it has an on-disk identity, the
///     canonical document URI of the winning copy (highest layer). <see cref="DocumentUri" /> is
///     <c>null</c> for sources without file identity (e.g. the baseline builder's game repository).
/// </summary>
public sealed record StoryChainFile(string Content, string? DocumentUri);

/// <summary>
///     Resolves xml-directory-relative paths (e.g. <c>"Story_Plots_Rebel.xml"</c>,
///     <c>"SubDir/Story_X.xml"</c>) for the <see cref="StoryChainScanner" />. Implementations
///     search every layer in rank order (workspace scan) or the game repository (baseline scan).
/// </summary>
public interface IStoryChainFileResolver
{
    /// <summary>Content of the best copy of the file, or <c>null</c> when no layer ships it.</summary>
    StoryChainFile? ReadFile(string xmlRelativePath);

    /// <summary>
    ///     Whether the file is known to the shipped baseline even though no readable copy exists —
    ///     used both to accept references into baseline-only files without a diagnostic and to
    ///     downgrade problems inside vanilla-shipped files from error to warning.
    /// </summary>
    bool IsKnownToBaseline(string xmlRelativePath);
}