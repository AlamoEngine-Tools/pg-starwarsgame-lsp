// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.StoryScripting;

public sealed record StoryEventDefinition(
    string EventType,
    bool SupportsFilter,
    IReadOnlyList<StoryParamDefinition> Params);
