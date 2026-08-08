// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.Commons.Hashing;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

using System.IO.Abstractions;

using System.IO.Abstractions.TestingHelpers;

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
    private static ICrc32HashingService Hashing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        services.SupportLocalisationBaseline();
        return services.BuildServiceProvider().GetRequiredService<ICrc32HashingService>();
    }

    private static IReadOnlyList<LocTranslationProblemDto> Inspect(params string[] keys)
    {
        return TranslationKeyInspector.Inspect(keys, Hashing());
    }

    /// <summary>
    ///     A key outside ASCII cannot be written at all: a compiled <c>.dat</c> stores keys as ASCII
    ///     bytes, and the writer refuses one that is not.
    ///     <para>
    ///         Reported here so it appears while the file is being edited. It used to surface only
    ///         when Save reached the writer, as <c>Value contains non-ASCII characters (Parameter
    ///         'value')</c> - which names the wrong field, since it is the KEY being rejected, and
    ///         arrives once per language file after every edit has been made.
    ///     </para>
    /// </summary>
    [Fact]
    public void AKeyOutsideAscii_IsReportedRatherThanLeftToTheWriter()
    {
        var problem = Assert.Single(Inspect("TEXT_CAFÉ"));

        Assert.Equal(LocProblemSeverity.Error, problem.Severity);
        Assert.Equal("TEXT_CAFÉ", problem.Key);
        Assert.Equal(0, problem.Index);
    }

    /// <summary>The offending characters are named: "somewhere in this key" is not actionable.</summary>
    [Fact]
    public void AKeyOutsideAscii_NamesWhatIsWrongWithIt()
    {
        var problem = Assert.Single(Inspect("TEXT_ÜBER_ÄRGER"));

        Assert.Contains("Ü", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Ä", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Values carry the translation and are expected to hold accents - only the key is limited.
    /// </summary>
    [Fact]
    public void AnAsciiKey_IsFineHoweverAccentedItsTranslationIs()
    {
        Assert.Empty(Inspect("TEXT_CAFE"));
    }

    /// <summary>
    ///     Two keys differing only outside ASCII fold to the same checksum and so collide for real -
    ///     which is what this input used to be reported as, since it is the easiest way to force a
    ///     collision between two keys that look nothing alike.
    ///     <para>
    ///         It is now reported per key instead, because that is the more useful answer: BOTH are
    ///         unwritable on their own, so renaming one to resolve a "collision" would leave the
    ///         file still refusing to save on the other.
    ///     </para>
    /// </summary>
    [Fact]
    public void TwoKeysOutsideAscii_AreEachReportedOnTheirOwnTerms()
    {
        var problems = Inspect("TEXT_Ä", "TEXT_Ö");

        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Equal(LocProblemSeverity.Error, p.Severity));
    }

    [Fact]
    public void AWellFormedFile_HasNoProblems()
    {
        Assert.Empty(Inspect("TEXT_A", "TEXT_B"));
    }

    [Fact]
    public void ABlankKey_IsAnError()
    {
        var problem = Assert.Single(Inspect("TEXT_A", "  "));

        Assert.Equal(LocProblemSeverity.Error, problem.Severity);
        Assert.Contains("no key", problem.Message);
    }

    [Fact]
    public void ADuplicateKey_IsAnErrorNamingTheFirstOccurrence()
    {
        var problem = Assert.Single(Inspect("TEXT_A", "TEXT_B", "TEXT_A"));

        Assert.Equal("TEXT_A", problem.Key);
        Assert.Contains("row 1", problem.Message);
    }

    /// <summary>
    ///     Two keys differing only in case are two distinct entries, not one.
    ///     <para>
    ///         The engine addresses an entry by the CRC32 of its key bytes
    ///         (<c>DatBinaryConverter</c>, ASCII), and that hash is case-sensitive - so both are read,
    ///         and calling it an error was simply wrong. It stays a warning because relying on case
    ///         alone to tell two keys apart is a trap for the next person to read the file.
    ///     </para>
    /// </summary>
    [Fact]
    public void KeysDifferingOnlyInCase_AreAWarningNotAnError()
    {
        var problem = Assert.Single(Inspect("TEXT_A", "text_a"));

        Assert.Equal(LocProblemSeverity.Warning, problem.Severity);
        Assert.Contains("case", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     An exact duplicate is still an error: identical keys hash identically, so the engine
    ///     genuinely cannot tell them apart and only one is ever read.
    /// </summary>
    [Fact]
    public void AnExactDuplicate_IsStillAnError()
    {
        var problem = Assert.Single(Inspect("TEXT_A", "TEXT_A"));

        Assert.Equal(LocProblemSeverity.Error, problem.Severity);
    }

    /// <summary>A case-only difference does not stop the real duplicate being reported.</summary>
    [Fact]
    public void ACaseWarningAndARealDuplicate_AreBothReported()
    {
        var problems = Inspect("TEXT_A", "text_a", "TEXT_A");

        Assert.Contains(problems, p => p.Severity == LocProblemSeverity.Warning);
        Assert.Contains(problems, p => p.Severity == LocProblemSeverity.Error);
    }

    [Fact]
    public void EveryDuplicateAfterTheFirst_IsReported()
    {
        Assert.Equal(2, Inspect("DUP", "DUP", "DUP").Count);
    }

    [Fact]
    public void AnEmptyFile_HasNoProblems()
    {
        Assert.Empty(Inspect());
    }
}
