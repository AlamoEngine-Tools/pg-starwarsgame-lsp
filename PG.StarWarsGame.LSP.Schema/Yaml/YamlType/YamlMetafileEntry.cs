// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml.YamlType;

internal sealed class YamlMetafileEntry
{
    public string Path { get; set; } = string.Empty;
    public string MetaFileType { get; set; } = string.Empty;
    public List<string> Types { get; set; } = [];
    public Dictionary<string, string> Description { get; set; } = [];
}