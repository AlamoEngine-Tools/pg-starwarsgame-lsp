// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     Captures what the server would have put in front of the user. Shared rather than nested per
///     test class: what a failure path *says* is now asserted in several suites, and three private
///     copies of the same two lists is how one of them ends up not being updated.
/// </summary>
public sealed class RecordingUserNotifier : IUserNotifier
{
    public List<string> Errors { get; } = [];
    public List<string> Infos { get; } = [];

    public void ShowError(string message)
    {
        Errors.Add(message);
    }

    public void ShowInfo(string message)
    {
        Infos.Add(message);
    }
}
