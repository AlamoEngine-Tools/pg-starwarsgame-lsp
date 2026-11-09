// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Tags Petroglyph added after the 2018 build, which no binary in hand can confirm.
/// </summary>
/// <remarks>
///     <para>
///         The engine's own XML field table is the normal authority for what a tag is, but it was
///         read out of the May 2018 executable and these postdate it. Their evidence is the patch
///         notes, which describe both precisely enough to type:
///     </para>
///     <para>
///         <c>MoveAttackWhileStopped</c> (2023-11-20) - "defaults to false, making units fully stop
///         when the stop command is used (EaW behavior). When set to true, units will immediately
///         start to move and attack things in their idle-attack-range". A boolean, and false by
///         default.
///     </para>
///     <para>
///         <c>Space_Or_Garrison_Category</c> (2021-01-14) - "The XML tags &lt;Garrison_Category&gt;
///         and &lt;Space_Or_Garrison_Category&gt; are actually the same. You can specify either
///         one, but should only specify one." So it mirrors <c>Garrison_Category</c> exactly.
///     </para>
///     <para>
///         <c>Disregard_Space_Layer_For_Guard_Attack_Move</c> is the one tag here with a binary
///         behind it: the 2024 64-bit build parses it as a yes/no flag on a game object type and reads
///         it through a getter of the same name; the 2018 build has no such row (#194).
///     </para>
///     <para>
///         Deliberately NOT added here: <c>&lt;Text&gt;</c> on <c>CommandBarComponent</c>. The note
///         is a single clause - "adding &lt;Text&gt; option" - with nothing to say whether it takes
///         a localisation key or a literal string, and no shipped data exercises it. Guessing would
///         put a diagnostic behind the guess.
///     </para>
/// </remarks>
public sealed class EawSchemaPostPatchTagTest
{
    [Fact]
    public void MoveAttackWhileStopped_IsABoolean()
    {
        Assert.Equal(XmlValueType.Boolean, Tag("GameObjectType.yaml", "MoveAttackWhileStopped").ValueType);
    }

    [Fact]
    public void DisregardSpaceLayerForGuardAttackMove_IsABoolean()
    {
        Assert.Equal(XmlValueType.Boolean,
            Tag("GameObjectType.yaml", "Disregard_Space_Layer_For_Guard_Attack_Move").ValueType);
    }

    // Same tag by another name, so it has to carry the same typing or one spelling validates
    // differently from the other.
    [Fact]
    public void SpaceOrGarrisonCategory_MatchesGarrisonCategory()
    {
        var twin = Tag("GameObjectType.yaml", "Space_Or_Garrison_Category");
        var original = Tag("GameObjectType.yaml", "Garrison_Category");

        Assert.Equal(original.ValueType, twin.ValueType);
        Assert.Equal(original.ReferenceKind, twin.ReferenceKind);
        Assert.Equal(original.EnumName, twin.EnumName);
    }

    private static RawTagDefinition Tag(string file, string name)
    {
        var tags = YamlSchemaParser.ParseTagFile(File.ReadAllText(Find(file)));
        var tag = tags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag;
    }

    private static string Find(string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaPostPatchTagTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "tags", file);
                if (File.Exists(candidate)) return candidate;
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"schema/eaw/tags/{file} not found.");
    }
}