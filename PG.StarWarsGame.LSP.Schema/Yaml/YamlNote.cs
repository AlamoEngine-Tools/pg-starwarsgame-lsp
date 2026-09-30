// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>
///     YAML deserialization model for one note on any schema element.
///     <para>
///         <c>text</c> is nested rather than spread beside <c>kind</c> on purpose: a mapping that
///         mixed one reserved key with arbitrary locale keys would need a custom deserialiser, and
///         would make <c>kind</c> unusable as a locale code forever. Nested, it is the same locale
///         map that <c>description</c> and <c>label</c> already are.
///     </para>
/// </summary>
internal sealed class YamlNote
{
    public string Kind { get; set; } = string.Empty;
    public Dictionary<string, string> Text { get; set; } = [];

    /// <summary>What the kind carries besides prose - a version for <c>Since</c>.</summary>
    public string? Value { get; set; }
}
