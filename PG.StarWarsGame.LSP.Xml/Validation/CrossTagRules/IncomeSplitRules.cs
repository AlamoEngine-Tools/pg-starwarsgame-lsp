// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Shared plumbing for the three rules in <c>IncomeStreamAbilityClass::Validate_Data</c>
///     (<c>0100f6a0</c>), all of which are gated on flags rather than stated about a tag.
/// </summary>
public abstract class IncomeStreamRuleBase : IXmlCrossTagRule
{
    protected const string ElementName = "income_stream_ability";
    protected const string FavorsTag = "Split_Favors_Owner";
    protected const string AlliesTag = "Split_Income_With_Allies";
    protected const string FullAmountTag = "Full_Amount_To_Everyone";
    protected const string ShareTag = "Owner_Income_Percentage";

    public abstract IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex);

    protected static bool IsIncomeStream(HtmlNode node)
    {
        return string.Equals(node.Name, ElementName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The last occurrence, matching the engine's keep-the-last rule for repeated tags.</summary>
    protected static HtmlNode? Last(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> children, string tag)
    {
        return children.TryGetValue(tag, out var nodes) && nodes.Count > 0 ? nodes[^1] : null;
    }

    protected static bool IsOn(
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> children, string tag)
    {
        return Last(children, tag) is { } node && EngineBoolean.IsTrue(node.InnerText.Trim());
    }
}

/// <summary>
///     The owner's share of a split income, outside the range the engine accepts.
/// </summary>
/// <remarks>
///     <c>(x &lt; 0.0) || (1.0 &lt;= x)</c> followed by <c>Clamp(x, 0.0, 0.99)</c>, and only inside
///     the branch where both split flags are on.
/// </remarks>
public sealed class OwnerIncomeShareRule : IncomeStreamRuleBase
{
    public override IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!IsIncomeStream(objectNode)) return [];
        if (!IsOn(childrenByName, FavorsTag) || !IsOn(childrenByName, AlliesTag)) return [];

        var node = Last(childrenByName, ShareTag);
        if (node is null) return [];

        var raw = node.InnerText.Trim();
        if (!double.TryParse(raw.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var value))
            return [];

        if (value >= 0.0 && value < 1.0) return [];

        var (line, column, length) = XmlUtility.GetValuePosition(node, lineIndex);

        return
        [
            new OwnerIncomeShareFact(documentUri, line, column, length, value,
                value < 0.0 ? "0.0" : "0.99")
        ];
    }
}

/// <summary>
///     <c>Split_Favors_Owner</c> switched on where there is no split for it to favour.
/// </summary>
/// <remarks>
///     The engine's message reads "irrelevant when Split_Income_With_Allies is true", but the branch
///     is the ELSE of that test, so it fires when allies splitting is OFF. It then clears the flag
///     and zeroes the share.
/// </remarks>
public sealed class SplitFavorsOwnerIgnoredRule : IncomeStreamRuleBase
{
    public override IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!IsIncomeStream(objectNode)) return [];

        var favors = Last(childrenByName, FavorsTag);
        if (favors is null || !EngineBoolean.IsTrue(favors.InnerText.Trim())) return [];
        if (IsOn(childrenByName, AlliesTag)) return [];

        // The engine clears the flag and zeroes the share; only edit tags that are actually written.
        var edits = new List<XmlDiagnosticEdit>();
        if (ValueEditFactory.ReplaceValue(favors, lineIndex, "No") is { } off) edits.Add(off);
        if (ValueEditFactory.ReplaceValue(Last(childrenByName, ShareTag), lineIndex, "0.0") is { } zero)
            edits.Add(zero);

        return
        [
            new IncomeSplitConflictFact(documentUri,
                XmlUtility.GetLine(favors),
                XmlUtility.GetTagBracketColumn(favors),
                XmlUtility.GetOpeningTagLength(favors),
                FavorsTag, AlliesTag,
                $"does nothing without <{AlliesTag}>",
                "turns it off and zeroes Owner_Income_Percentage",
                edits.Count == 0
                    ? null
                    : new XmlEngineRepair(
                        "Apply the engine's own correction: turn Split_Favors_Owner off", edits))
        ];
    }
}

/// <summary>
///     <c>Split_Favors_Owner</c> and <c>Full_Amount_To_Everyone</c> both on, which the engine
///     refuses by clearing both.
/// </summary>
/// <remarks>
///     Requires the allies split to be ON as well, and that is measured rather than inferred from
///     the message: this test runs AFTER the ignored-flag branch, which has already set
///     <c>SplitFavorsOwner</c> to false when the allies split is off. With no allies split the
///     engine therefore never reaches this complaint, and reporting it would put two diagnostics
///     where the game emits one.
/// </remarks>
public sealed class SplitFavorsOwnerVsFullAmountRule : IncomeStreamRuleBase
{
    public override IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex)
    {
        if (!IsIncomeStream(objectNode)) return [];

        var favors = Last(childrenByName, FavorsTag);
        var full = Last(childrenByName, FullAmountTag);
        if (favors is null || full is null) return [];
        if (!EngineBoolean.IsTrue(favors.InnerText.Trim())) return [];
        if (!EngineBoolean.IsTrue(full.InnerText.Trim())) return [];
        if (!IsOn(childrenByName, AlliesTag)) return [];

        var edits = new List<XmlDiagnosticEdit>();
        if (ValueEditFactory.ReplaceValue(favors, lineIndex, "No") is { } a) edits.Add(a);
        if (ValueEditFactory.ReplaceValue(full, lineIndex, "No") is { } b) edits.Add(b);

        return
        [
            new IncomeSplitConflictFact(documentUri,
                XmlUtility.GetLine(favors),
                XmlUtility.GetTagBracketColumn(favors),
                XmlUtility.GetOpeningTagLength(favors),
                FavorsTag, FullAmountTag,
                $"cannot be set together with <{FullAmountTag}>",
                "turns both off",
                edits.Count == 0
                    ? null
                    : new XmlEngineRepair(
                        "Apply the engine's own correction: turn both flags off", edits))
        ];
    }
}
