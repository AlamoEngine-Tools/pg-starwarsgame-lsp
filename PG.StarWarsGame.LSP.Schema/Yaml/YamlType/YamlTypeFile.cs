// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;

namespace PG.StarWarsGame.LSP.Schema.Yaml.YamlType;

[PublicAPI]
internal sealed class YamlTypeFile
{
    public List<YamlTypeEntry> Types { get; set; } = [];
}