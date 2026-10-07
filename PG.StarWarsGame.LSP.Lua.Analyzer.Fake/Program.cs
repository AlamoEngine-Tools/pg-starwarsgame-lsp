// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using OmniSharp.Extensions.LanguageServer.Server;

// A stand-in for emmylua_ls: full document sync, a diagnostic per open and change that echoes
// what arrived, and a hover that reports how the process was started. Behaviour switches:
//   FAKE_CRASH_ONCE_FILE  exit shortly after the first didOpen, unless that file exists (it is created first)
//   FAKE_HANG_HOVER=1     hover never answers

var server = await LanguageServer.From(options => options
    .WithInput(Console.OpenStandardInput())
    .WithOutput(Console.OpenStandardOutput())
    .WithHandler<FakeSync>()
    .WithHandler<FakeHover>()
    .OnStarted(async (s, ct) =>
    {
        // The real analyzer asks for its sections; a client that does not answer stalls it.
        FakeState.Configuration = "pending";
        try
        {
            var answer = await s.Workspace.RequestConfiguration(new ConfigurationParams
            {
                Items = new Container<ConfigurationItem>(new ConfigurationItem { Section = "emmylua" })
            }, ct);
            FakeState.Configuration = answer is null ? "null" : $"answered {answer.Count()}";
        }
        catch (Exception ex)
        {
            FakeState.Configuration = "failed " + ex.GetType().Name + " " + ex.Message;
        }
    }));

await server.WaitForExit;

internal static class FakeState
{
    public static volatile string Configuration = "not asked";
}

internal sealed class FakeSync(ILanguageServerFacade server) : TextDocumentSyncHandlerBase
{
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "lua");
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        Publish(request.TextDocument.Uri,
            $"open v{request.TextDocument.Version} len {request.TextDocument.Text.Length}");
        if (Environment.GetEnvironmentVariable("FAKE_CRASH_ONCE_FILE") is { Length: > 0 } marker &&
            !File.Exists(marker))
        {
            File.WriteAllText(marker, "crashed");
            // Later, not now: the publish above is written asynchronously, and exiting at once lost
            // it on a slower machine (CI), so the test saw nothing before the crash.
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                Environment.Exit(3);
            });
        }

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var text = request.ContentChanges.Last().Text;
        Publish(request.TextDocument.Uri, $"change v{request.TextDocument.Version} len {text.Length}");
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        Publish(request.TextDocument.Uri, "close");
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lua"),
            Change = TextDocumentSyncKind.Full
        };
    }

    private void Publish(DocumentUri uri, string message)
    {
        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(new Diagnostic
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(0, 0, 0, 1),
                Message = message,
                Code = "fake"
            })
        });
    }
}

internal sealed class FakeHover(ILanguageServerFacade server) : HoverHandlerBase
{
    public override async Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("FAKE_HANG_HOVER") == "1")
            await Task.Delay(Timeout.Infinite, ct);

        var config = Environment.GetEnvironmentVariable("EMMYLUALS_CONFIG") ?? "";
        var folders = string.Join(",", server.ClientSettings.WorkspaceFolders?.Select(f => f.Uri.ToString()) ?? []);
        var args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.PlainText,
                Value =
                    $"args={args}|config={config}|configExists={File.Exists(config)}|folders={folders}|configuration={FakeState.Configuration}"
            })
        };
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("lua") };
    }
}