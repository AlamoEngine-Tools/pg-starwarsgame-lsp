// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Every tuple-list tag needs <c>control-point-curve</c>, because the engine enforces the
///     minimum at the PARSER rather than per ability.
/// </summary>
/// <remarks>
///     <para>
///         The rule was adopted from three <c>Cost_Mod_By_*</c> messages in
///         <c>NeutralizeHeroAbilityClass</c> and three curves in <c>SlicerAbilityClass</c>, so it
///         was opted in on those six tags. That is where it came from, not where it applies.
///     </para>
///     <para>
///         <c>DatabaseMapClass::Map_Data_Of_Type</c> asserts <c>counter >= 2</c> in the case for
///         BOTH tuple-list type codes - <c>DatabaseMap.cpp:4990</c> for <c>0x2f FloatTupleList</c>
///         and <c>:5030</c> for <c>0x30 IntFloatTupleList</c> - and on failure jumps straight to the
///         function exit. So it binds every tag of either type, whatever ability declares it, and
///         the six opt-ins were a sixth of the real reach.
///     </para>
///     <para>
///         This test is the durable half of the fix. The opt-in stays per tag, matching how every
///         other range rule is bound, but a tuple-list tag added without it is now a red build
///         rather than a silent gap.
///     </para>
///     <para>
///         Measured before opting the rest in: 56 occurrences across <c>eaw/</c> and <c>foc/</c>
///         whose OWNER types them as a tuple list, zero with fewer than two pairs. Keying on the tag
///         name instead of the owner had said 36 violations - every one of them an
///         <c>Activation_Chance</c> that is a <c>NormalizedFloat</c> or a <c>Float</c> on its own
///         owner, judged against a rule that is not its own.
///     </para>
/// </remarks>
public sealed class EawSchemaTupleListCurveTest
{
    private static readonly string[] TupleListTypes = ["FloatTupleList", "IntFloatTupleList"];

    [Fact]
    public void Every_tuple_list_tag_carries_the_curve_rule()
    {
        var missing = new List<string>();
        var seen = 0;

        foreach (var path in Directory.EnumerateFiles(TagsDirectory(), "*.yaml"))
        foreach (var tag in YamlSchemaParser.ParseTagFile(File.ReadAllText(path)))
        {
            if (!TupleListTypes.Contains(tag.ValueType.ToString(), StringComparer.Ordinal)) continue;

            seen++;
            if (!string.Equals(tag.ValidationOverride?.ValidationId, "control-point-curve",
                    StringComparison.OrdinalIgnoreCase))
                missing.Add($"{Path.GetFileName(path)}:{tag.Tag}");
        }

        // A glob that matched nothing would pass this in silence.
        Assert.True(seen >= 15, $"expected the tuple-list tags to still be there, found {seen}");

        Assert.True(missing.Count == 0,
            "the engine asserts counter >= 2 for BOTH tuple-list type codes in Map_Data_Of_Type, so "
            + "every tag of either type needs control-point-curve - these do not have it: "
            + string.Join(", ", missing));
    }

    private static string TagsDirectory()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaTupleListCurveTest).Assembly.Location)!);
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