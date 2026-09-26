// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The shipped schema's own account of <c>StoryFlagCompareMethod</c>.
///     <para>
///         <c>StoryCompareMethodSmokeTest</c> asserts that an author sees an error for
///         <c>NOT_EQUAL_TO</c>; this asserts the premise that test rests on, so a failure says which
///         half broke - the note being absent from the schema, or the diagnostic not reaching the
///         wire.
///     </para>
/// </summary>
public sealed class EawSchemaCompareMethodTest
{
    private static RawEnumValueDefinition Value(string name)
    {
        var def = YamlSchemaParser.ParseEnumFile(EawSchemaRepo.Read("enums/StoryFlagCompareMethod.yaml"));
        return Assert.Single(def.Values.Where(v => v.Name == name));
    }

    // Measured in the engine, 2026-09-25: the comparison's jump table has an entry for this value
    // that goes straight to the end of the loop body, and the out-of-range default lands on the same
    // instruction. It is an empty case, not a missing one, and no default can rescue it.
    [Fact]
    public void NotEqualTo_CarriesABuggedInEngineNote()
    {
        var note = Assert.Single(Value("NOT_EQUAL_TO").Notes);

        Assert.Equal(SchemaNoteKind.BuggedInEngine, note.Kind);
        Assert.Contains("never fires", note.Text["en"], StringComparison.OrdinalIgnoreCase);
    }

    // The operators that do work carry nothing, or the diagnostic would fire on every comparison in
    // every story file and the one that matters would be lost in it.
    [Theory]
    [InlineData("EQUAL_TO")]
    [InlineData("LESS_THAN")]
    [InlineData("GREATER_THAN")]
    [InlineData("GREATER_THAN_EQUAL_TO")]
    [InlineData("LESS_THAN_EQUAL_TO")]
    public void TheWorkingOperators_CarryNoNotes(string name)
    {
        Assert.Empty(Value(name).Notes);
    }
}
