// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Messages;
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
    private readonly IMessageCatalog _messages;
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
        IMessageCatalog messages)
    {
        _fileHelper = fileHelper;
        _projectRegistry = projectRegistry;
        _reloadService = reloadService;
        _fileWriter = fileWriter;
        _logger = logger;
        _messages = messages;
    }

    public async Task<SetLocalisationProjectFormatResult> Handle(
        SetLocalisationProjectFormatParams request, CancellationToken ct)
    {
        var extension = LocalisationFormatUtility.ToExtension(request.Format);
        if (extension is null)
            return Fail(_messages.Format("LocSetFormat_NotLocalisationFormatUseCSV",
                request.Format));

        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank).FirstOrDefault();

        if (rootLayer?.ProjectPath is not { } pgprojPath)
            return Fail(_messages.Format("LocSetFormat_NoModProjectOpenLocalisation"));

        if (rootLayer.TextResourceType is not { } previous || rootLayer.TextRoots.Count == 0)
            return Fail(_messages.Format("LocSetFormat_NoLocalisationYet"));

        var format = request.Format.ToUpperInvariant();
        if (string.Equals(previous, format, StringComparison.OrdinalIgnoreCase))
            return new SetLocalisationProjectFormatResult(false, previous, format, CountInFormat(extension));

        var fs = _fileHelper.FileSystem;
        var relative = fs.Path.GetRelativePath(fs.Path.GetDirectoryName(pgprojPath)!, rootLayer.TextRoots[0]);
        if (fs.Path.IsPathRooted(relative))
            return Fail(
                _messages.Format("LocSetFormat_NotBelowProject", rootLayer.TextRoots[0]));

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