// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

public sealed class PreOpenBuffer : IPreOpenBuffer
{
    private readonly object _lock = new();
    private readonly List<(string Uri, string Text, int Version)> _pending = [];
    private bool _closed;

    public void RecordOpen(string uri, string text, int version)
    {
        lock (_lock)
        {
            if (!_closed)
                _pending.Add((uri, text, version));
        }
    }

    public IReadOnlyList<(string Uri, string Text, int Version)> DrainAndClose()
    {
        lock (_lock)
        {
            _closed = true;
            var result = _pending.ToList();
            _pending.Clear();
            return result;
        }
    }
}