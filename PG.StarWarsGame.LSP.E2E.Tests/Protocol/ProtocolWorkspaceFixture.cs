// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>
///     A server on one workspace of the 0.5.0 test protocol (<c>test-protocol/0.5.0</c>), started after the
///     protocol's setup script has written that workspace's files clean - so a test never depends on what a
///     manual run left behind.
/// </summary>
/// <remarks>
///     The protocol's fixtures live outside the repository (<c>C:\temp</c>), and the vanilla corpora are not
///     tracked: without them the tests skip, as every corpus test does.
/// </remarks>
public abstract class ProtocolWorkspaceFixture : LspServerFixture
{
    /// <summary>The folder or workspace file the server opens.</summary>
    protected abstract string? Workspace { get; }

    /// <summary>The protocol test whose setup writes the workspace's files, or null for a tracked folder.</summary>
    protected virtual string? SetupTest => null;

    /// <summary>Further protocol tests whose files the workspace needs, written before the server starts.</summary>
    protected virtual IReadOnlyList<string> AlsoSetUp => [];

    /// <summary>Starts the server with the real Lua analyzer, when one is staged or configured.</summary>
    protected virtual bool WithAnalyzer => false;

    /// <summary>The schema the server reads, when not the repository's.</summary>
    protected virtual string? SchemaPath => null;

    /// <summary>The analyzer the server is given, or null when none is staged.</summary>
    public static string? Analyzer => LspTestEnvironment.RepositoryRoot is { } repo
        ? LuaAnalyzerSmokeTest.FindAnalyzer(repo)
        : null;

    protected override IReadOnlyDictionary<string, string?> ServerEnvironment =>
        new Dictionary<string, string?> { ["AET_EMMYLUA_LS"] = WithAnalyzer ? Analyzer : null };

    protected override object BuildInitOptions()
    {
        return new
        {
            schemaLocalPath = SchemaPath ?? LspTestEnvironment.SchemaLocalPath,
            gamePath = LspTestEnvironment.GamePath,
            baselineLocalPath = LspTestEnvironment.BaselineLocalPath,
            locale = LspTestEnvironment.Locale,
            projectPath = (string?)null
        };
    }

    /// <summary>The folder the tests read and write, once it is there.</summary>
    public string? Root => Workspace is { } w && (Directory.Exists(w) || File.Exists(w)) ? w : null;

    public override async ValueTask InitializeAsync()
    {
        if (SetupTest is { } test) RunSetup(test);
        foreach (var also in AlsoSetUp) RunSetup(also);
        await base.InitializeAsync();
    }

    protected override string ResolveWorkspacePath()
    {
        return Root ?? TestDataDirectory;
    }

    /// <summary>Runs the protocol's setup script for one test; a no-op without the repository or pwsh.</summary>
    public static void RunSetup(string test)
    {
        if (LspTestEnvironment.RepositoryRoot is not { } repo) return;
        var script = Path.Combine(repo, "test-protocol", "0.5.0", "Setup-TestProtocol.ps1");
        if (!File.Exists(script)) return;
        using var process = Process.Start(new ProcessStartInfo("pwsh",
            $"-NoProfile -NonInteractive -File \"{script}\" -Test {test}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit(TimeSpan.FromMinutes(5));
    }
}

/// <summary>The XML tests' scratch project, <c>C:\temp\aet-test-xml</c> (XML-01).</summary>
public sealed class XmlScratchFixture : ProtocolWorkspaceFixture
{
    protected override string Workspace => @"C:\temp\aet-test-xml";
    protected override string SetupTest => "XML-01";
}

/// <summary>
///     A server started by the test itself, for a step that needs a fresh start - a file the server reads
///     once, at startup. Dispose it before starting the next.
/// </summary>
public sealed class AdHocWorkspaceFixture(string workspace) : ProtocolWorkspaceFixture
{
    protected override string Workspace => workspace;

    public static async Task<AdHocWorkspaceFixture> StartAsync(string workspace)
    {
        var fixture = new AdHocWorkspaceFixture(workspace);
        await fixture.InitializeAsync();
        return fixture;
    }
}

/// <summary>The vanilla Empire at War folder.</summary>
public sealed class EawVanillaFixture : ProtocolWorkspaceFixture
{
    protected override string? Workspace => LspTestEnvironment.EawWorkspacePath;
}

/// <summary>The vanilla Forces of Corruption folder.</summary>
public sealed class FocVanillaFixture : ProtocolWorkspaceFixture
{
    protected override string? Workspace => LspTestEnvironment.WorkspacePath;
}

/// <summary>The vanilla Empire at War folder with the Lua tests' scripts and the real analyzer.</summary>
public sealed class EawLuaFixture : ProtocolWorkspaceFixture
{
    protected override string? Workspace => LspTestEnvironment.EawWorkspacePath;
    protected override IReadOnlyList<string> AlsoSetUp => ["LUA-06", "LUA-19", "LUA-20", "LUA-21", "LUA-22"];
    protected override bool WithAnalyzer => true;
}

/// <summary>The vanilla Empire at War folder with no Lua analyzer at all (LUA-31).</summary>
public sealed class EawWithoutAnalyzerFixture : ProtocolWorkspaceFixture
{
    protected override string? Workspace => LspTestEnvironment.EawWorkspacePath;
}

/// <summary>The vanilla Empire at War folder on LUA-25's schema copy, whose Find_Player reads ---@xmlref.</summary>
public sealed class EawXmlrefSchemaFixture : ProtocolWorkspaceFixture
{
    protected override string? Workspace => LspTestEnvironment.EawWorkspacePath;
    protected override string SetupTest => "LUA-25";
    protected override IReadOnlyList<string> AlsoSetUp => ["LUA-06"];
    protected override string SchemaPath => @"C:\temp\aet-test-lua25\schema\eaw";
    protected override bool WithAnalyzer => true;
}

/// <summary>LUA-32's project, whose declared scripts folder does not exist yet.</summary>
public sealed class Lua32Fixture : ProtocolWorkspaceFixture
{
    protected override string Workspace => @"C:\temp\AetTest-LUA32";
    protected override string SetupTest => "LUA-32";
    protected override bool WithAnalyzer => true;
}

/// <summary>LUA-33's project, with a PGStateMachine of its own.</summary>
public sealed class Lua33Fixture : ProtocolWorkspaceFixture
{
    protected override string Workspace => @"C:\temp\aet-test-lua33";
    protected override string SetupTest => "LUA-33";
    protected override bool WithAnalyzer => true;
}

/// <summary>A copy of the load-order mod the protocol's tests edit, <c>C:\temp\aet-test-load-order</c>.</summary>
public sealed class LoadOrderCopyFixture : ProtocolWorkspaceFixture
{
    protected override string Workspace => @"C:\temp\aet-test-load-order";
    protected override string SetupTest => "PRJ-24";
}
