// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

internal sealed class YamlEnumFile
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "schemaFixed";
    public bool IsBitfield { get; set; }
    public string? SourceFile { get; set; }
    public Dictionary<string, string> Description { get; set; } = [];
    public bool Deprecated { get; set; }
    public string? AvailableSince { get; set; }
    public List<YamlEnumValueEntry> Values { get; set; } = [];
}
