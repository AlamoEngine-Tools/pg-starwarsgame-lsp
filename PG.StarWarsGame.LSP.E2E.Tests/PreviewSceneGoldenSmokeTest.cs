// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The preview scene of six shipped objects, pinned field for field (#314): a frigate, a fighter
///     squadron, a ground vehicle, a hero, a structure and a particle object, each compared against a
///     recording under <c>Golden/preview/</c>. The scene builder's split into section builders must
///     leave every one of them byte-identical.
/// </summary>
/// <remarks>
///     <para>
///         The recording depends on the schema, the baseline and the FoC corpus the server loads, so
///         it is re-recorded when one of them changes deliberately: run the test with
///         <c>AET_GOLDEN_RECORD=1</c> in the environment and it writes the files instead of comparing.
///     </para>
///     <para>
///         The workspace path is scrubbed out of the recording so it reads on another machine, and
///         every PNG data URI is replaced by its SHA-256 and byte count - the pixels are pinned, the
///         recording stays readable.
///     </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class PreviewSceneGoldenSmokeTest(LspServerFixture fixture) : IClassFixture<LspServerFixture>
{
    public const string RecordVariable = "AET_GOLDEN_RECORD";

    /// <summary>One object per shape the scene builder handles differently.</summary>
    private static readonly string[] Objects =
    [
        "Nebulon_B_Frigate", // frigate: hardpoints with mounted turret models
        "Rebel_X-Wing_Squadron", // fighter squadron: a container, the ships are its members
        "AT_AT_Walker", // ground vehicle: a unit weapon, no hardpoints
        "Boba_Fett", // hero: abilities and a death clone
        "Rebel_Star_Base_2", // structure: galactic and tactical models
        "Shield_Penetration_Effect_Particle" // particle object: no tactical model at all
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Regex DataUri = new("data:image/png;base64,([A-Za-z0-9+/=]+)", RegexOptions.Compiled);

    [Fact]
    public async Task TheSixShapes_KeepTheirScenes()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
        var completed = await Task.WhenAny(fixture.ScanCompleted, Task.Delay(TimeSpan.FromMinutes(3)));
        if (completed != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 3 min.");

        var root = LspTestEnvironment.RepositoryRoot
                   ?? throw new InvalidOperationException("No solution above the test binaries.");
        var goldenDirectory = Path.Combine(root, "PG.StarWarsGame.LSP.E2E.Tests", "Golden", "preview");
        var record = Environment.GetEnvironmentVariable(RecordVariable) is { Length: > 0 };
        var failures = new List<string>();

        foreach (var objectId in Objects)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var result = await fixture.Client.SendRequest(new GetPreviewSceneParams { ObjectId = objectId }, cts.Token);
            var actual = Scrub(JsonSerializer.Serialize(result.Scene, JsonOptions), LspTestEnvironment.WorkspacePath);
            var goldenPath = Path.Combine(goldenDirectory, objectId + ".json");

            if (record)
            {
                Directory.CreateDirectory(goldenDirectory);
                await File.WriteAllTextAsync(goldenPath, actual + "\n", cts.Token);
                continue;
            }

            if (!File.Exists(goldenPath))
            {
                failures.Add($"{objectId}: no recording at {goldenPath}; record one with {RecordVariable}=1");
                continue;
            }

            var expected = (await File.ReadAllTextAsync(goldenPath, cts.Token)).TrimEnd('\n', '\r');
            if (expected != actual) failures.Add($"{objectId}:\n{FirstDifferences(expected, actual)}");
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    /// <summary>The workspace path in every spelling becomes <c>&lt;ws&gt;</c>; a PNG becomes its hash.</summary>
    private static string Scrub(string json, string workspacePath)
    {
        var workspaceUri = DocumentUri.FromFileSystemPath(workspacePath).ToString();
        foreach (var spelling in new[]
                 {
                     workspaceUri, workspacePath, workspacePath.Replace('\\', '/'), workspacePath.Replace("\\", "\\\\")
                 })
            json = json.Replace(spelling, "<ws>", StringComparison.OrdinalIgnoreCase);
        return DataUri.Replace(json, m =>
        {
            var bytes = Convert.FromBase64String(m.Groups[1].Value);
            return $"data:image/png;base64,<sha256={Convert.ToHexString(SHA256.HashData(bytes))};bytes={bytes.Length}>";
        });
    }

    /// <summary>The first lines on which the recording and the scene disagree, with their numbers.</summary>
    private static string FirstDifferences(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var report = new StringBuilder();
        var shown = 0;
        for (var i = 0; i < Math.Max(e.Length, a.Length) && shown < 12; i++)
        {
            var left = i < e.Length ? e[i] : "<end of recording>";
            var right = i < a.Length ? a[i] : "<end of scene>";
            if (left == right) continue;
            report.Append("  line ").Append(i + 1).Append("\n    recorded: ").Append(left.Trim())
                .Append("\n    scene:    ").Append(right.Trim()).Append('\n');
            shown++;
        }

        if (e.Length != a.Length)
            report.Append("  ").Append(e.Length).Append(" lines recorded, ").Append(a.Length).Append(" in the scene\n");
        return report.ToString();
    }
}