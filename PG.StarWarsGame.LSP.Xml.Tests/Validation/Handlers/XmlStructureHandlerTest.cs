// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     One id per category. A file the game drops is a Syntax error; everything the game reads is
///     an XML strictness warning, so silencing that group never hides a lost file.
/// </summary>
public sealed class XmlStructureHandlerTest
{
    private static readonly XmlStructureHandler Sut = new();

    public static TheoryData<XmlStrictnessCategory, DiagnosticId, XmlDiagnosticSeverity> Categories => new()
    {
        { XmlStrictnessCategory.MissingDeclaration, DiagnosticIds.XmlMissingDeclaration, XmlDiagnosticSeverity.Error },
        { XmlStrictnessCategory.EndTagMismatch, DiagnosticIds.XmlEndTagMismatch, XmlDiagnosticSeverity.Error },
        { XmlStrictnessCategory.EndTagCaseMismatch, DiagnosticIds.XmlEndTagCaseMismatch, XmlDiagnosticSeverity.Error },
        { XmlStrictnessCategory.StrayEndTag, DiagnosticIds.XmlStrayEndTag, XmlDiagnosticSeverity.Error },
        { XmlStrictnessCategory.EmptyRoot, DiagnosticIds.XmlEmptyRoot, XmlDiagnosticSeverity.Error },
        { XmlStrictnessCategory.MultipleRoots, DiagnosticIds.XmlMultipleRoots, XmlDiagnosticSeverity.Error },
        { XmlStrictnessCategory.AttributeSyntax, DiagnosticIds.XmlAttributeSyntax, XmlDiagnosticSeverity.Error },
        { XmlStrictnessCategory.CommentSyntax, DiagnosticIds.XmlCommentSyntax, XmlDiagnosticSeverity.Error },
        {
            XmlStrictnessCategory.UnexpectedEndOfFile, DiagnosticIds.XmlUnexpectedEndOfFile, XmlDiagnosticSeverity.Error
        },
        {
            XmlStrictnessCategory.MalformedDeclaration, DiagnosticIds.XmlMalformedDeclaration,
            XmlDiagnosticSeverity.Warning
        },
        { XmlStrictnessCategory.StrayAmpersand, DiagnosticIds.XmlStrayAmpersand, XmlDiagnosticSeverity.Warning },
        { XmlStrictnessCategory.StrictOnly, DiagnosticIds.XmlStrictOnly, XmlDiagnosticSeverity.Warning },
        {
            XmlStrictnessCategory.CharacterDataAfterChild, DiagnosticIds.XmlCharacterDataAfterChild,
            XmlDiagnosticSeverity.Warning
        },
        {
            XmlStrictnessCategory.UntrimmedValueCharacter, DiagnosticIds.XmlUntrimmedValueCharacter,
            XmlDiagnosticSeverity.Warning
        },
        { XmlStrictnessCategory.CommentInsideValue, DiagnosticIds.XmlCommentInsideValue, XmlDiagnosticSeverity.Warning }
    };

    [Theory]
    [MemberData(nameof(Categories))]
    public void EachCategory_HasItsOwnIdAndSeverity(XmlStrictnessCategory category, DiagnosticId id,
        XmlDiagnosticSeverity severity)
    {
        var fact = new XmlStructureFact("file:///test.xml", 3, 2, 5, "reason", category);
        var d = Assert.Single(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
        Assert.Equal(id, d.Id);
        Assert.Equal(severity, d.Severity);
        Assert.Equal("reason", d.Message);
    }

    [Fact]
    public void EveryCategory_IsMapped()
    {
        var mapped = Categories.Select(row => row.Data.Item1).ToHashSet();
        Assert.Equal(Enum.GetValues<XmlStrictnessCategory>().ToHashSet(), mapped);
    }

    [Fact]
    public void FileLevelErrors_AreInTheSyntaxGroup_TheRestInTheStrictnessGroup()
    {
        foreach (var row in Categories)
        {
            var id = row.Data.Item2;
            var severity = row.Data.Item3;
            Assert.Equal(
                (int)(severity == XmlDiagnosticSeverity.Error ? DiagnosticGroup.Syntax : DiagnosticGroup.XmlStrictness),
                id.Group);
        }
    }
}