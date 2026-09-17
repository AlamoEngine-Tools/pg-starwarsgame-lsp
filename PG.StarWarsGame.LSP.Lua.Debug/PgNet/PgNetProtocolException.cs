// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     A datagram or bit stream that does not follow the wire format: truncated fields, a non-ASCII
///     string, an unexpected magic or greeting. The transport drops the datagram and carries on.
/// </summary>
public class PgNetProtocolException : Exception
{
    public PgNetProtocolException(string message) : base(message)
    {
    }

    public PgNetProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
