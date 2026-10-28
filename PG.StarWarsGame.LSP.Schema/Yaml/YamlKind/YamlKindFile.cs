// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;

namespace PG.StarWarsGame.LSP.Schema.Yaml.YamlKind;

[PublicAPI]
internal sealed class YamlKindFile
{
    public List<YamlKindEntry> Kinds { get; set; } = [];
}