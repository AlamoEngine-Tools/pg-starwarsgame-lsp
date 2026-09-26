// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

internal sealed class YamlEnumValueEntry
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Description { get; set; } = [];
    public List<YamlNote> Notes { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<YamlParamEntry>? Params { get; set; }
}