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

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
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

        ThrowIfLegacyLocalisationShape(text, fileName);

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
            Normalize(dto.Directories?.Ai))
        {
            StoryDialog = Normalize(dto.Directories?.StoryDialog)
        };

        var localisation = ParseLocalisation(dto.Localisation, fileName);

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

        return new ModProjectFile(name, modinfo, directories, references, localisation);
    }

    private static readonly HashSet<string> ValidLocalisationTypes =
        new(StringComparer.OrdinalIgnoreCase) { "CSV", "DAT", "XML", "NLS" };

    private LocalisationProjectSettings? ParseLocalisation(LocalisationDto? dto, string fileName)
    {
        if (dto is null) return null;

        if (string.IsNullOrWhiteSpace(dto.Type) || !ValidLocalisationTypes.Contains(dto.Type))
            throw new ModProjectLoadException(
                $"Could not load mod project '{fileName}': 'localisation.type' must be one of " +
                $"CSV, DAT, XML, NLS (got '{dto.Type}').");

        if (string.IsNullOrWhiteSpace(dto.Directory))
            throw new ModProjectLoadException(
                $"Could not load mod project '{fileName}': 'localisation.directory' is required " +
                "when 'localisation' is present.");

        return new LocalisationProjectSettings(dto.Type.ToUpperInvariant(), NormalizePath(dto.Directory));
    }

    // Clean break: directories.text/textResourceType were removed in 0.2.0 in favour of the
    // top-level "localisation" node. System.Text.Json silently ignores unknown properties, so
    // without this check an un-migrated .pgproj would load "successfully" with its localisation
    // quietly unconfigured — no error, no hint why. Hard-fail instead, per feedback_strict_validation_clear_errors.
    private static void ThrowIfLegacyLocalisationShape(string text, string fileName)
    {
        using var document = JsonDocument.Parse(text, DocumentOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;
        if (!TryGetPropertyCaseInsensitive(root, "directories", out var directories)) return;
        if (directories.ValueKind != JsonValueKind.Object) return;

        var hasLegacyText = TryGetPropertyCaseInsensitive(directories, "text", out _);
        var hasLegacyType = TryGetPropertyCaseInsensitive(directories, "textResourceType", out _);
        if (!hasLegacyText && !hasLegacyType) return;

        throw new ModProjectLoadException(
            $"Could not load mod project '{fileName}': 'directories.text' and 'directories.textResourceType' " +
            "were removed in 0.2.0. Move your localisation directory into a top-level 'localisation' node " +
            "instead, e.g. \"localisation\": { \"type\": \"CSV\", \"directory\": \"data/text\" }. " +
            "See the extension's 'Upgrading from 0.1.x' README section for the full migration steps.");
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }

        value = default;
        return false;
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
        public LocalisationDto? Localisation { get; init; }
    }

    private sealed class DirectoryMapDto
    {
        public List<string>? Xml { get; init; }
        public List<string>? Scripts { get; init; }
        public List<string>? Art { get; init; }
        public List<string>? Audio { get; init; }
        public List<string>? Ai { get; init; }
        public List<string>? StoryDialog { get; init; }
    }

    private sealed class ProjectReferenceDto
    {
        public string? Path { get; init; }
    }

    private sealed class LocalisationDto
    {
        public string? Type { get; init; }
        public string? Directory { get; init; }
    }
}