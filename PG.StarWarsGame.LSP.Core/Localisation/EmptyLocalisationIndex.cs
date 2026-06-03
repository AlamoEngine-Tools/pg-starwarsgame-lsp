// Copyright (c) Alamoeng Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Localisation;

internal sealed class EmptyLocalisationIndex : ILocalisationIndex
{
    public static readonly EmptyLocalisationIndex Instance = new();

    private EmptyLocalisationIndex()
    {
    }

    public bool ContainsKey(string key)
    {
        return false;
    }

    public IEnumerable<string> Keys => [];

    public string? GetValue(string key)
    {
        return null;
    }
}