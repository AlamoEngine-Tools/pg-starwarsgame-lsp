// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Indexing;

/// <summary>
///     The dynamic enum catalog: the values the workspace's enum files declare, with where each is
///     declared, published on the game index.
/// </summary>
public sealed class DynamicEnumCatalogBuilder
{
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<DynamicEnumCatalogBuilder> _logger;
    private readonly ISchemaProvider _schema;

    public DynamicEnumCatalogBuilder(IFileHelper fileHelper, IGameIndexService indexService, ISchemaProvider schema,
        ILogger<DynamicEnumCatalogBuilder> logger)
    {
        _fileHelper = fileHelper;
        _indexService = indexService;
        _schema = schema;
        _logger = logger;
    }

    /// <summary>
    ///     Scans the workspace XML directories for every <see cref="EnumKind.DynamicXml" /> enum file
    ///     declared in the schema, parses it with <see cref="DynamicEnumExtractor" />, and publishes
    ///     the merged result as <see cref="IGameIndexService.ApplyWorkspaceDynamicEnumValues" />.
    ///     Only files that actually exist in the workspace are included; missing files are silently
    ///     skipped so dependency-only enums degrade gracefully to baseline-only validation.
    /// </summary>
    public void Apply(IReadOnlyList<string> xmlRoots)
    {
        var sep = _fileHelper.FileSystem.Path.DirectorySeparatorChar;

        // Filename-only search across every xml root, recursing into subdirectories. Collect the
        // wanted filenames up front and walk each root ONCE, instead of one recursive directory
        // walk per enum source file per root. Roots are iterated in reverse so the highest-rank
        // (root project) directory wins; config.XmlDirectories is deps-first / root-last (see
        // ModProjectResolver.Resolve).
        var wantedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumDef in _schema.AllEnums)
        {
            if (enumDef.Kind != EnumKind.DynamicXml || string.IsNullOrEmpty(enumDef.SourceFile))
                continue;
            var anchorIdx = enumDef.SourceFile.IndexOf('$');
            var filePath = anchorIdx >= 0 ? enumDef.SourceFile[..anchorIdx] : enumDef.SourceFile;
            wantedFileNames.Add(_fileHelper.FileSystem.Path.GetFileName(filePath.Replace('/', sep)));
        }

        var diskPathByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (wantedFileNames.Count > 0)
            foreach (var xmlRoot in xmlRoots.AsEnumerable().Reverse())
            {
                if (!_fileHelper.FileSystem.Directory.Exists(xmlRoot)) continue;
                foreach (var file in _fileHelper.FileSystem.Directory
                             .EnumerateFiles(xmlRoot, "*", SearchOption.AllDirectories))
                {
                    var name = _fileHelper.FileSystem.Path.GetFileName(file);
                    if (wantedFileNames.Contains(name))
                        diskPathByFileName.TryAdd(name, file);
                }
            }

        // Keyed by logical file path (anchor stripped). Caches (diskPath, content) so the file is
        // read once even when multiple enums share the same source file (e.g. gameconstants.xml).
        var resolvedFiles = new Dictionary<string, (string DiskPath, string Content)>(
            StringComparer.OrdinalIgnoreCase);

        var (workspace, _) = DynamicEnumExtractor.Extract(_schema, FileReader);

        // Build per-value file locations for both plain-file and $anchor format enums.
        var defsBuilder =
            ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, FileOrigin>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var enumDef in _schema.AllEnums)
        {
            if (enumDef.Kind != EnumKind.DynamicXml || string.IsNullOrEmpty(enumDef.SourceFile))
                continue;

            var sourceFile = enumDef.SourceFile;
            var anchorIdx = sourceFile.IndexOf('$');
            var filePath = anchorIdx >= 0 ? sourceFile[..anchorIdx] : sourceFile;

            if (!resolvedFiles.TryGetValue(filePath, out var resolved))
                continue;

            var fileUri = _fileHelper.PathToFileUri(resolved.DiskPath);

            var locations = anchorIdx >= 0
                ? DynamicEnumExtractor.ParseElementTextWithLocations(
                    resolved.Content, sourceFile[(anchorIdx + 1)..], fileUri)
                : DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations(
                    resolved.Content, fileUri);

            if (locations.Count == 0) continue;

            var inner = ImmutableDictionary.CreateBuilder<string, FileOrigin>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, origin) in locations)
                inner[name] = origin;

            defsBuilder[enumDef.Name] = inner.ToImmutable();
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Dynamic enum catalog: {EnumCount} enum(s) with {ValueCount} workspace value(s) applied",
                workspace.Count, workspace.Values.Sum(v => v.Length));

        _indexService.ApplyWorkspaceDynamicEnumValues(workspace);
        _indexService.ApplyWorkspaceEnumValueDefinitions(defsBuilder.ToImmutable());
        return;

        string? FileReader(string sourceFile)
        {
            var filePath = sourceFile.Contains('$') ? sourceFile[..sourceFile.IndexOf('$')] : sourceFile;

            if (resolvedFiles.TryGetValue(filePath, out var cached))
                return cached.Content;

            var fileName = _fileHelper.FileSystem.Path.GetFileName(filePath.Replace('/', sep));
            if (!diskPathByFileName.TryGetValue(fileName, out var match))
                return null;

            try
            {
                var content = _fileHelper.FileSystem.File.ReadAllText(match);
                resolvedFiles[filePath] = (match, content);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read enum file '{Path}'", match);
                return null;
            }
        }
    }
}