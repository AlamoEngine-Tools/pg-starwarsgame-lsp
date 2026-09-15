// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Repoints the root project's declared localisation format, converting nothing (#121).
/// </summary>
/// <remarks>
///     <para>
///         The one way to change which files a project loads used to be editing the <c>.pgproj</c>: the
///         <c>aet-eaw-edit.localisation.format</c> setting only seeds NEW projects, and converting a file
///         repoints the project only as a side effect, and never to DAT. The reporter had DAT files and
///         a project declaring CSV.
///     </para>
///     <para>
///         The directory is kept as the project already declares it - only the type changes, and the
///         writer patches the node in place, so hand-written <c>credits</c> settings survive. Nothing on
///         disk besides the <c>.pgproj</c> is touched, which is why the result counts the files the
///         project can now load: zero means it loads nothing, and that needs saying.
///     </para>
/// </remarks>
public sealed class SetLocalisationProjectFormatHandler
    : IJsonRpcRequestHandler<SetLocalisationProjectFormatParams, SetLocalisationProjectFormatResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly IModProjectFileWriter _fileWriter;
    private readonly ILogger<SetLocalisationProjectFormatHandler> _logger;
    private readonly ILocalisationProjectRegistry _projectRegistry;
    private readonly IModProjectReloadService _reloadService;

    public SetLocalisationProjectFormatHandler(
        IFileHelper fileHelper,
        ILocalisationProjectRegistry projectRegistry,
        IModProjectReloadService reloadService,
        IModProjectFileWriter fileWriter,
        ILogger<SetLocalisationProjectFormatHandler> logger,
        ILspConfigurationProvider config)
    {
        _fileHelper = fileHelper;
        _projectRegistry = projectRegistry;
        _reloadService = reloadService;
        _fileWriter = fileWriter;
        _logger = logger;
        _config = config;
    }

    public async Task<SetLocalisationProjectFormatResult> Handle(
        SetLocalisationProjectFormatParams request, CancellationToken ct)
    {
        if (LocalisationFeatureDisabled.Rejection(_config) is { } rejection)
            return Fail(rejection);

        var extension = LocalisationFormatUtility.ToExtension(request.Format);
        if (extension is null)
            return Fail($"'{request.Format}' is not a localisation format. Use CSV, XML, NLS or DAT.");

        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank).FirstOrDefault();

        if (rootLayer?.ProjectPath is not { } pgprojPath)
            return Fail("No mod project is open. The localisation format is stored in the .pgproj.");

        if (rootLayer.TextResourceType is not { } previous || rootLayer.TextRoots.Count == 0)
            return Fail(
                "This project has no localisation set up yet. Use 'New Localisation Project' or " +
                "'Import Existing Localisation Files' to create one.");

        var format = request.Format.ToUpperInvariant();
        if (string.Equals(previous, format, StringComparison.OrdinalIgnoreCase))
            return new SetLocalisationProjectFormatResult(false, previous, format, CountInFormat(extension));

        var fs = _fileHelper.FileSystem;
        var relative = fs.Path.GetRelativePath(fs.Path.GetDirectoryName(pgprojPath)!, rootLayer.TextRoots[0]);
        if (fs.Path.IsPathRooted(relative))
            return Fail(
                $"The localisation directory '{rootLayer.TextRoots[0]}' is not below the project, so it " +
                "cannot be written back as a relative path.");

        // The same shape the conversion writes, so the two never disagree about the node.
        await _fileWriter.SetLocalisationAsync(
            pgprojPath, format, relative.Replace('\\', '/').ToLowerInvariant(), ct);
        await _reloadService.ReloadAsync(ct);

        var count = CountInFormat(extension);
        _logger.LogInformation(
            "aet/setLocalisationProjectFormat: '{Project}' now loads {Format} (was {Previous}); {Count} file(s).",
            pgprojPath, format, previous, count);

        return new SetLocalisationProjectFormatResult(true, previous, format, count);
    }

    private int CountInFormat(string extension)
    {
        return _projectRegistry.Projects.Count(p =>
            string.Equals(LocalisationFormatUtility.ToExtension(p.ResourceType), extension,
                StringComparison.OrdinalIgnoreCase));
    }

    private SetLocalisationProjectFormatResult Fail(string message)
    {
        _logger.LogWarning("aet/setLocalisationProjectFormat: {Reason}", message);
        return new SetLocalisationProjectFormatResult(Error: message);
    }
}