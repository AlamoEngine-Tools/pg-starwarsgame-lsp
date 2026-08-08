// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Localisation.Rows;

using Microsoft.Extensions.DependencyInjection;

using PG.Commons.Hashing;

using PG.StarWarsGame.Localisation.Baseline;

using System.IO.Abstractions;

using System.IO.Abstractions.TestingHelpers;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     Turning key-addressed translation edits into the positional commands the document editor
///     already understands.
///     <para>
///         This is the whole of the translation editor's addressing model. It exists so the
///         fidelity-critical document layer stays positional and untouched - a file is a sequence of
///         lines - while nothing above it has to know a row's position.
///     </para>
/// </summary>
public sealed class KeyedCommandTranslatorTest
{
    private static ICrc32HashingService Hashing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new MockFileSystem());
        services.SupportLocalisationBaseline();
        return services.BuildServiceProvider().GetRequiredService<ICrc32HashingService>();
    }

    private static LocDocument Document(params string[] keys)
    {
        var rows = keys.Select((key, index) => new LocRowDto(
            index, key, [new LocValueDto("ENGLISH", $"value {index}")])).ToList();
        return new LocDocument(rows, ["ENGLISH", "GERMAN"]);
    }

    private static LocKeyedCommandDto SetValue(string key, string value, string language = "ENGLISH")
    {
        return new LocKeyedCommandDto("setValue", key, Language: language, Value: value);
    }

    private static IReadOnlyList<LocEditCommandDto> Translate(
        LocDocument document, params LocKeyedCommandDto[] commands)
    {
        var result = KeyedCommandTranslator.Translate(document, commands, Hashing());
        Assert.True(result.Success, result.Error);
        return result.Commands!;
    }

    // ── resolution ───────────────────────────────────────────────────────────

    [Fact]
    public void EmptyBatch_TranslatesToNothing()
    {
        Assert.Empty(Translate(Document("TEXT_A", "TEXT_B")));
    }

    [Fact]
    public void SetValue_ResolvesToTheRowHoldingTheKey()
    {
        var commands = Translate(Document("TEXT_A", "TEXT_B", "TEXT_C"), SetValue("TEXT_B", "new"));

        var command = Assert.Single(commands);
        Assert.Equal("setCell", command.Kind);
        Assert.Equal(1, command.Index);
        Assert.Equal("ENGLISH", command.Language);
        Assert.Equal("new", command.Value);
    }

    [Fact]
    public void SetValue_OnAKeyTheFileDoesNotHave_FailsNamingIt()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A"), [SetValue("TEXT_MISSING", "x")], Hashing());

        Assert.False(result.Success);
        Assert.Equal(0, result.FailedIndex);
        Assert.Contains("TEXT_MISSING", result.Error);
    }

    /// <summary>
    ///     A duplicate key is invalid but loadable - the validator reports it rather than refusing
    ///     the file - so addressing one must not silently pick the first match and edit a row the
    ///     user was not looking at.
    /// </summary>
    [Fact]
    public void SetValue_OnADuplicateKey_FailsNamingBothRows()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A", "TEXT_DUP", "TEXT_B", "TEXT_DUP"), [SetValue("TEXT_DUP", "x")], Hashing());

        Assert.False(result.Success);
        Assert.Contains("TEXT_DUP", result.Error);
        Assert.Contains("2", result.Error);
        Assert.Contains("4", result.Error);
    }

    // ── commands that move rows ──────────────────────────────────────────────

    [Fact]
    public void AddEntry_AppendsAtTheEnd()
    {
        var commands = Translate(Document("TEXT_A", "TEXT_B"), new LocKeyedCommandDto(
            "addEntry", "TEXT_NEW", Values: [new LocValueDto("ENGLISH", "hello")]));

        var command = Assert.Single(commands);
        Assert.Equal("insertRow", command.Kind);
        Assert.Equal(2, command.Index);
        Assert.Equal("TEXT_NEW", command.Key);
    }

    [Fact]
    public void AddEntry_ThenSetValue_ResolvesToTheAppendedRow()
    {
        var commands = Translate(
            Document("TEXT_A", "TEXT_B"),
            new LocKeyedCommandDto("addEntry", "TEXT_NEW", Values: []),
            SetValue("TEXT_NEW", "filled in"));

        Assert.Equal(2, commands[1].Index);
        Assert.Equal("filled in", commands[1].Value);
    }

    [Fact]
    public void AddEntry_WithAKeyTheFileAlreadyHas_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A"), [new LocKeyedCommandDto("addEntry", "TEXT_A", Values: [])], Hashing());

        Assert.False(result.Success);
        Assert.Contains("TEXT_A", result.Error);
    }

    /// <summary>
    ///     A differently-cased key is a different entry, so adding one is allowed.
    ///     <para>
    ///         This used to be refused outright, on the belief that the game reads one row per key
    ///         however it is cased. It does not: an entry is addressed by the CRC32 of its key bytes
    ///         and that hash is case-sensitive, so both rows are read. Blocking the edit stopped
    ///         something the engine handles perfectly well. It is still worth flagging, which
    ///         <see cref="TranslationKeyInspector" /> now does as a warning.
    ///     </para>
    /// </summary>
    [Fact]
    public void AddEntry_DifferingOnlyInCase_IsAllowed()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A"), [new LocKeyedCommandDto("addEntry", "text_a", Values: [])], Hashing());

        Assert.True(result.Success, result.Error);
        Assert.Equal(["TEXT_A", "text_a"], result.ResultingKeys);
    }

    /// <summary>
    ///     A key whose checksum matches one already present is refused: the engine cannot tell the
    ///     two apart, so the second row would never be read. Keys are hashed as ASCII, which folds
    ///     every non-ASCII character to '?', so these two collide despite looking nothing alike.
    /// </summary>
    [Fact]
    public void AddEntry_CollidingOnChecksum_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_Ä"), [new LocKeyedCommandDto("addEntry", "TEXT_Ö", Values: [])],
            Hashing());

        Assert.False(result.Success);
    }

    /// <summary>
    ///     A blank key is a row the game can never look up. Credits files use them as spacers; a
    ///     lookup table has no such concept, so it is refused rather than written and flagged later.
    /// </summary>
    [Fact]
    public void AddEntry_WithABlankKey_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A"), [new LocKeyedCommandDto("addEntry", "   ", Values: [])], Hashing());

        Assert.False(result.Success);
    }

    [Fact]
    public void RenameKey_ToABlankKey_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A"), [new LocKeyedCommandDto("renameKey", "TEXT_A", NewKey: "")], Hashing());

        Assert.False(result.Success);
    }

    /// <summary>
    ///     Changing only the casing of a key is a rename onto itself, which must not be mistaken for
    ///     a clash with the row being renamed.
    /// </summary>
    [Fact]
    public void RenameKey_ChangingOnlyCase_IsAllowed()
    {
        var commands = Translate(
            Document("text_a", "TEXT_B"),
            new LocKeyedCommandDto("renameKey", "text_a", NewKey: "TEXT_A"));

        Assert.Equal("TEXT_A", Assert.Single(commands).Key);
    }

    [Fact]
    public void DeleteEntry_ResolvesToItsRow()
    {
        var commands = Translate(
            Document("TEXT_A", "TEXT_B"), new LocKeyedCommandDto("deleteEntry", "TEXT_A"));

        var command = Assert.Single(commands);
        Assert.Equal("deleteRow", command.Kind);
        Assert.Equal(0, command.Index);
    }

    /// <summary>
    ///     Positions shift as a batch composes, exactly as they do for the positional batch, so a
    ///     later command has to resolve against the document as of everything before it.
    /// </summary>
    [Fact]
    public void DeleteEntry_ThenSetValueOnALaterKey_ResolvesToTheShiftedPosition()
    {
        var commands = Translate(
            Document("TEXT_A", "TEXT_B", "TEXT_C"),
            new LocKeyedCommandDto("deleteEntry", "TEXT_A"),
            SetValue("TEXT_C", "x"));

        Assert.Equal(1, commands[1].Index);
    }

    [Fact]
    public void DeleteEntry_ThenAddEntry_AppendsAtTheReducedCount()
    {
        var commands = Translate(
            Document("TEXT_A", "TEXT_B"),
            new LocKeyedCommandDto("deleteEntry", "TEXT_A"),
            new LocKeyedCommandDto("addEntry", "TEXT_NEW", Values: []));

        Assert.Equal(1, commands[1].Index);
    }

    [Fact]
    public void DeleteEntry_ThenSetValueOnTheDeletedKey_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A", "TEXT_B"),
            [new LocKeyedCommandDto("deleteEntry", "TEXT_A"), SetValue("TEXT_A", "x")], Hashing());

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedIndex);
    }

    // ── rename ───────────────────────────────────────────────────────────────

    [Fact]
    public void RenameKey_ResolvesToItsRow()
    {
        var commands = Translate(
            Document("TEXT_A", "TEXT_B"),
            new LocKeyedCommandDto("renameKey", "TEXT_B", NewKey: "TEXT_RENAMED"));

        var command = Assert.Single(commands);
        Assert.Equal("setKey", command.Kind);
        Assert.Equal(1, command.Index);
        Assert.Equal("TEXT_RENAMED", command.Key);
    }

    [Fact]
    public void RenameKey_ThenSetValueUnderTheNewName_Works()
    {
        var commands = Translate(
            Document("TEXT_A", "TEXT_B"),
            new LocKeyedCommandDto("renameKey", "TEXT_B", NewKey: "TEXT_RENAMED"),
            SetValue("TEXT_RENAMED", "x"));

        Assert.Equal(1, commands[1].Index);
    }

    [Fact]
    public void RenameKey_ThenSetValueUnderTheOldName_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A", "TEXT_B"),
            [
                new LocKeyedCommandDto("renameKey", "TEXT_B", NewKey: "TEXT_RENAMED"),
                SetValue("TEXT_B", "x")
            ], Hashing());

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedIndex);
    }

    [Fact]
    public void RenameKey_OntoAKeyThatAlreadyExists_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A", "TEXT_B"),
            [new LocKeyedCommandDto("renameKey", "TEXT_B", NewKey: "TEXT_A")], Hashing());

        Assert.False(result.Success);
        Assert.Contains("TEXT_A", result.Error);
    }

    // ── pass-through and rejection ───────────────────────────────────────────

    [Fact]
    public void AddLanguage_PassesThroughUnchanged()
    {
        var commands = Translate(
            Document("TEXT_A"), new LocKeyedCommandDto("addLanguage", Language: "FRENCH"));

        var command = Assert.Single(commands);
        Assert.Equal("addLanguage", command.Kind);
        Assert.Equal("FRENCH", command.Language);
    }

    [Fact]
    public void UnknownKind_Fails()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A"), [new LocKeyedCommandDto("moveRow", "TEXT_A")], Hashing());

        Assert.False(result.Success);
        Assert.Contains("moveRow", result.Error);
    }

    /// <summary>
    ///     Nothing positional may reach this vocabulary: a translation file has no row order the
    ///     user can see, so a command that implies one is a bug in the caller.
    /// </summary>
    [Fact]
    public void FailedBatch_ProducesNoCommandsAtAll()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A"), [SetValue("TEXT_A", "fine"), SetValue("TEXT_MISSING", "bad")], Hashing());

        Assert.False(result.Success);
        Assert.Null(result.Commands);
    }

    /// <summary>
    ///     The keys the file will hold once the batch lands. The validator inspects these rather
    ///     than composing the whole file and re-parsing it, which also means a compiled
    ///     <c>.dat</c> can be validated - it has no text to compose.
    /// </summary>
    [Fact]
    public void ResultingKeys_ReflectTheWholeBatch()
    {
        var result = KeyedCommandTranslator.Translate(
            Document("TEXT_A", "TEXT_B"),
            [
                new LocKeyedCommandDto("deleteEntry", "TEXT_A"),
                new LocKeyedCommandDto("addEntry", "TEXT_NEW", Values: []),
                new LocKeyedCommandDto("renameKey", "TEXT_B", NewKey: "TEXT_B2")
            ], Hashing());

        Assert.True(result.Success, result.Error);
        Assert.Equal(["TEXT_B2", "TEXT_NEW"], result.ResultingKeys);
    }
}
