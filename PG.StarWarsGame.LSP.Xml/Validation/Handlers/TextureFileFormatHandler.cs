// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Errors on a texture reference whose extension is none of the formats the game's texture lookup
///     records - DDS, TGA and PNG (<see cref="GameTextureName.Extensions" />, #420).
/// </summary>
public sealed class TextureFileFormatHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.TextureFileFormat;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != ReferenceKind.TextureFile)
            return [];

        var value = fact.RawValue;
        if (!GameTextureName.IsTextureFile(value))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("TextureFileFormat_IsNotAValidTexture", value, fact.Tag.Tag))
            ];

        return [];
    }
}