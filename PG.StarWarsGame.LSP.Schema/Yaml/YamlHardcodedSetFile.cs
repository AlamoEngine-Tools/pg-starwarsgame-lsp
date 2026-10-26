// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;

namespace PG.StarWarsGame.LSP.Schema.Yaml;

[PublicAPI]
internal sealed class YamlHardcodedSetFile
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Description { get; set; } = [];
    public List<YamlNote> Notes { get; set; } = [];
    public List<YamlHardcodedSetEntry> Values { get; set; } = [];
}