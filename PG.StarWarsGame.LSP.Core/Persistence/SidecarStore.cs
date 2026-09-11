// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Persistence;

/// <summary>
///     How every sidecar is written: camelCase and indented, matching the files already on disk.
///     Non-generic on purpose - one instance for all documents rather than one per closed
///     <see cref="SidecarStore{T}" />.
/// </summary>
internal static class SidecarJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

/// <summary>
///     A versioned JSON document under the project's <c>.aetswg/</c> directory: read through its
///     migration chain, written back with its envelope, cached in memory, and degrading to
///     memory-only when there is no project to hold a file.
///     <para>
///         Every sidecar hand-rolled its own copy of "cache it, degrade without a .pgproj, treat a
///         corrupt file as defaults". This owns that once, and adds the part none of them had: a
///         document that says which revision of its own shape it holds.
///     </para>
/// </summary>
/// <typeparam name="T">
///     The document. Serializes to a JSON OBJECT - the envelope fields sit beside its properties, so
///     a payload that is naturally a list needs a wrapper with the list under a named property.
/// </typeparam>
public sealed class SidecarStore<T> where T : class
{
    private const string TypeField = "_type";
    private const string VersionField = "_typeVersion";

    private readonly Func<T> _createDefault;
    private readonly TypeVersion _current;
    private readonly IFileHelper _fileHelper;
    private readonly string _fileName;
    private readonly object _gate = new();
    private readonly ISidecarLocator _locator;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<IDocumentMigration> _migrations;
    private readonly string _typeName;

    private SidecarLoad<T>? _cache;

    /// <summary>Set when a load refused a newer file. Blocks every write for the session - see TrySave.</summary>
    private bool _writesBlocked;

    public SidecarStore(
        string fileName,
        string typeName,
        TypeVersion current,
        IReadOnlyList<IDocumentMigration> migrations,
        ISidecarLocator locator,
        IFileHelper fileHelper,
        ILogger logger,
        Func<T> createDefault)
    {
        _fileName = fileName;
        _typeName = typeName;
        _current = current;
        _migrations = migrations;
        _locator = locator;
        _fileHelper = fileHelper;
        _logger = logger;
        _createDefault = createDefault;
    }

    /// <summary>Reads the document, migrating it forward if it is behind. Never throws.</summary>
    public SidecarLoad<T> Load()
    {
        lock (_gate)
        {
            return _cache ??= ReadLocked();
        }
    }

    /// <summary>
    ///     Writes the document, stamped with its type and current version.
    ///     <para>
    ///         Refuses while a newer file is on disk. That refusal is the other half of refusing to
    ///         READ one: without it an older build reads a newer file, fails, falls back to
    ///         defaults, and saves those defaults over it - the one path in this design that
    ///         destroys data rather than declining to touch it.
    ///     </para>
    /// </summary>
    public bool TrySave(T value, out string? error)
    {
        lock (_gate)
        {
            if (_writesBlocked)
            {
                error = $"'{_fileName}' was written by a newer version of aet-eaw-edit and is left "
                        + "untouched. Update the extension to edit this project.";
                return false;
            }

            _cache = new SidecarLoad<T>(value, SidecarStatus.Loaded, null, []);

            var path = _locator.TryLocate(_fileName);
            if (path is null) return Ok(out error); // no project: the session keeps it, disk never sees it

            try
            {
                WriteLocked(path, value, _current);
                return Ok(out error);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist '{File}' - the change stays in memory", _fileName);
                error = $"Could not write '{_fileName}': {ex.Message}";
                return false;
            }
        }

        static bool Ok(out string? error)
        {
            error = null;
            return true;
        }
    }

    private SidecarLoad<T> ReadLocked()
    {
        var path = _locator.TryLocate(_fileName);
        if (path is null || !_fileHelper.FileSystem.File.Exists(path)) return Defaulted(null);

        JsonNode document;
        try
        {
            document = JsonNode.Parse(_fileHelper.FileSystem.File.ReadAllText(path))
                       ?? throw new JsonException("empty");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "'{File}' is unreadable - continuing with defaults", _fileName);
            return Defaulted($"'{_fileName}' could not be read and was left alone. Defaults are in use.");
        }

        // A document that is not an object carries no envelope and cannot: both sidecars that
        // exist today are bare collections. It is version zero, and its first migration is what
        // gives it a shape that can hold one.
        var envelope = document as JsonObject;

        // A file holding some OTHER document is not this one at an odd version. Reading it anyway
        // would silently adopt whatever fields happened to match.
        var declaredType = (string?)envelope?[TypeField];
        if (declaredType is not null && !string.Equals(declaredType, _typeName, StringComparison.Ordinal))
            return Defaulted($"'{_fileName}' holds '{declaredType}', not '{_typeName}'. Defaults are in use.");

