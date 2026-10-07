// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     The end-tag repair measured on the shipped files: delete one end tag of a file the game
///     reads cleanly, repair, and compare the tree with the original - element names, attributes
///     and values as the game trims them. Whether the repair writes the end tag on the original
///     line or on one of its own does not matter; what the game reads does.
/// </summary>
/// <remarks>Local only, like the other corpus tests: set <c>AET_XML_CORPUS=1</c>.</remarks>
public sealed partial class XmlEndTagRepairCorpusTest
{
    private const int PerFile = 30;

    [Fact]
    public void DeletedEndTags_RepairRestoresTheTree()
    {
        if (Environment.GetEnvironmentVariable("AET_XML_CORPUS") is not "1")
            Assert.Skip("Set AET_XML_CORPUS=1 to sweep the extracted game trees.");

        var roots = XmlStructuralValidatorCorpusTest.CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip("No extracted game tree found next to the repository root.");

        int cases = 0, offered = 0, restored = 0;
        var misses = new StringBuilder();
        foreach (var root in roots)
        foreach (var path in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            if (XmlGameReader.Read(text).Error is not null || XmlStructuralValidator.StrictReadError(text) is not null)
                continue;
            var expected = Tree(text);

            // An end tag inside a comment is not one the game reads.
            var comments = Comment().Matches(text).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
            var endTags = EndTag().Matches(text)
                .Where(m => !comments.Any(c => m.Index > c.Index && m.Index < c.End))
                .ToList();
            endTags.RemoveAt(endTags.Count - 1); // the root's: nothing after it to close against
            var step = Math.Max(1, endTags.Count / PerFile);
            for (var t = 0; t < endTags.Count; t += step)
            {
                cases++;
                var broken = text.Remove(endTags[t].Index, endTags[t].Length);
                var error = XmlGameReader.Read(broken).Error;
                if (error?.Repair is not { } repair)
                {
                    Miss(misses, path, text, endTags[t].Index, $"no repair ({error?.Category})");
                    continue;
                }

                offered++;
                var repaired = XmlStructureRepairs.Apply(broken, repair);
                if (Tree(repaired) == expected) restored++;
                else Miss(misses, path, text, endTags[t].Index, repair.Title);
            }
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "aet-endtag-repair-misses.txt"),
            $"cases {cases}, offered {offered}, restored {restored}\n\n{misses}");
        // Measured 2026-10-07: every deletion gets a repair; 19 of 24,941 restore a different tree
        // (same-name roots, debug goals whose children share the parent's column, and the one file
        // that nests an element in its own name). The misses list names each one.
        Assert.Equal(24941, cases);
        Assert.Equal(cases, offered);
        Assert.Equal(24922, restored);
    }

    private static void Miss(StringBuilder misses, string path, string text, int at, string what)
    {
        if (misses.Length > 20_000) return;
        var line = text.Take(at).Count(c => c == '\n') + 1;
        misses.AppendLine($"{Path.GetFileName(path)}:{line} - {what}");
    }

    // The game's view: names, attributes, and values trimmed of space, tab, CR and LF only.
    private static string Tree(string text)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch (System.Xml.XmlException ex)
        {
            return "unparsable: " + ex.Message;
        }

        var sb = new StringBuilder();
        Write(doc.Root!, 0);
        return sb.ToString();

        void Write(XElement e, int depth)
        {
            sb.Append(depth).Append(' ').Append(e.Name.LocalName);
            foreach (var a in e.Attributes()) sb.Append(' ').Append(a.Name.LocalName).Append('=').Append(a.Value);
            var value = string.Concat(e.Nodes().OfType<XText>().Select(x => x.Value)).Trim(' ', '\t', '\r', '\n');
            sb.Append(" '").Append(value).Append("'\n");
            foreach (var child in e.Elements()) Write(child, depth + 1);
        }
    }

    [GeneratedRegex(@"</[^>\s]+\s*>")]
    private static partial Regex EndTag();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();
}