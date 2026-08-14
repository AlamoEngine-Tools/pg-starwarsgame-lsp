// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Warns when an <c>Icon_Name</c> points at art the project has drawn but not yet packed into
///     its mega texture.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately separate from <see cref="TextureFileExistenceHandler" />, which answers a
///         different question. "This icon does not exist" tells an author to go and draw something;
///         "your mega texture is out of date" tells them to run their packer. Collapsing the two
///         would send anyone whose .mtd has fallen behind chasing a file that is already sitting in
///         their source folder.
///     </para>
///     <para>
///         Only fires when the workspace actually ships a mega texture. Without one there is nothing
///         for the raw sources to be out of sync with, so the set is empty and this stays silent.
///     </para>
/// </remarks>
public sealed class IconAwaitingRepackHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.IconAwaitingRepack;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var stale = ctx.IconsAwaitingRepack;
        if (stale is null || stale.Count == 0)
            return [];

        if (!string.Equals(fact.Tag.Tag, EncyclopediaTags.IconName, StringComparison.OrdinalIgnoreCase))
            return [];

        var value = fact.RawValue.Trim();
        if (value.Length == 0)
            return [];

        // A mega texture directory records every entry with a .TGA suffix whatever the packer was
        // fed, while raw sources are tracked by base name - so compare without the extension.
        var baseName = StripExtension(value);
        if (!stale.Contains(baseName))
            return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"Icon '{value}' exists as a source image but is missing from this project's mega " +
                "texture, so the game will not display it. Rebuild the .mtd to include it.")
        ];
    }

    private static string StripExtension(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot > 0 ? value[..dot] : value;
    }
}
