// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using Json.Schema;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

/// <summary>
///     Drift guard: <c>PG.StarWarsGame.LSP.Core/Resources/pgproj.schema.json</c> is the canonical
///     authoring schema (copied into the VS Code client at build time) and a hand-maintained mirror
///     of the server's <c>ModProjectFileDto</c> contract. This asserts the shipped vanilla project
///     files actually validate against it, so the schema cannot silently drift away from the shape
///     the server parses and ships.
/// </summary>
public sealed class PgprojSchemaTest
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly JsonSchema Schema = JsonSchema.FromFile(
        Path.Combine(RepoRoot, "PG.StarWarsGame.LSP.Core", "Resources", "pgproj.schema.json"));

    [Theory]
    [InlineData("eaw/empire-at-war-vanilla.pgproj")]
    [InlineData("foc/forces-of-corruption-vanilla.pgproj")]
    public void ShippedVanillaProject_ValidatesAgainstSchema(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Shipped project file not found: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = Schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, DescribeErrors(relativePath, result));
    }

    // ── localisation.credits ─────────────────────────────────────────────────

    private static EvaluationResults Evaluate(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
    }

    [Theory]
    [InlineData("""{ "detection": "convention" }""")]
    [InlineData("""{ "detection": "explicit", "files": ["rolls.csv"] }""")]
    [InlineData("""{ "detection": "none" }""")]
    [InlineData("""{ "files": ["rolls.csv"] }""")]
    [InlineData("""{ }""")]
    public void CreditsNode_AcceptsTheDocumentedShapes(string credits)
    {
        var json = $$"""
                     {
                       "name": "Mod",
                       "localisation": { "type": "CSV", "directory": "data/text", "credits": {{credits}} }
                     }
                     """;

        Assert.True(Evaluate(json).IsValid, DescribeErrors("credits node", Evaluate(json)));
    }

    // The authoring schema is what gives a modder red squiggles in the editor before the server
    // ever sees the file; if it accepted a bad value the only feedback would be a load failure.
    [Theory]
    [InlineData("""{ "detection": "guess" }""")]
    [InlineData("""{ "detection": 1 }""")]
    [InlineData("""{ "files": "rolls.csv" }""")]
    [InlineData("""{ "unknown": true }""")]
    public void CreditsNode_RejectsMalformedShapes(string credits)
    {
        var json = $$"""
                     {
                       "name": "Mod",
                       "localisation": { "type": "CSV", "directory": "data/text", "credits": {{credits}} }
                     }
                     """;

        Assert.False(Evaluate(json).IsValid, $"schema wrongly accepted credits: {credits}");
    }

    // Every existing project omits it; making it required would invalidate all of them.
    [Fact]
    public void CreditsNode_IsOptional()
    {
        const string json = """
                            {
                              "name": "Mod",
                              "localisation": { "type": "CSV", "directory": "data/text" }
                            }
                            """;

        Assert.True(Evaluate(json).IsValid);
    }

    // ── icons ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{ "megaTexture": "data/art/textures/mt_mymod" }""")]
    [InlineData("""{ "sourceRoots": ["data/art/icons"] }""")]
    [InlineData("""{ "megaTexture": "data/art/textures/mt_mymod", "sourceRoots": [] }""")]
    [InlineData("""{ }""")]
    public void IconsNode_AcceptsTheDocumentedShapes(string icons)
    {
        var json = $$"""
                     {
                       "name": "Mod",
                       "icons": {{icons}}
                     }
                     """;

        Assert.True(Evaluate(json).IsValid, DescribeErrors("icons node", Evaluate(json)));
    }

    // megaTexture names a .mtd/.tga PAIR, so an extension means the author misunderstood the field.
    // Catching it in the authoring schema turns a load failure into a red squiggle while typing.
    [Theory]
    [InlineData("""{ "megaTexture": "data/art/textures/mt_mymod.mtd" }""")]
    [InlineData("""{ "megaTexture": "data/art/textures/mt_mymod.tga" }""")]
    [InlineData("""{ "megaTexture": "data/art/textures/mt_mymod.TGA" }""")]
    [InlineData("""{ "megaTexture": "" }""")]
    [InlineData("""{ "megaTexture": 1 }""")]
    [InlineData("""{ "sourceRoots": "data/art/icons" }""")]
    [InlineData("""{ "sourceRoots": [""] }""")]
    [InlineData("""{ "unknown": true }""")]
    public void IconsNode_RejectsMalformedShapes(string icons)
    {
        var json = $$"""
                     {
                       "name": "Mod",
                       "icons": {{icons}}
                     }
                     """;

        Assert.False(Evaluate(json).IsValid, $"schema wrongly accepted icons: {icons}");
    }

    [Fact]
    public void IconsNode_IsOptional()
    {
        const string json = """{ "name": "Mod" }""";

        Assert.True(Evaluate(json).IsValid);
    }

    private static string DescribeErrors(string file, EvaluationResults result)
    {
        var lines = (result.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"  at {d.InstanceLocation} [{e.Key}]: {e.Value}"));
        return $"'{file}' does not validate against pgproj.schema.json:\n{string.Join("\n", lines)}";
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PG.StarWarsGame.LSP.slnx")))
            dir = dir.Parent;
        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate repo root (PG.StarWarsGame.LSP.slnx) above the test output directory.");
    }
}