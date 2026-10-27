// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AET.Modinfo.Spec;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
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

    private static readonly HashSet<string> ValidLocalisationTypes =
        new(StringComparer.OrdinalIgnoreCase) { "CSV", "DAT", "XML", "NLS" };

    private static readonly HashSet<string> ValidCreditsDetections =
        new(StringComparer.Ordinal)
        {
            LocalisationCreditsSettings.Convention,
            LocalisationCreditsSettings.Explicit,
            LocalisationCreditsSettings.None
        };

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<ModProjectLoader> _logger;
    private readonly IMessageCatalog _messages;
    private readonly IPgprojMigrationSink _migrationSink;
    private readonly IReadOnlyList<IDocumentMigration> _migrations;

    public ModProjectLoader(
        IFileHelper fileHelper,
        ILogger<ModProjectLoader> logger,
        IPgprojMigrationSink migrationSink,
        IReadOnlyList<IDocumentMigration> migrations,
        IMessageCatalog messages)
    {
        _messages = messages;
        _fileHelper = fileHelper;
        _logger = logger;
        _migrationSink = migrationSink;
        _migrations = migrations;
    }

    public ModProjectFile Load(string path)
    {
        var text = _fileHelper.FileSystem.File.ReadAllText(path);
        var fileName = _fileHelper.FileSystem.Path.GetFileName(path);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ModProjectLoadException(BuildLoadErrorMessage(fileName, ex), ex, ProjectProblem.Unparseable);
        }

        if (root is not JsonObject document)
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject14", fileName),
                problem: ProjectProblem.Unparseable);

        // JSON permits a repeated property and the parser accepts one; JsonObject only objects on
        // the first access, as a dictionary-key ArgumentException. A hand-edited file (the envelope
        // pasted in twice) deserves the file and property named instead.
        if (FindDuplicateProperty(text) is { } duplicate)
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject13", fileName, duplicate),
                problem: ProjectProblem.Unparseable);

        // Before anything is read out of it: a project from a newer extension is refused rather
        // than read as best we can, because this build would go on to write its own shape back
        // over it. Absent means version one - every .pgproj written so far predates the fields.
        var declaredType = (string?)document["_type"];
        var declaredVersion = (string?)document["_typeVersion"];
        if (PgprojFormat.Check(declaredVersion, declaredType, _messages) is { CanLoad: false } refusal)
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject12", fileName, refusal.Message),
                problem: ProjectProblem.UnsupportedVersion);

        document = BringForward(document, declaredVersion, path, fileName);

        ModProjectFileDto? dto;
        try
        {
            dto = document.Deserialize<ModProjectFileDto>(Options);
        }
        catch (JsonException ex)
        {
            throw new ModProjectLoadException(BuildLoadErrorMessage(fileName, ex), ex);
        }

        if (dto is null)
            throw new ModProjectLoadException(_messages.Format("ModProject_CouldNotLoadModProject11", fileName));

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
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - deserialized data; the declared type is not the payload's contract
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
        var icons = ParseIcons(dto.Icons, fileName);

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

        return new ModProjectFile(name, modinfo, directories, references, localisation, icons);
    }

    // Absence means "use the engine convention", not "no icons" - unlike localisation there is a
    // sensible default, so IconProjectSettings.Default covers a project that says nothing. A node
    // that IS present is validated strictly: a half-configured icons block is a mistake worth
    // reporting, not something to silently paper over.
    private IconProjectSettings? ParseIcons(IconsDto? dto, string fileName)
    {
        if (dto is null) return null;

        var megaTexture = IconProjectSettings.ConventionalMegaTexture;
        if (dto.MegaTexture is not null)
        {
            if (string.IsNullOrWhiteSpace(dto.MegaTexture))
                throw new ModProjectLoadException(
                    _messages.Format("ModProject_CouldNotLoadModProject10", fileName,
                        IconProjectSettings.ConventionalMegaTexture));

            // It names a PAIR - foo.mtd and foo.tga - so an extension here is ambiguous about which
            // half the author meant, and silently stripping it would hide a real misunderstanding.
            var extension = Path.GetExtension(dto.MegaTexture);
            if (extension.Equals(".mtd", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
                throw new ModProjectLoadException(
                    _messages.Format("ModProject_CouldNotLoadModProject9", fileName, dto.MegaTexture,
                        IconProjectSettings.ConventionalMegaTexture));

            megaTexture = NormalizePath(dto.MegaTexture);
        }

        var sourceRoots = new List<string>();
        foreach (var root in dto.SourceRoots ?? [])
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ModProjectLoadException(
                    _messages.Format("ModProject_CouldNotLoadModProject8", fileName));

            sourceRoots.Add(NormalizePath(root));
        }

        return new IconProjectSettings(megaTexture, sourceRoots);
    }

    private LocalisationProjectSettings? ParseLocalisation(LocalisationDto? dto, string fileName)
    {
        if (dto is null) return null;

        if (string.IsNullOrWhiteSpace(dto.Type) || !ValidLocalisationTypes.Contains(dto.Type))
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject7", fileName, dto.Type));

        if (string.IsNullOrWhiteSpace(dto.Directory))
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject6", fileName));

        return new LocalisationProjectSettings(
            dto.Type.ToUpperInvariant(), NormalizePath(dto.Directory), ParseCredits(dto.Credits, fileName));
    }

    private LocalisationCreditsSettings? ParseCredits(CreditsDto? dto, string fileName)
    {
        if (dto is null) return null;

        // Omitted detection means the default, so `"credits": { "files": [...] }` reads as "the
        // convention, plus these" rather than being rejected for an absent field.
        var detection = string.IsNullOrWhiteSpace(dto.Detection)
            ? LocalisationCreditsSettings.Convention
            : dto.Detection.Trim().ToLowerInvariant();

        if (!ValidCreditsDetections.Contains(detection))
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject5", fileName, dto.Detection));

        var files = dto.Files ?? [];

        // An explicit list that lists nothing classifies nothing - indistinguishable from "none",
        // except that the author clearly meant to name something.
        if (detection == LocalisationCreditsSettings.Explicit && files.Count == 0)
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject4", fileName));

        return new LocalisationCreditsSettings(detection, files);
    }

    // Clean break: directories.text/textResourceType were removed in 0.2.0 in favour of the
    // top-level "localisation" node. System.Text.Json silently ignores unknown properties, so
    // without this check an un-migrated .pgproj would load "successfully" with its localisation
    // quietly unconfigured - no error, no hint why. Hard-fail instead, per feedback_strict_validation_clear_errors.
    private void ThrowIfLegacyLocalisationShape(string text, string fileName)
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
            _messages.Format("ModProject_CouldNotLoadModProject3", fileName));
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

    // Separators only. These are author-written paths out of the pgproj - source roots, the mod
    // directory, the mega-texture - and every one of them is used to OPEN something, so flattening
    // the case here is the difference between a working project and an unopenable one on a
    // case-sensitive host. Compare them through DocumentUris, never with Ordinal.
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>The name of the first property that repeats within one object, anywhere in the document.</summary>
    private static string? FindDuplicateProperty(string text)
    {
        using var parsed = JsonDocument.Parse(text, DocumentOptions);
        return FindDuplicateProperty(parsed.RootElement);
    }

    private static string? FindDuplicateProperty(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name)) return property.Name;
                    if (FindDuplicateProperty(property.Value) is { } nested) return nested;
                }

                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (FindDuplicateProperty(item) is { } nested)
                        return nested;
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    ///     Runs the project through its migration chain, in memory, every time it is read.
    /// </summary>
    /// <remarks>
    ///     Always in memory, never as a side effect of reading: the file belongs to the user, sits in
    ///     their repository and is shared with teammates, so writing it is a separate and consented
    ///     act. What this does is make sure the rest of the server never sees the old shape - and
    ///     tell whoever can ask the user that there is something to persist.
    /// </remarks>
    private JsonObject BringForward(
        JsonObject document, string? declaredVersion, string path, string fileName)
    {
        // No version and no initial migration means we cannot say which shape it holds, so there is
        // nothing to bring forward - it is read as it is, which for a project file is version one.
        if (declaredVersion is null
            && !DocumentMigrator.HasInitialMigration(
                PgprojFormat.TypeName, PgprojFormat.Current, _migrations))
            return document;

        var from = declaredVersion is null
            ? TypeVersion.Zero(PgprojFormat.Current.Namespace)
            : TypeVersion.TryParse(declaredVersion, out var parsed)
                ? parsed
                : PgprojFormat.Current;

        if (from.CompareTo(PgprojFormat.Current) >= 0) return document;

        var migration = DocumentMigrator.Run(
            document, PgprojFormat.TypeName, from, PgprojFormat.Current, _migrations, _messages);

        // A gap in the chain is a bug in our registration, and this is the file everything else is
        // read from - reading it at a version whose shape it does not have would paper over it.
        if (migration.Document is not JsonObject migrated)
            throw new ModProjectLoadException(
                _messages.Format("ModProject_CouldNotLoadModProject2", fileName, migration.Failure));

        migrated["_type"] = PgprojFormat.TypeName;
        migrated["_typeVersion"] = PgprojFormat.Current.ToString();

        // The diff is the one moment the author is already being asked to change this file, so a
        // project with icon sources it has no way to reach is offered the node here rather than
        // finding out later that every icon came from the base game. Outside the migration chain
        // because it needs the PATH: a migration step sees only the tree, so it could not tell
        // whether the folder it would name is really there. See PgprojIconProposal.
        var notices = migration.Notices;
        var directory = _fileHelper.FileSystem.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)
            && PgprojIconProposal.TryPropose(migrated, directory, _fileHelper.FileSystem, _messages) is { } icons)
            notices = [.. notices, icons];

        _migrationSink.Migrated(path, migrated, notices);
        return migrated;
    }

    // Translates System.Text.Json's cryptic deserialization failures into a clear, user-facing
    // message: which file, where in it, and how to fix it. Shown verbatim as an editor notification.
    private string BuildLoadErrorMessage(string fileName, JsonException ex)
    {
        var location = ex.LineNumber is { } line
            ? ex.BytePositionInLine is { } col
                ? _messages.Format("ModProject_AtLineColumn", line + 1, col + 1)
                : _messages.Format("ModProject_AtLine", line + 1)
            : string.Empty;

        var hint = ex.Path is { } jsonPath
                   && jsonPath.Contains("projectReferences", StringComparison.OrdinalIgnoreCase)
            ? _messages.Format("ModProject_ProjectReferencesMustBeArrayReferences")
            : _messages.Format("ModProject_FileNotValidJSONDoes");

        return _messages.Format("ModProject_CouldNotLoadModProject", fileName, location, hint);
    }

    private sealed class ModProjectFileDto
    {
        /// <summary>The document's identity - see <see cref="PgprojFormat" />. Not the mod's version.</summary>
        [JsonPropertyName("_type")]
        public string? Type { get; init; }

        [JsonPropertyName("_typeVersion")] public string? TypeVersion { get; init; }

        public string? Name { get; init; }
        public JsonElement? Modinfo { get; init; }
        public DirectoryMapDto? Directories { get; init; }
        public List<ProjectReferenceDto>? ProjectReferences { get; init; }
        public LocalisationDto? Localisation { get; init; }
        public IconsDto? Icons { get; init; }
    }

    // The shapes below are filled by System.Text.Json; their setters have no caller in the code.
    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private sealed class IconsDto
    {
        public string? MegaTexture { get; init; }
        public IReadOnlyList<string>? SourceRoots { get; init; }
    }

    private sealed class DirectoryMapDto
    {
        public IReadOnlyList<string>? Xml { get; init; }
        public IReadOnlyList<string>? Scripts { get; init; }
        public IReadOnlyList<string>? Art { get; init; }
        public IReadOnlyList<string>? Audio { get; init; }
        public IReadOnlyList<string>? Ai { get; init; }
        public IReadOnlyList<string>? StoryDialog { get; init; }
    }

    private sealed class ProjectReferenceDto
    {
        public string? Path { get; init; }
    }

    private sealed class LocalisationDto
    {
        public string? Type { get; init; }
        public string? Directory { get; init; }
        public CreditsDto? Credits { get; init; }
    }

    private sealed class CreditsDto
    {
        public string? Detection { get; init; }
        public IReadOnlyList<string>? Files { get; init; }
    }
}