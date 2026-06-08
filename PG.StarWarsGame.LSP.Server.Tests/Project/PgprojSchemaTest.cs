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

    private static string DescribeErrors(string file, EvaluationResults result)
    {
        var lines = result.Details
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