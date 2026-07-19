// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     Base class for late-binding proxies that start with a default implementation and swap
///     to the real one when <see cref="Configure" /> is called (typically in OnInitialize).
/// </summary>
public abstract class LateBindingProxy<T> where T : class
{
    private T _inner;

    protected LateBindingProxy(T defaultValue)
    {
        _inner = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
    }

    protected T Inner => Volatile.Read(ref _inner);

    public void Configure(T provider)
    {
        Volatile.Write(ref _inner, provider ?? throw new ArgumentNullException(nameof(provider)));
        OnConfigured(provider);
    }

    protected virtual void OnConfigured(T provider)
    {
    }
}