// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IFileTypeRegistry
{
    IReadOnlyDictionary<string, ImmutableArray<string>> All { get; }
    ImmutableArray<string> GetTypesForFile(string fileUri);

    /// <summary>
    ///     Whether anything registered this file at all, whatever it knows about its contents.
    /// </summary>
    /// <remarks>
    ///     Not the same question as "has types". A metafile the schema declares but whose contents
    ///     it does not model yet registers with an empty type list, and
    ///     <see cref="GetTypesForFile" /> answers those two cases identically - which is how
    ///     <c>guidialogs.xml</c> came to be reported as read by nothing when the engine opens it by
    ///     a name compiled into the binary.
    /// </remarks>
    bool IsRegistered(string fileUri)
    {
        return All.ContainsKey(fileUri);
    }

    void RegisterFile(string fileUri, ImmutableArray<string> typeNames);
    void UnregisterFile(string fileUri);
}