// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Commands;

namespace PG.StarWarsGame.LSP.Server.Project;

// ── aet/adoptModProject ──────────────────────────────────────────────────────
//
// A project file for a mod that has none (#295), written from what the folder holds. New Mod
// Project builds a mod; this one describes a mod that exists: it creates no directory, and the
// mod's modinfo.json is read for its name and never written.

[Method("aet/adoptModProject", Direction.ClientToServer)]
public sealed record AdoptModProjectParams(string Directory) : IRequest<AdoptModProjectResult>;

/// <param name="ProjectFile">The written file, or null when nothing was written.</param>
/// <param name="Error">Why nothing was written.</param>
public sealed record AdoptModProjectResult(string? ProjectFile, string? Error);

public sealed class AdoptModProjectHandler(IFileHelper fileHelper, ModLayout layout, IMessageCatalog messages)
    : IJsonRpcRequestHandler<AdoptModProjectParams, AdoptModProjectResult>
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public Task<AdoptModProjectResult> Handle(AdoptModProjectParams request, CancellationToken cancellationToken)
    {
        var fs = fileHelper.FileSystem;
        var root = request.Directory;
        // One reading of the folder answers every question below; the client's offer took the
        // folder by the same rule (modLayout.ts), so a folder it offers is one this writes.
        var folder = layout.Inspect(root);

        if (folder.ProjectFile is { } existing)
            return Task.FromResult(Refused(messages.Format("AdoptModProject_ProjectExists", existing)));

        if (folder.IsGame)
            return Task.FromResult(Refused(messages.Format("AdoptModProject_GameFolder", root)));

        if (!folder.IsMod)
            return Task.FromResult(Refused(messages.Format("AdoptModProject_NotAMod", root)));

        var name = ModinfoName(folder.Modinfo) ?? fs.Path.GetFileName(root.TrimEnd('/', '\\'));
        var directories = new JsonObject();
        if (folder.DataChild("XML") is { } xml) directories["xml"] = Relative(root, xml);
        if (folder.DataChild("Scripts") is { } scripts)
        {
            directories["scripts"] = Relative(root, scripts);
            if (Child(scripts, "Story") is { } story && fs.Directory.GetFiles(story, "*.txt").Length > 0)
                directories["storyDialog"] = Relative(root, story);
        }

        if (folder.DataChild("Art") is { } art) directories["art"] = Relative(root, art);
        if (folder.DataChild("Audio") is { } audio) directories["audio"] = Relative(root, audio);

        // A project we write says what it is and which format it is in, first.
        var project = new JsonObject
        {
            ["_type"] = PgprojFormat.TypeName,
            ["_typeVersion"] = PgprojFormat.Current.ToString(),
            ["name"] = name,
            ["directories"] = directories
        };
        if (folder.DataChild("Text") is { } text && TextFormat(text) is { } type)
            project["localisation"] = new JsonObject
                { ["type"] = type, ["directory"] = Relative(root, text)[0]!.GetValue<string>() };
        project["projectReferences"] = new JsonArray();

        var path = fs.Path.Combine(root, NewModProjectCommandHandler.SanitizeModName(name) + ".pgproj");
        fs.File.WriteAllText(path, project.ToJsonString(WriteOptions));
        return Task.FromResult(new AdoptModProjectResult(path, null));
    }

    private static AdoptModProjectResult Refused(string error)
    {
        return new AdoptModProjectResult(null, error);
    }

    // A subdirectory by name in any case, as the game reads it, spelled as it is on disk.
    private string? Child(string parent, string name)
    {
        var fs = fileHelper.FileSystem;
        if (!fs.Directory.Exists(parent)) return null;
        return fs.Directory.GetDirectories(parent)
            .FirstOrDefault(d => string.Equals(fs.Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }

    private JsonArray Relative(string root, string directory)
    {
        return [GamePaths.Forward(fileHelper.FileSystem.Path.GetRelativePath(root, directory))];
    }

    // The csv is what a mod compiles its dat from, so with both the csv is the source.
    private string? TextFormat(string text)
    {
        var fs = fileHelper.FileSystem;
        if (fs.Directory.GetFiles(text, "*.csv").Length > 0) return "CSV";
        return fs.Directory.GetFiles(text, "*.dat").Length > 0 ? "DAT" : null;
    }

    private string? ModinfoName(string? modinfo)
    {
        var fs = fileHelper.FileSystem;
        if (modinfo is null) return null;
        try
        {
            return JsonNode.Parse(fs.File.ReadAllText(modinfo), documentOptions: ReadOptions)?["name"]
                ?.GetValue<string>() is { Length: > 0 } name
                ? name
                : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}