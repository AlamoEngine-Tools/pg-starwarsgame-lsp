// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Changes which localisation format the root project loads, without converting any file (#121).
/// </summary>
/// <param name="Format">
///     <c>CSV</c>, <c>XML</c>, <c>NLS</c> or <c>DAT</c>. DAT is allowed here although it is not a
///     conversion target: switching a project to load the DAT files it already has is exactly what #121's
///     reporter had to do by hand in the <c>.pgproj</c>.
/// </param>
[Method("aet/setLocalisationProjectFormat", Direction.ClientToServer)]
public sealed record SetLocalisationProjectFormatParams(string Format)
    : IRequest<SetLocalisationProjectFormatResult>;

/// <param name="Changed">False when the project already loaded that format; nothing was written.</param>
/// <param name="PreviousFormat">The format the project declared before, as the <c>.pgproj</c> wrote it.</param>
/// <param name="Format">The format now declared, upper-cased as the writer stores it.</param>
/// <param name="FilesInFormat">
///     How many registered localisation files are in the new format after the reload. Zero means the
///     project now loads nothing, which the caller must say while it is one step from being undone.
/// </param>
public sealed record SetLocalisationProjectFormatResult(
    bool Changed = false,
    string? PreviousFormat = null,
    string? Format = null,
    int FilesInFormat = 0,
    string? Error = null);