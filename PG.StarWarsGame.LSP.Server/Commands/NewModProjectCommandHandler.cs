// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Commands;

public sealed class NewModProjectCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.newModProject";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<NewModProjectCommandHandler> _logger;

    public NewModProjectCommandHandler(IFileHelper fileHelper, ILogger<NewModProjectCommandHandler> logger)
    {
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public override Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        if (request.Arguments?.FirstOrDefault() is not JObject args)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.newModProject invoked without arguments; nothing created.");
            return Unit.Task;
        }

        var name = args.Value<string>("name");
        var path = args.Value<string>("path");

        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("aet-eaw-edit.lsp.newModProject invoked without a mod name; nothing created.");
            return Unit.Task;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("aet-eaw-edit.lsp.newModProject invoked without a target path; nothing created.");
            return Unit.Task;
        }

        var fs = _fileHelper.FileSystem;
        var sanitized = SanitizeModName(name);
        var pgprojPath = fs.Path.Combine(path, sanitized + ".pgproj");

        if (fs.File.Exists(pgprojPath))
        {
            _logger.LogWarning(
                "aet-eaw-edit.lsp.newModProject: project file '{Path}' already exists; nothing created.", pgprojPath);
            return Unit.Task;
        }

        CreateDirectories(path);
        WritePgproj(pgprojPath, name);
        WriteModinfo(fs.Path.Combine(path, "modinfo.json"), name);

        _logger.LogInformation("aet-eaw-edit.lsp.newModProject: created mod project '{Name}' at '{Path}'.", name, path);
        return Unit.Task;
    }

    private void CreateDirectories(string root)
    {
        var fs = _fileHelper.FileSystem;
        string[] relativeDirs =
        [
            "Data/XML",
            "Data/Scripts",
            "Data/Scripts/Story",
            "Data/Scripts/Library",
            "Data/Art",
            "Data/Art/Models",
            "Data/Art/Textures",
            "Data/Audio",
            "Data/Audio/Music",
            "Data/Audio/SFX",
            "Data/Text"
        ];

        fs.Directory.CreateDirectory(root);
        foreach (var rel in relativeDirs)
            fs.Directory.CreateDirectory(fs.Path.Combine(root, rel));
    }

    private void WritePgproj(string pgprojPath, string name)
    {
        var content = new
        {
            modinfo = new
            {
                name,
                version = "1.0.0"
            },
            directories = new
            {
                xml = new[] { "data/xml" },
                scripts = new[] { "data/scripts" },
                art = new[] { "data/art" },
                audio = new[] { "data/audio" },
                text = new[] { "data/text" }
            },
            projectReferences = Array.Empty<object>()
        };

        _fileHelper.FileSystem.File.WriteAllText(
            pgprojPath, JsonSerializer.Serialize(content, JsonOptions));
    }

    private void WriteModinfo(string modinfoPath, string name)
    {
        var content = new
        {
            name,
            version = "1.0.0"
        };

        _fileHelper.FileSystem.File.WriteAllText(
            modinfoPath, JsonSerializer.Serialize(content, JsonOptions));
    }

    private static string SanitizeModName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
            if (char.IsWhiteSpace(ch))
                builder.Append('_');
            else if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                builder.Append(ch);

        return builder.ToString();
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}