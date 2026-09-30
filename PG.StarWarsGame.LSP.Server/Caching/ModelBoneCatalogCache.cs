// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Caching;

/// <inheritdoc cref="IModelBoneCatalogCache" />
public sealed class ModelBoneCatalogCache : IModelBoneCatalogCache
{
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<ModelBoneCatalogCache> _logger;

    public ModelBoneCatalogCache(IFileHelper fileHelper, ILogger<ModelBoneCatalogCache> logger)
    {
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, ModelCatalogEntry>? TryLoad(string pgprojPath, string fingerprint)
    {
        var path = ProjectIndexLocator.GetModelBonesFilePath(pgprojPath);
        if (!_fileHelper.FileSystem.File.Exists(path))
            return null;

        ModelBoneCatalogSnapshot? snapshot;
        try
        {
            snapshot = ModelBoneCatalogSerializer.Deserialize(_fileHelper.FileSystem.File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read model bone snapshot from '{Path}'", path);
            return null;
        }

        if (snapshot is null)
        {
            _logger.LogDebug("Model bone snapshot at '{Path}' is stale or corrupt; will re-extract", path);
            return null;
        }

        if (!string.Equals(snapshot.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            _logger.LogDebug("Model bone snapshot at '{Path}' is for a different model set; will re-extract",
                path);
            return null;
        }

        // Rebuilt case-insensitively: every other consumer spells a model key the way the XML does,
        // and MessagePack round-trips the entries, not the comparer they were collected under.
        var models = new Dictionary<string, ModelCatalogEntry>(
            snapshot.Models.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var model in snapshot.Models)
            models[model.ModelKey] = new ModelCatalogEntry(model.Bones, model.Textures);

        _logger.LogDebug("Reused model snapshot from '{Path}' ({Models} model(s))", path, models.Count);
        return models;
    }

    public void Save(
        string pgprojPath, string fingerprint, IReadOnlyDictionary<string, ModelCatalogEntry> models)
    {
        var path = ProjectIndexLocator.GetModelBonesFilePath(pgprojPath);

        try
        {
            var dir = _fileHelper.FileSystem.Path.GetDirectoryName(path)!;
            _fileHelper.FileSystem.Directory.CreateDirectory(dir);

            var snapshot = new ModelBoneCatalogSnapshot
            {
                SchemaVersion = ModelBoneCatalogSnapshot.CurrentSchemaVersion,
                Fingerprint = fingerprint,
                Models = models
                    .Select(kv => new SerializedModelBones
                    {
                        ModelKey = kv.Key, Bones = kv.Value.Bones, Textures = kv.Value.Textures
                    })
                    .ToArray()
            };

            // Atomic write: serialize to a temp file then rename over the target, so a start that
            // dies mid-write leaves the previous snapshot rather than a truncated one.
            var tempPath = path + ".tmp";
            _fileHelper.FileSystem.File.WriteAllBytes(tempPath, ModelBoneCatalogSerializer.Serialize(snapshot));
            if (_fileHelper.FileSystem.File.Exists(path))
                _fileHelper.FileSystem.File.Delete(path);
            _fileHelper.FileSystem.File.Move(tempPath, path);

            _logger.LogDebug("Saved model snapshot to '{Path}' ({Models} model(s))", path, models.Count);
        }
        catch (Exception ex)
        {
            // A cache that cannot be written is a performance loss, never a correctness one - the
            // catalog in memory is already correct. A read-only or full disk must not fail startup.
            _logger.LogWarning(ex, "Failed to save model bone snapshot to '{Path}'", path);
        }
    }
}