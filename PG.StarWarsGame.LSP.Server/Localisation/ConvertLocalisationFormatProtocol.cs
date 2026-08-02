// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Rewrites one localisation file in a different text format, leaving the original where it is.
/// </summary>
/// <param name="ProjectFilePath">The file to convert.</param>
/// <param name="TargetFormat">
///     <c>CSV</c>, <c>XML</c> or <c>NLS</c>. DAT is deliberately not a target: it is one binary file
///     per language and the engine's load format, which is what <c>aet/exportLocalisationToDat</c>
///     already produces. Two code paths writing DAT files is two places for the crawl's sort order
///     to be got wrong.
/// </param>
[Method("aet/convertLocalisationFormat", Direction.ClientToServer)]
public sealed record ConvertLocalisationFormatParams(string ProjectFilePath, string TargetFormat)
    : IRequest<ConvertLocalisationFormatResult>;

/// <param name="WrittenPath">The new file, or null if nothing was written.</param>
/// <param name="ProjectFormatChanged">
///     Whether the <c>.pgproj</c> localisation node was repointed at the new format. False when the
///     converted file was not the one the project's declared format describes - the new file is
///     written, but the project still loads the old one, and the caller has to say so.
/// </param>
/// <param name="OtherFilesInOldFormat">
///     How many other registered localisation files are still in the previous format. Nonzero after
///     a project-format change means those files are now orphaned, which the user needs to hear
///     about while it is still one undo away.
/// </param>
public sealed record ConvertLocalisationFormatResult(
    string? WrittenPath,
    bool ProjectFormatChanged = false,
    int OtherFilesInOldFormat = 0,
    string? Error = null);