        var rawVersion = (string?)envelope?[VersionField];
        TypeVersion version;
        if (rawVersion is null)
        {
            // A document carrying no identity is NOT a document at version zero waiting to be
            // migrated. It is read as one only during the INITIAL typing of this document - the
            // window in which a migration from zero exists, because that migration is precisely
            // what an unversioned file is for. Once that handler retires, a file with no identity
            // is one we cannot place, and adopting it would mean guessing which shape it holds.
            if (!DocumentMigrator.HasInitialMigration(_typeName, _current, _migrations))
                return Defaulted(
                    $"'{_fileName}' declares no '{TypeField}' or '{VersionField}', so there is no way "
                    + "to tell which shape it holds. It was left alone and defaults are in use.");

            version = TypeVersion.Zero(_current.Namespace);
        }
        else if (!TypeVersion.TryParse(rawVersion, out version))
        {
            return Defaulted($"'{_fileName}' declares an unreadable version '{rawVersion}'. Defaults are in use.");
        }

        if (!string.Equals(version.Namespace, _current.Namespace, StringComparison.Ordinal))
            return Defaulted(
                $"'{_fileName}' declares version '{version}', which is not a version of "
                + $"'{_current.Namespace}'. Defaults are in use.");

        var order = version.CompareTo(_current);
        if (order > 0)
        {
            // Enforced upgrading: a document from the future cannot be migrated backwards, and
            // guessing at it would be a downgrade dressed up as a read.
            _writesBlocked = true;
            return new SidecarLoad<T>(_createDefault(), SidecarStatus.RefusedNewer,
                $"'{_fileName}' is version {version.Version}, which this version of aet-eaw-edit does "
                + $"not understand (it writes {_current.Version}). Update the extension to open this "
                + "project. The file has been left untouched.", []);
        }

        IReadOnlyList<string> notices = [];
        if (order < 0)
        {
            var migration = DocumentMigrator.Run(document, _typeName, version, _current, _migrations);
            if (migration.Document is null)
                return Defaulted(
                    $"'{_fileName}' could not be brought forward: {migration.Failure}. Defaults are in "
                    + "use and the file was left alone.");

            document = migration.Document;
            notices = migration.Notices;
        }

        // Whatever it started as, a migrated document has to end up as an object: that is what
        // carries the envelope and what T deserializes from.
        if (document is not JsonObject migrated)
            return Defaulted(
                $"'{_fileName}' is not a JSON object and no migration made it one. Defaults are in use.");

        T? value;
        try
        {
            migrated.Remove(TypeField);
            migrated.Remove(VersionField);
            value = migrated.Deserialize<T>(SidecarJson.Options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "'{File}' does not match {Type} - continuing with defaults", _fileName, typeof(T));
            return Defaulted($"'{_fileName}' does not match the shape of '{_typeName}'. Defaults are in use.");
        }

        if (value is null) return Defaulted($"'{_fileName}' is empty. Defaults are in use.");
        if (order == 0) return new SidecarLoad<T>(value, SidecarStatus.Loaded, null, []);

        // Migrated: persist the new shape now, so the next read is a plain load and the chain does
        // not have to keep working for a version nobody writes any more.
        try
        {
            WriteLocked(path, value, _current);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Migrated '{File}' could not be written back", _fileName);
        }

        return new SidecarLoad<T>(value, SidecarStatus.Migrated, null, notices);
    }

    private void WriteLocked(string path, T value, TypeVersion version)
    {
        var fs = _fileHelper.FileSystem;
        var node = JsonSerializer.SerializeToNode(value, SidecarJson.Options)?.AsObject()
                   ?? throw new JsonException($"{typeof(T).Name} did not serialize to a JSON object");

        // Envelope first, so a person opening the file reads what it is before what it holds.
        var stamped = new JsonObject { [TypeField] = _typeName, [VersionField] = version.ToString() };
        foreach (var property in node.ToList())
        {
            node.Remove(property.Key);
            stamped[property.Key] = property.Value;
        }

        var directory = fs.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) fs.Directory.CreateDirectory(directory);

        // Written through a temp file, as the index cache is: a half-written sidecar reads as
        // damage, and damage means defaults, which is how a layout would be lost to a crash.
        var temp = path + ".tmp";
        fs.File.WriteAllText(temp, stamped.ToJsonString(SidecarJson.Options));
        if (fs.File.Exists(path)) fs.File.Delete(path);
        fs.File.Move(temp, path);
    }

    private SidecarLoad<T> Defaulted(string? message)
    {
        return new SidecarLoad<T>(_createDefault(), SidecarStatus.Defaulted, message, []);
    }
}
