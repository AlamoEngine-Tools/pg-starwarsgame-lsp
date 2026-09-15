// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Surfaces a message to the user as an editor notification (VS Code balloon) via the LSP
///     <c>window/showMessage</c> channel. Lets server logic report actionable failures to the user
///     directly instead of leaving them buried in the log.
/// </summary>
public interface IUserNotifier
{
    void ShowError(string message);

    /// <summary>
    ///     Reports that something the user asked for actually happened, and what it did. Needed
    ///     wherever the client cannot tell success from a silent no-op on its own - a command whose
    ///     only channel is <c>workspace/executeCommand</c> returns nothing to inspect, so without
    ///     this the client can only guess, and guessing produced a success message for every
    ///     failure.
    /// </summary>
    void ShowInfo(string message);
}