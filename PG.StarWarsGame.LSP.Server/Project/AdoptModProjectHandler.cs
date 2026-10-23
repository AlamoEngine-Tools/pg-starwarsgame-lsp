// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
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

public sealed class AdoptModProjectHandler(IFileHelper fileHelper, IMessageCatalog messages)
    : IJsonRpcRequestHandler<AdoptModProjectParams, AdoptModProjectResult>
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // The folders under Data the game reads; a mod holds at least one of them. The client's offer
    // takes a folder by the same rule (modLayout.ts), so a folder it offers is one this writes.
    private static readonly string[] ModDataFolders = ["XML", "Scripts", "Art", "Audio", "Text"];

    // The executables the game runs from; the folder holding one is the game, not a mod.
    private static readonly string[] GameExecutables = ["sweaw.exe", "swfoc.exe", "starwarsg.exe", "starwarsi.exe"];

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public Task<AdoptModProjectResult> Handle(AdoptModProjectParams request, CancellationToken cancellationToken)
    {
        var fs = fileHelper.FileSystem;
        var root = request.Directory;

        if (fs.Directory.Exists(root) && fs.Directory.GetFiles(root, "*.pgproj").FirstOrDefault() is { } existing)
            return Task.FromResult(Refused(messages.Format("AdoptModProject_ProjectExists", existing)));

        if (IsGame(root))
            return Task.FromResult(Refused(messages.Format("AdoptModProject_GameFolder", root)));

        var data = Child(root, "Data");
        var modinfo = fs.Path.Combine(root, "modinfo.json");
        if (!HoldsModData(data) && !fs.File.Exists(modinfo))
            return Task.FromResult(Refused(messages.Format("AdoptModProject_NotAMod", root)));

        var name = ModinfoName(modinfo) ?? fs.Path.GetFileName(root.TrimEnd('/', '\\'));
        var directories = new JsonObject();
        if (data is not null && Child(data, "XML") is { } xml) directories["xml"] = Relative(root, xml);
        if (data is not null && Child(data, "Scripts") is { } scripts)
        {
            directories["scripts"] = Relative(root, scripts);
            if (Child(scripts, "Story") is { } story && fs.Directory.GetFiles(story, "*.txt").Length > 0)
                directories["storyDialog"] = Relative(root, story);
        }

        if (data is not null && Child(data, "Art") is { } art) directories["art"] = Relative(root, art);
        if (data is not null && Child(data, "Audio") is { } audio) directories["audio"] = Relative(root, audio);

        // A project we write says what it is and which format it is in, first.
        var project = new JsonObject
        {
            ["_type"] = PgprojFormat.TypeName,
            ["_typeVersion"] = PgprojFormat.Current.ToString(),
            ["name"] = name,
            ["directories"] = directories
        };
        if (data is not null && Child(data, "Text") is { } text && TextFormat(text) is { } type)
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

    // Whether a Data folder holds one of the game's folders or a MEG archive.
    private bool HoldsModData(string? data)
    {
        if (data is null) return false;
        var fs = fileHelper.FileSystem;
        return ModDataFolders.Any(f => Child(data, f) is not null)
               || fs.Directory.GetFiles(data).Any(f => f.EndsWith(".meg", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsGame(string root)
    {
        var fs = fileHelper.FileSystem;
        return fs.Directory.Exists(root) && fs.Directory.GetFiles(root)
            .Any(f => GameExecutables.Contains(fs.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
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
        return [fileHelper.FileSystem.Path.GetRelativePath(root, directory).Replace('\\', '/')];
    }

    // The csv is what a mod compiles its dat from, so with both the csv is the source.
    private string? TextFormat(string text)
    {
        var fs = fileHelper.FileSystem;
        if (fs.Directory.GetFiles(text, "*.csv").Length > 0) return "CSV";
        return fs.Directory.GetFiles(text, "*.dat").Length > 0 ? "DAT" : null;
    }

    private string? ModinfoName(string modinfo)
    {
        var fs = fileHelper.FileSystem;
        if (!fs.File.Exists(modinfo)) return null;
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