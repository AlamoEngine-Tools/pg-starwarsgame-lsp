// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation;

[Method("aet/setLocalisationEntry", Direction.ClientToServer)]
public sealed record SetLocalisationEntryParams(
    string ProjectFilePath,
    string Key,
    IReadOnlyDictionary<string, string>? Translations,
    string? ExpectedContentHash) : IRequest<LocalisationWriteResult>;