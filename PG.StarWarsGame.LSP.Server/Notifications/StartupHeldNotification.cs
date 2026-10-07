// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Notifications;

/// <summary>
///     Holds a client push back while startup runs, then sends it once when the startup gate opens.
/// </summary>
/// <remarks>
///     <para>
///         Startup rewrites the index stage by stage - baseline, documents, localisation, assets,
///         bones, enums - and each stage raises the events the panel notifiers listen to. A push per
///         stage rebuilds every open panel for content that is not final yet, and a debounced one
///         could land after <c>$/workspaceScanComplete</c>, so "complete" was not quiet.
///     </para>
///     <para>
///         None of these pushes carries data that needs queueing - each says "re-read", and the
///         story push reads its invalidated campaigns when it sends - so the queue is one flag.
///         The gate raises <see cref="IStartupGate.Opened" /> before the pipeline announces the
///         scan complete, so a held push always arrives before that announcement.
///     </para>
/// </remarks>
public sealed class StartupHeldNotification
{
    private readonly IStartupGate _gate;
    private readonly Action _send;
    private int _held;

    /// <param name="gate">The startup gate; while it is closed, pushes are held.</param>
    /// <param name="send">Sends the push. Called at most once for everything held.</param>
    public StartupHeldNotification(IStartupGate gate, Action send)
    {
        _gate = gate;
        _send = send;
        gate.Opened += Release;
    }

    /// <summary>
    ///     True when the push may go out now. False when startup is still running: the push is
    ///     held and sent when the gate opens.
    /// </summary>
    public bool PassesNow()
    {
        if (_gate.IsOpen) return true;

        Volatile.Write(ref _held, 1);
        // The gate may have opened between the check and the write, after its Opened already ran.
        if (_gate.IsOpen) Release();
        return false;
    }

    private void Release()
    {
        if (Interlocked.Exchange(ref _held, 0) == 1) _send();
    }
}
