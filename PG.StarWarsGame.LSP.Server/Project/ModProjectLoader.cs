// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using AET.Modinfo.Model;
using AET.Modinfo.Spec;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using CoreModinfoData = PG.StarWarsGame.LSP.Core.Project.ModinfoData;

namespace PG.StarWarsGame.LSP.Server.Project;

public sealed class ModProjectLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<ModProjectLoader> _logger;

    public ModProjectLoader(IFileHelper fileHelper, ILogger<ModProjectLoader> logger)
    {
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public ModProjectFile Load(string path)
    {
        var text = _fileHelper.FileSystem.File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<ModProjectFileDto>(text, Options)
                  ?? throw new InvalidOperationException($"Failed to parse mod project file '{path}'.");

        if (dto.Modinfo is not { } modinfoElement)
            throw new InvalidOperationException(
                $"Mod project file '{path}' is missing the required 'modinfo.name'.");

        IModinfo parsed;
        try
        {
            parsed = AET.Modinfo.Model.ModinfoData.Parse(modinfoElement.GetRawText());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Mod project file '{path}' has an invalid 'modinfo' section: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(parsed.Name))
            throw new InvalidOperationException(
                $"Mod project file '{path}' is missing the required 'modinfo.name'.");

        var modinfo = new CoreModinfoData(
            parsed.Name,
            parsed.Version?.ToString(),
            parsed.Summary,
            parsed.Icon,
            parsed.Languages.Select(l => new ModinfoLanguageInfo(l.Code, (int)l.Support)).ToList(),
            parsed.Custom.Count > 0 ? (object)parsed.Custom : null);

        var directories = new DirectoryMap(
            Normalize(dto.Directories?.Xml),
            Normalize(dto.Directories?.Scripts),
            Normalize(dto.Directories?.Art),
            Normalize(dto.Directories?.Audio),
            Normalize(dto.Directories?.Text),
            Normalize(dto.Directories?.Ai),
            dto.Directories?.TextResourceType);

        var references = new List<ProjectReference>();
        foreach (var reference in dto.ProjectReferences ?? [])
        {
            var raw = reference.Path ?? string.Empty;
            if (_fileHelper.FileSystem.Path.IsPathRooted(raw))
                _logger.LogWarning(
                    "Project reference '{Path}' in '{ProjectPath}' is an absolute path; " +
                    "relative paths are recommended for portability.",
                    raw, path);

            references.Add(new ProjectReference(NormalizePath(raw)));
        }

        return new ModProjectFile(modinfo, directories, references);
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? paths)
    {
        return paths?.Select(NormalizePath).ToList() ?? (IReadOnlyList<string>)[];
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }

    private sealed class ModProjectFileDto
    {
        public JsonElement? Modinfo { get; init; }
        public DirectoryMapDto? Directories { get; init; }
        public List<ProjectReferenceDto>? ProjectReferences { get; init; }
    }

    private sealed class DirectoryMapDto
    {
        public List<string>? Xml { get; init; }
        public List<string>? Scripts { get; init; }
        public List<string>? Art { get; init; }
        public List<string>? Audio { get; init; }
        public List<string>? Text { get; init; }
        public List<string>? Ai { get; init; }
        public string? TextResourceType { get; init; }
    }

    private sealed class ProjectReferenceDto
    {
        public string? Path { get; init; }
    }
}
