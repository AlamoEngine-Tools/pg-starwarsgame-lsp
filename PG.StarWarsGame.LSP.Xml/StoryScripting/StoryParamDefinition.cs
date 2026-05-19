// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.StoryScripting;

public sealed record StoryParamDefinition(
    int Position,
    StoryParamKind Kind,
    bool Required,
    string? EnumName = null,
    string? ReferenceType = null,
    string? Description = null);
