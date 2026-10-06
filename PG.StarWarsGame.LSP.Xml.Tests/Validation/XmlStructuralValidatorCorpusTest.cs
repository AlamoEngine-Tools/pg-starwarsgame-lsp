// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     The structural findings over every shipped XML file, counted per category and file. The
///     numbers are the corpus survey's (1006 files, measured 2026-10-06); a change to the game
///     reader emulation or the strict pass that moves one is a change in what modders see.
/// </summary>
/// <remarks>
///     Local only: the extracted game trees are not in every checkout. Set
///     <c>AET_XML_CORPUS=1</c> to run it.
/// </remarks>
public sealed class XmlStructuralValidatorCorpusTest
{
    private const string OptInVariable = "AET_XML_CORPUS";

    [Fact]
    public void ShippedFiles_FindingsPerCategory_MatchTheSurvey()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) is not "1")
            Assert.Skip($"Set {OptInVariable}=1 to sweep the extracted game trees.");

        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip("No extracted game tree found next to the repository root.");

        var validator = new XmlStructuralValidator();
        var files = 0;
        var filesPerCategory = new Dictionary<XmlStrictnessCategory, int>();
        var findingsPerCategory = new Dictionary<XmlStrictnessCategory, int>();
        foreach (var root in roots)
        foreach (var path in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
        {
            files++;
            var findings = validator.Validate(File.ReadAllText(path));
            foreach (var category in findings.Select(f => f.Category).Distinct())
                filesPerCategory[category] = filesPerCategory.GetValueOrDefault(category) + 1;
            foreach (var f in findings)
                findingsPerCategory[f.Category] = findingsPerCategory.GetValueOrDefault(f.Category) + 1;
        }

        Assert.Equal(1006, files);
        Assert.Equal(new Dictionary<XmlStrictnessCategory, int>
        {
            [XmlStrictnessCategory.EndTagMismatch] = 6,
            [XmlStrictnessCategory.EndTagCaseMismatch] = 1,
            [XmlStrictnessCategory.EmptyRoot] = 2,
            [XmlStrictnessCategory.MalformedDeclaration] = 6,
            [XmlStrictnessCategory.CharacterDataAfterChild] = 10,
            [XmlStrictnessCategory.CommentInsideValue] = 5,
            [XmlStrictnessCategory.UntrimmedValueCharacter] = 1
        }, filesPerCategory);
        // One finding per comment: 16. The survey counted runs of text after a comment (15); FoC's
        // Starbases.xml has two comments in one run.
        Assert.Equal(16, findingsPerCategory[XmlStrictnessCategory.CommentInsideValue]);
        Assert.Equal(10, findingsPerCategory[XmlStrictnessCategory.CharacterDataAfterChild]);
    }

    [Fact]
    public void ShippedFiles_AfterFixAll_OnlyCategoriesWithoutARepairRemain()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) is not "1")
            Assert.Skip($"Set {OptInVariable}=1 to sweep the extracted game trees.");

        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip("No extracted game tree found next to the repository root.");

        var validator = new XmlStructuralValidator();
        var left = new List<string>();
        foreach (var root in roots)
        foreach (var path in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
        {
            var (fixedText, iterations) = XmlStructureRepairs.FixAll(File.ReadAllText(path), validator, _ => true);
            Assert.True(iterations < XmlStructureRepairs.DefaultIterationCap, $"{path}: fix-all hit the cap");
            left.AddRange(validator.Validate(fixedText)
                .Where(f => f.Repair is not null)
                .Select(f => $"{path}:{f.Line + 1} {f.Category}"));
        }

        Assert.Empty(left);
    }

    /// <summary>The <c>Data/Xml</c> folders of the <c>eaw/</c> and <c>foc/</c> trees at the repository root.</summary>
    private static List<string> CorpusRoots()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eaw", "Data")))
            dir = dir.Parent;

        if (dir is null)
            return [];

        return new[] { "eaw", "foc" }
            .Select(g => Path.Combine(dir.FullName, g, "Data", "Xml"))
            .Where(Directory.Exists)
            .ToList();
    }
}