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
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Commands;

public sealed class NewModProjectCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.newModProject";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IFileHelper _fileHelper;
    private readonly ModLayout _layout;
    private readonly ILogger<NewModProjectCommandHandler> _logger;
    private readonly IMessageCatalog _messages;
    private readonly IUserNotifier _notifier;

    public NewModProjectCommandHandler(IFileHelper fileHelper, ModLayout layout, IUserNotifier notifier,
        IMessageCatalog messages, ILogger<NewModProjectCommandHandler> logger)
    {
        _fileHelper = fileHelper;
        _layout = layout;
        _notifier = notifier;
        _messages = messages;
        _logger = logger;
    }

    public override Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        // Every refusal is shown: the client announces nothing of its own, so a logged warning
        // and a silent return read as success.
        if (request.Arguments?.FirstOrDefault() is not JObject args)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.newModProject invoked without arguments; nothing created");
            _notifier.ShowError(_messages.Format("NewModProject_NameRequired"));
            return Unit.Task;
        }

        var name = args.Value<string>("name");
        var path = args.Value<string>("path");

        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("aet-eaw-edit.lsp.newModProject invoked without a mod name; nothing created");
            _notifier.ShowError(_messages.Format("NewModProject_NameRequired"));
            return Unit.Task;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("aet-eaw-edit.lsp.newModProject invoked without a target path; nothing created");
            _notifier.ShowError(_messages.Format("NewModProject_PathRequired"));
            return Unit.Task;
        }

        var fs = _fileHelper.FileSystem;
        var sanitized = SanitizeModName(name);
        var pgprojPath = fs.Path.Combine(path, sanitized + ".pgproj");

        // The same rule as Adopt Project: any project file, whatever its name, and the game's own
        // folder refuse the command - a second project file beside a differently named one, or a
        // project file written into the game's install, is not a mod project.
        var folder = _layout.Inspect(path);
        if (folder.ProjectFile is { } existing)
        {
            _logger.LogWarning(
                "aet-eaw-edit.lsp.newModProject: project file '{Path}' already exists; nothing created", existing);
            _notifier.ShowError(_messages.Format("NewModProject_ProjectExists", existing));
            return Unit.Task;
        }

        if (folder.IsGame)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.newModProject: '{Path}' is the game's folder; nothing created", path);
            _notifier.ShowError(_messages.Format("NewModProject_GameFolder", path));
            return Unit.Task;
        }

        CreateDirectories(path);
        WritePgproj(pgprojPath, name);
        // An existing mod's modinfo.json - its dependencies included - is the mod's, not ours to
        // replace; only a folder without one gets a new one.
        if (folder.Modinfo is null)
            WriteModinfo(fs.Path.Combine(path, ModLayout.ModinfoFileName), name);

        _logger.LogInformation("aet-eaw-edit.lsp.newModProject: created mod project '{Name}' at '{Path}'", name, path);
        _notifier.ShowInfo(_messages.Format("NewModProject_Created", name, pgprojPath));
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
        var content = new Dictionary<string, object?>
        {
            // A project we write says what it is and which format it is in, first, like every other
            // document here. Absent still means version one, so this is not what makes the file
            // loadable - it is what lets the next format change tell a stamped file from an
            // unstamped one instead of guessing.
            ["_type"] = PgprojFormat.TypeName,
            ["_typeVersion"] = PgprojFormat.Current.ToString(),
            ["modinfo"] = new
            {
                name,
                version = "1.0.0"
            },
            ["directories"] = new
            {
                xml = new[] { "data/xml" },
                scripts = new[] { "data/scripts" },
                art = new[] { "data/art" },
                audio = new[] { "data/audio" }
            },
            ["localisation"] = new
            {
                type = "CSV",
                directory = "data/text"
            },
            ["projectReferences"] = Array.Empty<object>()
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

    /// <summary>A mod name as a file name: whitespace to underscores, anything else unsafe dropped.</summary>
    internal static string SanitizeModName(string name)
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
