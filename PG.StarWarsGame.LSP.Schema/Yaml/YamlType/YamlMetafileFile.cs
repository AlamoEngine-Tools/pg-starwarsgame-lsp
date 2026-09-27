// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml.YamlType;

internal sealed class YamlMetafileFile
{
    public List<YamlMetafileEntry> Metafiles { get; set; } = [];

    /// <summary>
    ///     Directories the engine walks. A separate key rather than a metaFileType, so that a schema
    ///     carrying them still loads on a server that predates them: the deserialiser ignores a key
    ///     it does not know, where an unknown metaFileType now refuses the file outright.
    /// </summary>
    public List<YamlScannedDirectoryEntry> ScannedDirectories { get; set; } = [];
}