// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     The registry scope of the story-dialog language: which files are story-dialog scripts
///     (the .pgproj <c>directories.storyDialog</c> node - filename conventions deliberately play
///     no part), how <c>Story_Dialog</c> names resolve to files, and which chapters a dialog file
///     defines. Implemented server-side over the resolved workspace configuration; consumed by
///     the dialog diagnostics publisher and the XML-side cross-checks.
/// </summary>
public interface IStoryDialogScope
{
    /// <summary>
    ///     <c>true</c> when the dialog language is active: the <c>features.dialog.diagnostics</c>
    ///     flag is on AND the workspace declares at least one storyDialog directory.
    /// </summary>
    bool Enabled { get; }

    /// <summary>Whether the document lies under any declared storyDialog directory.</summary>
    bool IsInScope(string canonicalUri);

    /// <summary>
    ///     Resolves an extensionless <c>Story_Dialog</c> name against the storyDialog directories
    ///     (highest layer wins). Returns the canonical URI of the winning copy, or <c>null</c>.
    /// </summary>
    string? ResolveDialogFile(string dialogName);

    /// <summary>Chapter indices defined by the dialog file at the given canonical URI.</summary>
    IReadOnlyCollection<int> GetChapters(string canonicalUri);
}