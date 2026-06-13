// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using AET.Modinfo.Spec;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using CoreModinfoData = PG.StarWarsGame.LSP.Core.Project.ModinfoData;
using ModinfoData = AET.Modinfo.Model.ModinfoData;

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
        var fileName = _fileHelper.FileSystem.Path.GetFileName(path);

        ModProjectFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ModProjectFileDto>(text, Options);
        }
        catch (JsonException ex)
        {
            throw new ModProjectLoadException(BuildLoadErrorMessage(fileName, ex), ex);
        }

        if (dto is null)
            throw new ModProjectLoadException($"Could not load mod project '{fileName}': the file is empty.");

        IModinfo? parsed = null;
        if (dto.Modinfo is { } modinfoElement)
            try
            {
                parsed = ModinfoData.Parse(modinfoElement.GetRawText());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Mod project file '{Path}' has an invalid modinfo section; modinfo will be ignored.", path);
            }

        var name = dto.Name ?? _fileHelper.FileSystem.Path.GetFileNameWithoutExtension(path);

        var modinfo = parsed is null
            ? null
            : new CoreModinfoData(
                parsed.Name ?? string.Empty,
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

        return new ModProjectFile(name, modinfo, directories, references);
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? paths)
    {
        return paths?.Select(NormalizePath).ToList() ?? (IReadOnlyList<string>)[];
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }

    // Translates System.Text.Json's cryptic deserialization failures into a clear, user-facing
    // message: which file, where in it, and how to fix it. Shown verbatim as an editor notification.
    private static string BuildLoadErrorMessage(string fileName, JsonException ex)
    {
        var location = ex.LineNumber is { } line
            ? $" at line {line + 1}" +
              (ex.BytePositionInLine is { } col ? $", column {col + 1}" : string.Empty)
            : string.Empty;

        var hint = ex.Path is { } jsonPath
                   && jsonPath.Contains("projectReferences", StringComparison.OrdinalIgnoreCase)
            ? "'projectReferences' must be an array of references — either path strings or " +
              "{ \"path\": \"...\" } objects, e.g. [ { \"path\": \"../core/core.pgproj\" } ]."
            : "the file is not valid JSON or does not match the .pgproj schema.";

        return $"Could not load mod project '{fileName}'{location}: {hint}";
    }

    private sealed class ModProjectFileDto
    {
        public string? Name { get; init; }
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