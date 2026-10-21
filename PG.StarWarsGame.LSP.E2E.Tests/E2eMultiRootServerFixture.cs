// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A window of two projects (#280): the suite's authored mod over the authored base project in
///     <c>e2e-workspace-multi-root/base</c>. The server runs for the mod, the base is a layer beneath it.
/// </summary>
/// <remarks>
///     The window is composed in a temporary folder when the fixture starts: a copy of
///     <c>e2e-workspace/</c> as <c>mod/</c>, its project file given a reference to the base, beside a
///     copy of the base. One source for the mod's files, so a test changing them changes both setups.
///     Copied are the projects and their data only, never a cache or a log.
/// </remarks>
public sealed class E2eMultiRootServerFixture : E2eWorkspaceServerFixture
{
    private const string ModProjectFile = "e2e-workspace.pgproj";
    private string? _window;

    public override string? ModPath => _window is null ? null : Path.Combine(_window, "mod");

    public override int Layers => 2;

    /// <summary>The folder holding the base project and its files.</summary>
    public string? BasePath => _window is null ? null : Path.Combine(_window, "base");

    protected override string? ProjectPath => ModPath is null ? null : Path.Combine(ModPath, ModProjectFile);

    protected override string ResolveWorkspacePath()
    {
        if (LspTestEnvironment.E2eWorkspacePath is not { } mod ||
            LspTestEnvironment.E2eMultiRootWorkspacePath is not { } multiRoot)
            return TestDataDirectory;

        _window = Path.Combine(Path.GetTempPath(), "aet-e2e-multi-root-" + Guid.NewGuid().ToString("N")[..8]);
        CopyProject(mod, ModPath!);
        CopyProject(Path.Combine(multiRoot, "base"), BasePath!);

        var project = JsonNode.Parse(File.ReadAllText(ProjectPath!))!.AsObject();
        project["projectReferences"] = new JsonArray(new JsonObject { ["path"] = "../base/base.pgproj" });
        File.WriteAllText(ProjectPath!, project.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return _window;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_window is null) return;
        try
        {
            Directory.Delete(_window, true);
        }
        catch (IOException)
        {
            // A file the server still held; the folder is in the temporary directory and goes with it.
        }
    }

    private static void CopyProject(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (relative.StartsWith(".aetswg", StringComparison.OrdinalIgnoreCase) ||
                relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}