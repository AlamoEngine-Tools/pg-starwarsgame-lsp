// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>YAML deserialization model for a tag's optional validationOverride block.</summary>
internal sealed class YamlTagValidationOverride
{
    public string ValidationId { get; set; } = string.Empty;
    public string? Mode { get; set; }
    public string? Order { get; set; }
}