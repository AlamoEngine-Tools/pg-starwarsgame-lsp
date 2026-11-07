// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;

namespace PG.StarWarsGame.LSP.Schema.Yaml.YamlType;

[PublicAPI]
internal sealed class YamlMetafileEntry
{
    public string Path { get; set; } = string.Empty;
    public string MetaFileType { get; set; } = string.Empty;

    /// <summary>firstWins, lastWins, merge or firstByName; required on a fileRegistry.</summary>
    public string? DuplicateNames { get; set; }

    public List<string> Types { get; set; } = [];
    public Dictionary<string, string> Description { get; set; } = [];
    public List<YamlNote> Notes { get; set; } = [];
}