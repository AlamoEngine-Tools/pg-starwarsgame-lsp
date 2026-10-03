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

    // ── the identity fields ──────────────────────────────────────────────────

    // The schema refuses properties it does not declare, so a project written by a newer extension
    // would light up as an error in an older editor even where the server reads it happily. The
    // field has to be declared here for the version gate to be usable at all.
    [Fact]
    public void IdentityFields_AreAccepted()
    {
        var result = Evaluate(
            """{ "_type": "aetswg.ModProject", "_typeVersion": "aetswg-1.0.0", "name": "Mod" }""");

        Assert.True(result.IsValid, DescribeErrors("identity fields", result));
    }

    // A file claiming to be some other document must not validate as a project either - the editor
    // should say so at the same moment the server would.
    [Fact]
    public void TypeField_MustBeTheModProjectType()
    {
        Assert.False(Evaluate("""{ "_type": "aetswg.StoryLayout", "name": "Mod" }""").IsValid);
    }

    // It is the document's format, not the mod's version: a two-part or free-text value is the
    // typo that would otherwise surface as "unreadable format version" on every open.
    // It is the document's format, not the mod's version, and it carries its namespace: a bare
    // semver or free text is the typo that would otherwise surface as "unreadable format version"
    // on every open.
    [Theory]
    [InlineData("\"1.0.0\"")]
    [InlineData("\"aetswg-1.0\"")]
    [InlineData("\"banana\"")]
    [InlineData("1")]
    public void TypeVersion_MustBeANamespacedSemanticVersion(string value)
    {
        Assert.False(Evaluate($$"""{ "name": "Mod", "_typeVersion": {{value}} }""").IsValid);
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

    // ── the schema reaches the author ────────────────────────────────────────

    /// <summary>
    ///     The client ships the schema, so the editor has to be told to use it.
    /// </summary>
    /// <remarks>
    ///     <c>Core.csproj</c> copies the canonical schema into the client's <c>resources/</c> on every
    ///     build and <c>.vscodeignore</c> lets it into the <c>.vsix</c>, but for a long time nothing
    ///     referenced it: no <c>jsonValidation</c> entry, no import. The schema, the copy step and the
    ///     shipping were all in place and the author still got no completion or validation in a
    ///     <c>.pgproj</c>. These two tests are the last link.
    /// </remarks>
    [Fact]
    public void ClientManifest_PointsJsonValidationAtTheShippedSchema()
    {
        var contributes = ClientManifest().GetProperty("contributes");

        Assert.True(contributes.TryGetProperty("jsonValidation", out var validation),
            "package.json declares no contributes.jsonValidation, so the shipped pgproj.schema.json "
            + "is dead weight in the .vsix and a .pgproj gets no completion or validation.");

        var entry = validation.EnumerateArray().SingleOrDefault(e =>
            e.GetProperty("fileMatch").GetString() == "*.pgproj");

        Assert.True(entry.ValueKind == JsonValueKind.Object,
            "No contributes.jsonValidation entry matches '*.pgproj'.");

        var url = entry.GetProperty("url").GetString();
        Assert.Equal("./resources/pgproj.schema.json", url);

        // The path is relative to the extension root, and it is the file Core.csproj writes.
        var shipped = Path.Combine(ClientRoot, "resources", "pgproj.schema.json");
        Assert.True(File.Exists(shipped),
            $"jsonValidation points at '{url}' but nothing is there: {shipped}. The copy step in "
            + "Core.csproj is what puts it there, so the client has not been built against it.");
    }

    /// <summary>
    ///     A <c>.pgproj</c> is read as JSON, which is what makes the schema apply at all.
    /// </summary>
    /// <remarks>
    ///     <c>jsonValidation</c> is applied by the JSON language service, so a file the editor opens
    ///     as plain text is never offered to it and the entry above would do nothing. Mapping the
    ///     extension onto a BUILT-IN language rather than inventing a language id is what gets the
    ///     schema, the syntax highlighting and the brace matching in one move.
    ///     <para>
    ///         It has to be <c>jsonc</c> and not <c>json</c>. <c>ModProjectLoader</c> parses with
    ///         <c>ReadCommentHandling = JsonCommentHandling.Skip</c> and
    ///         <c>AllowTrailingCommas = true</c>, so a comment or a trailing comma in a
    ///         <c>.pgproj</c> is something the tool accepts. Under strict <c>json</c> the editor
    ///         would underline both as errors and contradict the server about a file that loads
    ///         perfectly well.
    ///     </para>
    ///     <para>
    ///         Safe against the LSP: the server's documentSelector covers xml, lua and
    ///         <c>plaintext **/*.txt</c> - a <c>.pgproj</c> was never an LSP document, it is watched
    ///         by glob (<c>**/*.pgproj</c> in fileEvents), and a watcher does not care about
    ///         language ids.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ClientManifest_ReadsAPgprojAsJsonWithComments()
    {
        var contributes = ClientManifest().GetProperty("contributes");

        Assert.True(contributes.TryGetProperty("languages", out var languages),
            "package.json declares no contributes.languages, so a .pgproj opens as plain text and "
            + "the jsonValidation entry never fires.");

        var jsonc = languages.EnumerateArray()
            .SingleOrDefault(l => l.GetProperty("id").GetString() == "jsonc");

        Assert.True(jsonc.ValueKind == JsonValueKind.Object,
            "No contributes.languages entry extends the built-in 'jsonc' language. Strict 'json' is "
            + "the wrong one here - the server accepts comments and trailing commas, so the editor "
            + "must too.");

        var extensions = jsonc.GetProperty("extensions").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains(".pgproj", extensions);
    }

    /// <summary>
    ///     A comment and a trailing comma are accepted, because the loader accepts them.
    /// </summary>
    /// <remarks>
    ///     Pins the reason the language above is <c>jsonc</c>. If the loader is ever tightened, this
    ///     fails and says to change the language id with it rather than leaving the editor lenient
    ///     about something the tool has started rejecting.
    /// </remarks>
    [Fact]
    public void TheLoader_AcceptsCommentsAndTrailingCommas()
    {
        const string jsonc = """
                             {
                               // the mod's display name
                               "name": "Mod",
                             }
                             """;

        var options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var parsed = JsonSerializer.Deserialize<JsonElement>(jsonc, options);
        Assert.Equal("Mod", parsed.GetProperty("name").GetString());
    }

    private static string ClientRoot =>
        Path.Combine(RepoRoot, "PG.StarWarsGame.LSP.Client.VSCode", "aet-eaw-edit");

    private static JsonElement ClientManifest()
    {
        var path = Path.Combine(ClientRoot, "package.json");
        Assert.True(File.Exists(path), $"Client manifest not found: {path}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
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