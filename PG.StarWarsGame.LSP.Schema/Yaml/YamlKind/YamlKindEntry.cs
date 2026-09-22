// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml.YamlKind;

internal sealed class YamlKindEntry
{
    public string Kind { get; set; } = string.Empty;
    public List<string> Behaviors { get; set; } = [];
    public List<string> Flags { get; set; } = [];
    public List<string> MemberOf { get; set; } = [];
    public Dictionary<string, string> Description { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
}
