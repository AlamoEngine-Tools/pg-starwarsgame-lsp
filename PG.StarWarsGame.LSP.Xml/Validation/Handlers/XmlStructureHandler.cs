// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Publishes the structural findings of <see cref="XmlStructuralValidator" />, one id per
///     category. A file the game drops is an Error in the Syntax group; everything the game reads
///     is a Warning in the XML strictness group.
/// </summary>
public sealed class XmlStructureHandler : XmlDiagnosticsHandler<XmlStructureFact>
{
    private static readonly
        IReadOnlyDictionary<XmlStrictnessCategory, (DiagnosticId Id, XmlDiagnosticSeverity Severity)>
        ByCategory = new Dictionary<XmlStrictnessCategory, (DiagnosticId, XmlDiagnosticSeverity)>
        {
            [XmlStrictnessCategory.MissingDeclaration] =
                (DiagnosticIds.XmlMissingDeclaration, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.EndTagMismatch] = (DiagnosticIds.XmlEndTagMismatch, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.EndTagCaseMismatch] =
                (DiagnosticIds.XmlEndTagCaseMismatch, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.StrayEndTag] = (DiagnosticIds.XmlStrayEndTag, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.EmptyRoot] = (DiagnosticIds.XmlEmptyRoot, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.MultipleRoots] = (DiagnosticIds.XmlMultipleRoots, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.AttributeSyntax] = (DiagnosticIds.XmlAttributeSyntax, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.CommentSyntax] = (DiagnosticIds.XmlCommentSyntax, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.UnexpectedEndOfFile] =
                (DiagnosticIds.XmlUnexpectedEndOfFile, XmlDiagnosticSeverity.Error),
            [XmlStrictnessCategory.MalformedDeclaration] =
                (DiagnosticIds.XmlMalformedDeclaration, XmlDiagnosticSeverity.Warning),
            [XmlStrictnessCategory.StrayAmpersand] = (DiagnosticIds.XmlStrayAmpersand, XmlDiagnosticSeverity.Warning),
            [XmlStrictnessCategory.StrictOnly] = (DiagnosticIds.XmlStrictOnly, XmlDiagnosticSeverity.Warning),
            [XmlStrictnessCategory.CharacterDataAfterChild] =
                (DiagnosticIds.XmlCharacterDataAfterChild, XmlDiagnosticSeverity.Warning),
            [XmlStrictnessCategory.UntrimmedValueCharacter] =
                (DiagnosticIds.XmlUntrimmedValueCharacter, XmlDiagnosticSeverity.Warning),
            [XmlStrictnessCategory.CommentInsideValue] =
                (DiagnosticIds.XmlCommentInsideValue, XmlDiagnosticSeverity.Warning)
        };

    /// <summary>The category a published diagnostic code stands for; false for any other code.</summary>
    public static bool TryGetCategory(string? code, out XmlStrictnessCategory category)
    {
        foreach (var (c, (id, _)) in ByCategory)
            if (string.Equals(id.ToString(), code, StringComparison.OrdinalIgnoreCase))
            {
                category = c;
                return true;
            }

        category = default;
        return false;
    }

    /// <summary>The id a category is published under.</summary>
    public static DiagnosticId IdOf(XmlStrictnessCategory category)
    {
        return ByCategory[category].Id;
    }

    /// <inheritdoc />
    /// <remarks>Every result carries its own id; this is only the registry's fallback.</remarks>
    public override DiagnosticId? DefaultId => DiagnosticIds.XmlStrictOnly;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlStructureFact fact, DiagnosticsContext ctx)
    {
        var (id, severity) = ByCategory[fact.Category];
        return [new XmlDiagnosticResult(severity, fact.Reason, Id: id)];
    }
}