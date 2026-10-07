// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Xml.Formatting;
using PG.StarWarsGame.LSP.Xml.Tests.Validation;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Formatting;

/// <summary>
///     Formats every shipped XML file standard XML tools accept, and checks the result reads the
///     same: the game reader and the strict reader report exactly what they reported before, the
///     lenient tree is unchanged, line endings keep their kind, and a second pass changes nothing.
/// </summary>
/// <remarks>Local only, like the structural corpus test: set <c>AET_XML_CORPUS=1</c>.</remarks>
public sealed class XmlFormatterCorpusTest
{
    [Fact]
    public void ShippedFiles_FormatWithoutChangingWhatTheyRead()
    {
        if (Environment.GetEnvironmentVariable("AET_XML_CORPUS") is not "1")
            Assert.Skip("Set AET_XML_CORPUS=1 to sweep the extracted game trees.");

        var roots = XmlStructuralValidatorCorpusTest.CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip("No extracted game tree found next to the repository root.");

        var validator = new XmlStructuralValidator();
        int strictClean = 0, refused = 0, changed = 0;
        var failures = new List<string>();
        foreach (var root in roots)
        foreach (var path in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            var doc = ParsedXmlDocument.Parse(text);
            var result = XmlFormatter.Format(doc, "\t");
            if (XmlStructuralValidator.StrictReadError(text) is not null)
            {
                refused++;
                if (result.Refusal is null) failures.Add($"{path}: not refused");
                continue;
            }

            strictClean++;
            if (result.Refusal is { } why)
            {
                failures.Add($"{path}: {why}");
                continue;
            }

            var formatted = XmlStructureRepairs.Apply(text, result.Edits);
            if (formatted != text) changed++;
            if (Findings(validator, text) != Findings(validator, formatted)) failures.Add($"{path}: findings changed");
            if (EolKind(text) != EolKind(formatted)) failures.Add($"{path}: line endings changed");
            if (XmlFormatter.Format(ParsedXmlDocument.Parse(formatted), "\t").Edits.Count != 0)
                failures.Add($"{path}: second pass edits");
        }

        Assert.Empty(failures);
        Assert.Equal(993, strictClean);
        Assert.Equal(13, refused);
        Assert.True(changed > 0);
    }

    private static string Findings(XmlStructuralValidator validator, string text)
    {
        return string.Join("|", validator.Validate(text).Select(f => $"{f.Category}:{f.Reason}"));
    }

    private static string EolKind(string text)
    {
        var crlf = text.Contains("\r\n");
        var lf = Regex.IsMatch(text, "(?<!\r)\n");
        return crlf && lf ? "mixed" : crlf ? "crlf" : "lf";
    }
}