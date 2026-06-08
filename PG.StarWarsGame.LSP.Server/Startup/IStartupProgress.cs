// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Reports linear startup progress to the client (one <c>window/workDoneProgress</c> token for
///     the whole sequence). Implementations must be resilient to a client that never grants the
///     progress token.
/// </summary>
public interface IStartupProgress
{
    void Report(string stage, int percent);

    void Complete();
}