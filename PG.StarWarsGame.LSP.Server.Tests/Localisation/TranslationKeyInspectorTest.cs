// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     What a translation file's keys must satisfy, checked against the keys the staged batch will
///     leave behind.
///     <para>
///         Problems here are nearly always pre-existing: the translator refuses to create a blank or
///         clashing key in the first place. This is what tells the user their file already has one.
///     </para>
/// </summary>
public sealed class TranslationKeyInspectorTest
{
    [Fact]
    public void AWellFormedFile_HasNoProblems()
    {
        Assert.Empty(TranslationKeyInspector.Inspect(["TEXT_A", "TEXT_B"]));
    }

    [Fact]
    public void ABlankKey_IsAnError()
    {
        var problem = Assert.Single(TranslationKeyInspector.Inspect(["TEXT_A", "  "]));

        Assert.Equal(LocProblemSeverity.Error, problem.Severity);
        Assert.Contains("no key", problem.Message);
    }

    [Fact]
    public void ADuplicateKey_IsAnErrorNamingTheFirstOccurrence()
    {
        var problem = Assert.Single(TranslationKeyInspector.Inspect(["TEXT_A", "TEXT_B", "TEXT_A"]));

        Assert.Equal("TEXT_A", problem.Key);
        Assert.Contains("row 1", problem.Message);
    }

    /// <summary>
    ///     The game looks a key up regardless of how it is cased, so two spellings of one name are
    ///     the same entry and only one of them is ever read.
    /// </summary>
    [Fact]
    public void ADuplicateDifferingOnlyInCase_IsAnError()
    {
        Assert.Single(TranslationKeyInspector.Inspect(["TEXT_A", "text_a"]));
    }

    [Fact]
    public void EveryDuplicateAfterTheFirst_IsReported()
    {
        Assert.Equal(2, TranslationKeyInspector.Inspect(["DUP", "DUP", "DUP"]).Count);
    }

    [Fact]
    public void AnEmptyFile_HasNoProblems()
    {
        Assert.Empty(TranslationKeyInspector.Inspect([]));
    }
}
