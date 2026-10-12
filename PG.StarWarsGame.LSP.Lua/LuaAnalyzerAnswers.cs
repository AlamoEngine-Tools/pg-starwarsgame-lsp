// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Lua;

/// <summary>How the Lua analyzer's answers are combined with this server's.</summary>
public static partial class LuaAnalyzerAnswers
{
    /// <summary>
    ///     One hover card: this server's first - it knows the engine and the XML - then a rule, then
    ///     the analyzer's. Either alone when the other has nothing.
    /// </summary>
    public static Hover? MergeHover(Hover? ours, Hover? theirs)
    {
        var theirText = theirs is null ? "" : CleanAnalyzerMarkdown(Markdown(theirs.Contents));
        if (theirText.Length == 0) return ours;
        if (ours is null)
            return new Hover { Contents = Content(theirText), Range = theirs!.Range };

        return new Hover
        {
            Contents = Content(Markdown(ours.Contents) + "\n\n---\n\n" + theirText),
            Range = ours.Range ?? theirs!.Range
        };
    }

    // Marks an item as the analyzer's, around its own data, so resolve goes back to it.
    private const string AnalyzerItemMarker = "aet.analyzer";

    /// <summary>
    ///     Our items, then every analyzer item whose label is not already ours - ours know the engine
    ///     and the XML. Each analyzer item carries its own data inside a marker, for resolve.
    /// </summary>
    public static CompletionList MergeCompletion(CompletionList ours, CompletionList? theirs)
    {
        if (theirs is null || !theirs.Items.Any()) return ours;
        var taken = new HashSet<string>(ours.Items.Select(i => i.Label), StringComparer.Ordinal);
        var added = theirs.Items
            .Where(i => taken.Add(i.Label))
            .Select(i => i with { Data = new JObject { [AnalyzerItemMarker] = true, ["data"] = i.Data } });
        return new CompletionList(ours.Items.Concat(added), ours.IsIncomplete || theirs.IsIncomplete);
    }

    /// <summary>The analyzer's item as it sent it, or null when the item is ours.</summary>
    public static CompletionItem? AnalyzerItem(CompletionItem item)
    {
        if (item.Data is not JObject data || data[AnalyzerItemMarker]?.Type != JTokenType.Boolean) return null;
        return item with { Data = data["data"] };
    }

    /// <summary>The text of a hover's contents as Markdown, whichever form it came in.</summary>
    public static string Markdown(MarkedStringsOrMarkupContent contents)
    {
        if (contents.HasMarkupContent) return contents.MarkupContent!.Value;
        return string.Join("\n\n", contents.MarkedStrings?.Select(m =>
            string.IsNullOrEmpty(m.Language) ? m.Value : $"```{m.Language}\n{m.Value}\n```") ?? []);
    }

    // The analyzer renders tags it does not know as text; ours (@aetref, @xmlref) are this
    // server's to show, as the reference card. Measured in the spike: "@*xmlref* XmlObject".
    private static string CleanAnalyzerMarkdown(string markdown)
    {
        var kept = ReferenceTagLine().Replace(markdown, "");
        return BlankRun().Replace(kept, "\n\n").Trim();
    }

    private static MarkedStringsOrMarkupContent Content(string markdown)
    {
        return new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = markdown });
    }

    [GeneratedRegex(@"^[ \t]*@\*?(aetref|xmlref)\*?.*$", RegexOptions.Multiline)]
    private static partial Regex ReferenceTagLine();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRun();
}