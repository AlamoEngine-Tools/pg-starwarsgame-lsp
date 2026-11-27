// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The settled-diagnostics wait, without a server: publishes are handed to it by hand, so what it
///     does with one that arrives before the server took an edit in is decided here and not by the
///     timing of a run.
/// </summary>
public sealed class SettledDiagnosticsTest
{
    private static readonly DocumentUri Document = DocumentUri.From("file:///c:/mod/Data/Xml/Units.xml");
    private static readonly DocumentUri Other = DocumentUri.From("file:///c:/mod/Data/Xml/Other.xml");

    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(200);

    // Three quiet windows: a wait that was going to settle on what it has would have done so by now.
    private static readonly TimeSpan WellPastQuiet = TimeSpan.FromMilliseconds(600);

    private event Action<PublishDiagnosticsParams>? Published;

    private Task<PublishDiagnosticsParams?> Wait(TimeSpan timeout, Task? after = null)
    {
        return SettledDiagnostics.WaitAsync(
            handler => Published += handler,
            handler => Published -= handler,
            Document, Quiet, timeout, after);
    }

    private void Publish(DocumentUri uri, string message)
    {
        Published?.Invoke(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(new Diagnostic { Message = message })
        });
    }

    private static string? MessageOf(PublishDiagnosticsParams? publish)
    {
        return publish?.Diagnostics.Single().Message.Text();
    }

    [Fact]
    public async Task WithoutABarrier_SettlesOnTheLastPublishOfTheDocument()
    {
        var wait = Wait(TimeSpan.FromSeconds(10));

        Publish(Document, "first");
        Publish(Document, "second");
        Publish(Other, "another document");

        Assert.Equal("second", MessageOf(await wait));
    }

    // The rename roundtrip's failure: a publish from before the rename-back was processed, then a
    // silence longer than the quiet window while the server worked through the edit. The wait
    // returned that publish, and it named the symbol the edit had just renamed away.
    [Fact]
    public async Task WithABarrier_DoesNotSettleOnAPublishFromBeforeIt()
    {
        var processed = new TaskCompletionSource();
        var wait = Wait(TimeSpan.FromSeconds(10), processed.Task);

        Publish(Document, "from before the edit");
        await Task.Delay(WellPastQuiet, TestContext.Current.CancellationToken);

        Assert.False(wait.IsCompleted, "The wait settled on a publish from before the barrier.");

        processed.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Publish(Document, "from after the edit");

        Assert.Equal("from after the edit", MessageOf(await wait));
    }

    // The last publish before the barrier may describe a half-applied edit. Returning it when
    // nothing follows would hand the test a state the author never sees, so the wait returns
    // nothing and the test says that no publish followed.
    [Fact]
    public async Task WithABarrier_ReturnsNothingWhenNoPublishFollowsIt()
    {
        var processed = new TaskCompletionSource();
        var wait = Wait(WellPastQuiet, processed.Task);

        Publish(Document, "from before the edit");
        processed.SetResult();

        Assert.Null(await wait);
    }

    [Fact]
    public async Task WithABarrier_ThatFails_FailsTheWait()
    {
        var processed = new TaskCompletionSource();
        var wait = Wait(TimeSpan.FromSeconds(10), processed.Task);

        processed.SetException(new InvalidOperationException("the barrier request failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => wait);
    }
}