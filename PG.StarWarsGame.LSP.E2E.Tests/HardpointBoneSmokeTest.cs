// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Guards the hardpoint attachment-bone validation against false positives, opening the real
///     <c>Hardpoints_underworld.xml</c>.
///     <para>
///         <c>HP_Underworld_Station_L1_Comm_Array</c> declares <c>Attachment_Bone HP01_COM_BONE</c>
///         and is mounted by the underworld starbases. Each starbase carries a tactical
///         <c>Space_Model_Name</c> (which has that bone, spelled <c>HP01_COM_Bone</c>) AND a low-detail
///         <c>Galactic_Model_Name</c> (<c>i_ub_0X_station.alo</c>, which legitimately has no hardpoint
///         bones). The bone must only be validated against the tactical model - checking it against the
///         galactic model produced a nondeterministic false positive ("erratic" across runs, because
///         the mounting objects are visited in dictionary order).
///     </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class HardpointBoneSmokeTest : IClassFixture<LspServerFixture>, IAsyncDisposable
{
    private readonly LspServerFixture _fixture;
    private readonly List<DocumentUri> _openedUris = [];

    public HardpointBoneSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    private static string XmlDir => Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML");

    public async ValueTask DisposeAsync()
    {
        foreach (var uri in _openedUris)
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri }
            });
        if (_openedUris.Count > 0)
            await Task.Delay(500);
    }

    [Fact]
    public async Task AttachmentBoneOnTacticalModel_NotFlaggedAgainstGalacticModel()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var filePath = Path.Combine(XmlDir, "Hardpoints_underworld.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(20));
        _openedUris.Add(uri);
        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, LanguageId = "xml", Version = 1,
                Text = await File.ReadAllTextAsync(filePath)
            }
        });

        var diags = await received;

        // HP01_COM_BONE exists on every tactical station model, so no "does not exist on" diagnostic
        // for it must be published - the only model that lacks it is the galactic one, which hardpoints
        // do not attach to.
        var falsePositives = diags.Diagnostics
            .Where(d => d.Message.Contains("HP01_COM_BONE", StringComparison.OrdinalIgnoreCase)
                        && d.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Message)
            .ToList();

        Assert.True(falsePositives.Count == 0,
            "Attachment bone HP01_COM_BONE was flagged as missing, but it exists on the tactical station "
            + "models; only the galactic model lacks it. Offending diagnostics:\n"
            + string.Join("\n", falsePositives));
    }

    [Fact]
    public async Task MeshOnlyDamageDecal_NotFlaggedAgainstStationModel()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var filePath = Path.Combine(XmlDir, "Hardpoints_underworld.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(20));
        _openedUris.Add(uri);
        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, LanguageId = "xml", Version = 1,
                Text = await File.ReadAllTextAsync(filePath)
            }
        });

        var diags = await received;

        // HP_Underworld_Station_L2_LC_01 declares Damage_Decal HP02_LC_BLAST, which the L2-L5 station
        // models carry only as a MESH (never a skeleton bone). The engine synthesises a bone at the mesh
        // origin, so this is a valid reference: validating bones ∪ mesh names must not flag it. Before the
        // union it was a false positive on every station that mounts the L2 hardpoint.
        var falsePositives = diags.Diagnostics
            .Where(d => d.Message.Contains("HP02_LC_BLAST", StringComparison.OrdinalIgnoreCase)
                        && d.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Message)
            .ToList();

        Assert.True(falsePositives.Count == 0,
            "Damage decal HP02_LC_BLAST was flagged as missing, but it exists as a mesh on the station "
            + "models and the engine resolves it as a bone at the mesh origin. Offending diagnostics:\n"
            + string.Join("\n", falsePositives));
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }
}
