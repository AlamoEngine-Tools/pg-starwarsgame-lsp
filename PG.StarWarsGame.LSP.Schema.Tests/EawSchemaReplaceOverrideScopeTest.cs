// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     <c>validationOverride.mode: replace</c> drops EVERY default handler for the fact type, not
///     just the one the tag's declared <c>type:</c> would dispatch to.
/// </summary>
/// <remarks>
///     <para>
///         <c>XmlDiagnosticsHandlerRegistry.Dispatch</c> resolves <c>Replace</c> to the named
///         handlers alone - the whole <c>_byFactType</c> list is discarded. Every other concern
///         registered against <c>XmlTagValueFact</c> therefore stops running for that tag:
///         reference resolution, allowed values, type mismatch, the lot.
///     </para>
///     <para>
///         That is wider than any current user needs. All of them replace the SHAPE check for a
///         tag whose declared type does not describe it - a <c>TupleList</c> that is really one
///         pair, a <c>PerFactionObjectList</c> with its own grammar - and none of them wants to
///         switch off reference checking. It costs nothing today because no tag using
///         <c>replace</c> carries a <c>referenceKind</c> or <c>allowedValues</c>, so there is
///         nothing for the wider scope to drop.
///     </para>
///     <para>
///         This test is that assumption, written down. It does not fix the mechanism; it makes the
///         day someone relies on it a red build rather than a silently unchecked reference. If it
///         fails, the choice is to scope the replacement (the named handler declaring what it
///         supersedes) or to take the tag off <c>replace</c> - not to delete the assertion.
///     </para>
/// </remarks>
public sealed class EawSchemaReplaceOverrideScopeTest
{
    /// <summary>
    ///     The four users as of this writing, pinned by name so the next one is a deliberate
    ///     addition rather than something that arrives unnoticed.
    /// </summary>
    [Fact]
    public void Replace_is_used_by_exactly_the_tags_we_have_checked()
    {
        Assert.Equal(
            [
                "Land_Terrain_Model_Mapping",
                "Music_Event_List_Ambient",
                "Music_Event_List_Battle",
                "Presence_Induced_Animations",
            ],
            ReplaceTags().Select(t => t.Tag.Tag).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    ///     A reference on a <c>replace</c> tag would never be resolved - no unresolved-reference
    ///     diagnostic, no go-to-definition warning, nothing.
    /// </summary>
    [Fact]
    public void No_replace_tag_carries_a_reference_kind()
    {
        var offenders = ReplaceTags()
            .Where(t => t.Tag.ReferenceKind != ReferenceKind.None)
            .Select(t => $"{t.File}:{t.Tag.Tag} ({t.Tag.ReferenceKind})")
            .ToList();

        Assert.True(offenders.Count == 0,
            "mode: replace discards every default handler, so these references stop being checked: "
            + string.Join(", ", offenders));
    }

    /// <summary>The same hole, for the allowed-values narrowing.</summary>
    [Fact]
    public void No_replace_tag_carries_allowed_values()
    {
        var offenders = ReplaceTags()
            .Where(t => t.Tag.AllowedValues.Count > 0)
            .Select(t => $"{t.File}:{t.Tag.Tag}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "mode: replace discards every default handler, so these narrowings stop being enforced: "
            + string.Join(", ", offenders));
    }

    private static IReadOnlyList<(string File, RawTagDefinition Tag)> ReplaceTags()
    {
        var found = new List<(string, RawTagDefinition)>();

        foreach (var path in Directory.EnumerateFiles(TagsDirectory(), "*.yaml"))
        foreach (var tag in YamlSchemaParser.ParseTagFile(File.ReadAllText(path)))
            if (tag.ValidationOverride?.Mode == ValidationOverrideMode.Replace)
                found.Add((Path.GetFileName(path), tag));

        // A glob that matches nothing would pass every assertion above in silence.
        Assert.NotEmpty(found);
        return found;
    }

    private static string TagsDirectory()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaReplaceOverrideScopeTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "tags");
                if (Directory.Exists(candidate)) return candidate;
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("schema/eaw/tags not found.");
    }
}
