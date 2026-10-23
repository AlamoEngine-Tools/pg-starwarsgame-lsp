// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Encyclopedia;

/// <summary>
///     Request for the resolved text of a GameObject's in-game encyclopedia popup, backing the
///     preview panel in the editor client.
/// </summary>
[Method("aet/getEncyclopediaEntry", Direction.ClientToServer)]
public sealed record GetEncyclopediaEntryParams : IRequest<GetEncyclopediaEntryResult>
{
    /// <summary>Id (Name) of the object whose popup to resolve.</summary>
    public string ObjectId { get; init; } = string.Empty;

    /// <summary>
    ///     Whether to preview the multiplayer popup. Only then can <c>MP_Encyclopedia_Text</c>
    ///     take over the body, and even then only when it is non-empty.
    /// </summary>
    public bool Multiplayer { get; init; }

    /// <summary>
    ///     The screen the card is drawn for; absent, 1920x1080. The game sizes these glyphs from the
    ///     display it runs on: the height sets the glyph size, the width converts it into the card's
    ///     units, and only the exact pair 800x600 also narrows the line budget.
    /// </summary>
    public int? ScreenWidth { get; init; }

    /// <inheritdoc cref="ScreenWidth" />
    public int? ScreenHeight { get; init; }
}