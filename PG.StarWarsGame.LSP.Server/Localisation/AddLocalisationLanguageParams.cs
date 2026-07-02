// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation;

[Method("aet/addLocalisationLanguage", Direction.ClientToServer)]
public sealed record AddLocalisationLanguageParams(
    string ProjectFilePath, string Language, string? ExpectedContentHash) : IRequest<LocalisationWriteResult>;
