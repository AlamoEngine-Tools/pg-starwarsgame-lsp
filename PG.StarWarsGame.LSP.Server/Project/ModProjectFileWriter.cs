// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Project;

// Patches the "localisation" node of an existing .pgproj file in place, preserving every other
// property (modinfo, directories, projectReferences, ...). Round-trips through JsonNode rather
// than hand-building JSON text - note this means // and /* */ comments in the original file are
// NOT preserved: System.Text.Json discards them while parsing, and JsonNode has no concept of
// them to write back out.
public sealed class ModProjectFileWriter : IModProjectFileWriter
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private const string TypeField = "_type";
    private const string VersionField = "_typeVersion";

    private readonly IFileHelper _fileHelper;

    public ModProjectFileWriter(IFileHelper fileHelper)
    {
        _fileHelper = fileHelper;
    }

    /// <summary>
    ///     Gives the document its identity, first in the file, and keeps it current.
    /// </summary>
    /// <remarks>
    ///     A file we are already rewriting should say what it is: the fields spread as projects are
    ///     touched, without us going near a project the user has not asked us to change. The version
    ///     is set to what this build writes, which is the truth of what just happened - by the time
    ///     a write runs, the loader has already refused anything newer than that.
    /// </remarks>
    private static void Stamp(JsonObject root)
    {
        // Rebuilt so the two fields lead the file rather than trailing whatever was there. JsonNode
        // keeps insertion order, and a project file is read by people.
        var ordered = new JsonObject
        {
            [TypeField] = PgprojFormat.TypeName,
            [VersionField] = PgprojFormat.Current.ToString()
        };

        foreach (var property in root.ToList())
        {
            root.Remove(property.Key);
            if (property.Key is TypeField or VersionField) continue;
            ordered[property.Key] = property.Value;
        }

        foreach (var property in ordered.ToList())
        {
            ordered.Remove(property.Key);
            root[property.Key] = property.Value;
        }
    }

    public async Task SetLocalisationAsync(
        string pgprojPath, string type, string directory, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var text = await fs.File.ReadAllTextAsync(pgprojPath, ct);
        var root = JsonNode.Parse(text, documentOptions: ParseOptions)?.AsObject()
                   ?? throw new InvalidOperationException($"'{pgprojPath}' is not a valid JSON object.");

        // Written into the existing node rather than over it. Replacing it wholesale discarded any
        // 'credits' settings the user had hand-written - nothing in the server ever writes that node,
        // so an import or a format conversion silently undid the only way to configure it.
        var localisation = root["localisation"]?.AsObject();
        if (localisation is null)
        {
            localisation = new JsonObject();
            root["localisation"] = localisation;
        }

        localisation["type"] = type;
        localisation["directory"] = directory;

        Stamp(root);

        await fs.File.WriteAllTextAsync(pgprojPath, root.ToJsonString(WriteOptions), ct);
    }
}