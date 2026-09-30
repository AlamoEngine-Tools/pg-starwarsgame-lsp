// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     An XML file that sits in the workspace and that no registry names.
///     <para>
///         Measured in the engine: a file is read either because a registry lists it, because the
///         engine opens it by a name compiled in, or because it sits in a directory the engine
///         walks. A file that is none of those is never opened, so everything in it - every object,
///         every override - does nothing, and the author has no way to see that from the file.
///     </para>
/// </summary>
public sealed class UnregisteredXmlFileHandlerTest
{
    private static readonly UnregisteredXmlFileHandler Sut = new();

    private static readonly DiagnosticsContext Ctx =
        new(new EmptySchemaProvider(), GameIndex.Empty, "file:///data/xml/Orphan.xml", "en");

    private static XmlUnregisteredFileFact Fact(string fileName = "Orphan.xml")
    {
        return new XmlUnregisteredFileFact("file:///data/xml/Orphan.xml", 0, 1, 9, fileName);
    }

    /// <summary>
    ///     A warning, not an error: the file is well-formed and everything in it is valid. What is
    ///     wrong is that nothing reaches it, and that is a fact about the project rather than about
    ///     the file.
    /// </summary>
    [Fact]
    public void ItIsAWarning()
    {
        var result = Assert.Single(Sut.Handle(Fact(), Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public void ItNamesTheFileAndSaysTheGameNeverReadsIt()
    {
        var result = Assert.Single(Sut.Handle(Fact("Orphan.xml"), Ctx));

        Assert.Contains("Orphan.xml", result.Message, StringComparison.Ordinal);
        Assert.Contains("never reads it", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Its own id, so a mod that keeps a disabled file on purpose can silence this one file with
    ///     <c>aetswg:suppress-file</c> without silencing anything else.
    /// </summary>
    [Fact]
    public void ItCarriesItsOwnId()
    {
        Assert.Equal(DiagnosticIds.UnregisteredXmlFile, Sut.DefaultId);
    }

    /// <summary>
    ///     Nothing is struck through. Deprecation says "this still works, stop using it"; this says
    ///     the opposite - the file is fine and is simply not wired up.
    /// </summary>
    [Fact]
    public void ItCarriesNoEditorTag()
    {
        var result = Assert.Single(Sut.Handle(Fact(), Ctx));

        Assert.Null(result.Tags);
    }
}
