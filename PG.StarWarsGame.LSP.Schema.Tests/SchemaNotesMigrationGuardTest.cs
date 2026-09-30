// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Guards the 2.0.0 note contract across the shipped schema files.
///     <para>
///         This is the only thing standing between a missed file and silence. The YAML deserialiser
///         runs with <c>IgnoreUnmatchedProperties</c>, so a leftover <c>deprecated: true</c> is not
///         an error - it is simply not read, and a tag quietly stops being deprecated. The JSON
///         schemas under <c>.schemas/</c> forbid these keys, but nothing executes them: they help an
///         editor and no build.
///     </para>
/// </summary>
public sealed class SchemaNotesMigrationGuardTest
{
    /// <summary>The fields the note list replaced. None of them may survive anywhere.</summary>
    private static readonly string[] RetiredKeys = ["deprecated", "untested", "availableSince"];

    /// <summary>
    ///     The schema repository root. <see cref="EawSchemaRepo.Root" /> is <c>schema/eaw</c>, and
    ///     every check here has to cover <c>foc</c> as well - it is the flavour whose own files did
    ///     not change in 2.0.0, and therefore the one most easily left behind.
    /// </summary>
    private static string SchemaRoot => Directory.GetParent(EawSchemaRepo.Root)!.FullName;

    private static IEnumerable<string> AllYamlFiles()
    {
        return Directory
            .EnumerateFiles(SchemaRoot, "*.yaml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SchemaRoot, f)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    [Fact]
    public void NoSchemaFile_StillCarriesARetiredAnnotation()
    {
        var offenders = new List<string>();
        foreach (var file in AllYamlFiles())
        {
            var lines = File.ReadAllLines(Path.Combine(SchemaRoot, file));
            for (var i = 0; i < lines.Length; i++)
                foreach (var key in RetiredKeys)
                    if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], $@"^\s*{key}\s*:"))
                        offenders.Add($"{file}:{i + 1} {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "These fields were replaced by notes in schema 2.0.0 and are no longer read:\n"
            + string.Join("\n", offenders));
    }

    // A note written as a locale map is the pre-2.0.0 shape. The parser rejects it outright, so this
    // only ever fails on a file nothing has loaded yet - which is exactly when it is worth knowing.
    [Fact]
    public void EveryNotesBlock_IsAListOfKindedNotes()
    {
        var offenders = new List<string>();
        foreach (var file in AllYamlFiles())
        {
            var lines = File.ReadAllLines(Path.Combine(SchemaRoot, file));
            for (var i = 0; i < lines.Length; i++)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"^\s*notes:\s*$")) continue;

                var next = Array.FindIndex(lines, i + 1, l => l.Trim().Length > 0);
                if (next < 0) continue;
                if (!lines[next].TrimStart().StartsWith("- ", StringComparison.Ordinal))
                    offenders.Add($"{file}:{i + 1} notes is a locale map, not a list");
            }
        }

        Assert.True(offenders.Count == 0,
            "A note must declare its kind:\n" + string.Join("\n", offenders));
    }

    // Every kind the schema uses has to be one the server can classify, or loading throws. Cheaper
    // to learn here, by name, than from a stack trace at startup.
    [Fact]
    public void EveryNoteKind_IsOneTheServerKnows()
    {
        var known = Enum.GetNames<Core.Schema.SchemaNoteKind>();
        var offenders = new List<string>();

        foreach (var file in AllYamlFiles())
        {
            var lines = File.ReadAllLines(Path.Combine(SchemaRoot, file));
            for (var i = 0; i < lines.Length; i++)
            {
                // Only kinds inside a notes block. `kinds.yaml` spells its object kinds
                // `- kind: SpaceUnit`, the same two words meaning something else entirely - the
                // parser tells them apart by nesting, and so must anything reading the text.
                var notes = System.Text.RegularExpressions.Regex.Match(lines[i], @"^(\s*)notes:\s*$");
                if (!notes.Success) continue;
                var owner = notes.Groups[1].Value.Length;

                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].Trim().Length == 0) continue;
                    if (lines[j].Length - lines[j].TrimStart().Length <= owner) break;

                    var match = System.Text.RegularExpressions.Regex.Match(lines[j], @"^\s*-\s*kind:\s*(\S+)");
                    if (!match.Success) continue;
                    if (!known.Contains(match.Groups[1].Value, StringComparer.OrdinalIgnoreCase))
                        offenders.Add($"{file}:{j + 1} {match.Groups[1].Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"Known kinds are {string.Join(", ", known)}:\n" + string.Join("\n", offenders));
    }

    // The contract the parser now implements. Both flavours must declare it, and foc is the one that
    // is easy to forget because none of its own files changed.
    [Theory]
    [InlineData("eaw")]
    [InlineData("foc")]
    public void EveryFlavour_DeclaresTheCurrentContract(string flavour)
    {
        var manifest = Path.Combine(SchemaRoot, flavour, "_index.json");
        Assert.True(File.Exists(manifest), $"{flavour}/_index.json is missing");

        var version = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest))
            .RootElement.GetProperty("schemaVersion").GetString();

        Assert.Equal(Versioning.SchemaVersionCompatibility.Supported,
            Versioning.SchemaVersionGate.Check(version).Compatibility);
    }
}