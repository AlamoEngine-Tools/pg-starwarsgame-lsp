// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Server.Assets;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Publishes what the loose particle files carry that the game does not apply into the
///     Problems view - on "Re-validate Workspace" only.
/// </summary>
/// <remarks>
///     <para>
///         A particle file is not a text document, so nothing diagnoses it when it is opened or
///         saved, and the preview's own problems live in its panel. This is the one path to the
///         Problems view, and it runs only when the whole workspace is asked for: reading every
///         particle file costs seconds, which is fine for an explicit command and not for startup.
///     </para>
///     <para>
///         Same classification as the preview panel, through <see cref="ParticleNoticeProblems" />:
///         a warning for data the game's reader has no case for, information for editor data it
///         leaves unapplied, and an error for a file the game would fail to load. Packed files are
///         skipped - a diagnostic on a file inside an archive has nowhere to open.
///     </para>
/// </remarks>
public sealed class ParticleFileDiagnosticsPublisher : DiagnosticsPublisherBase
{
    private const string ModelFolder = "data/art/models/";

    private readonly IGameAssetResolver _assets;
    private readonly IMessageCatalog _messages;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger _logger;
    private HashSet<string> _published = [];

    public ParticleFileDiagnosticsPublisher(
        IDiagnosticsSink sink,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IGameAssetResolver assets,
        IFileHelper fileHelper,
        IMessageCatalog messages,
        IGlobalSuppressionStore globalSuppressions,
        IProjectLayerMap layerMap,
        ServerOptions options,
        ILogger<ParticleFileDiagnosticsPublisher> logger)
        : base(sink, indexService, workspaceHost, options, logger, globalSuppressions, layerMap)
    {
        _indexService = indexService;
        _assets = assets;
        _fileHelper = fileHelper;
        _messages = messages;
        _logger = logger;
    }

    protected override string FileExtension => ".alo";

    public override Task RepublishAllAsync(CancellationToken ct)
    {
        foreach (var uri in _published)
            Publish(EmptyParams(uri));
        var published = new HashSet<string>(StringComparer.Ordinal);

        var files = 0;
        var flagged = 0;
        foreach (var path in _indexService.Current.AssetFiles.GetByExtension(".alo"))
        {
            ct.ThrowIfCancellationRequested();
            if (!path.StartsWith(ModelFolder, StringComparison.OrdinalIgnoreCase)
                || _indexService.Current.AssetFiles.IsPackedAsset(path))
                continue;

            var location = _assets.Locate(path);
            var bytes = location is null ? null : _assets.Read(path);
            if (location is null || bytes is null || AloFile.Classify(bytes) != AloFileKind.Particle)
                continue;

            files++;
            var diagnostics = Diagnose(bytes);
            if (diagnostics.Count == 0)
                continue;

            var uri = _fileHelper.PathToFileUri(location.ResolvedPath);
            var kept = FilterSuppressed(diagnostics, []);
            if (kept.Count == 0)
                continue;

            Publish(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri),
                Diagnostics = new Container<Diagnostic>(kept)
            });
            published.Add(uri);
            flagged++;
        }

        _published = published;
        _logger.LogInformation("Particle files: {Files} read, {Flagged} with findings", files, flagged);
        return Task.CompletedTask;
    }

    /// <summary>Text documents never come here: a particle file is bytes, read on the sweep.</summary>
    protected override void PublishForDocument(string uri, string text, GameIndex index)
    {
    }

    private List<Diagnostic> Diagnose(byte[] bytes)
    {
        IReadOnlyList<PreviewProblem> problems;
        try
        {
            problems = ParticleNoticeProblems.For(AloParticleReader.Read(bytes));
        }
        catch (AloFormatException e)
        {
            // What the game chokes on is an error: the file does not load there either.
            problems =
            [
                new PreviewProblem(DiagnosticIds.PreviewModelUnreadable, "error",
                    _messages.Format("ParticleFile_MalformedParticleFile", e.Message))
            ];
        }

        return problems.Select(ToDiagnostic).ToList();
    }

    private static Diagnostic ToDiagnostic(PreviewProblem problem)
    {
        return new Diagnostic
        {
            Range = new Range(new Position(0, 0), new Position(0, 0)),
            Severity = problem.Severity switch
            {
                "error" => DiagnosticSeverity.Error,
                "warning" => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Information
            },
            Code = new DiagnosticCode(problem.DiagnosticId.ToString()),
            Source = AppProperties.LspServerId,
            Message = problem.Message
        };
    }
}