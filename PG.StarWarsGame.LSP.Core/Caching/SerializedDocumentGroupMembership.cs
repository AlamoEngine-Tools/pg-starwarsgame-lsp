// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Caching;

[MessagePackObject]
public sealed class SerializedDocumentGroupMembership
{
    [Key(0)] public GroupMembership Membership { get; set; } = null!;
    [Key(1)] public int TagLine { get; set; }
    [Key(2)] public int TagColumn { get; set; }
    [Key(3)] public int TagLength { get; set; }

    public static SerializedDocumentGroupMembership FromRuntime(DocumentGroupMembership m)
    {
        return new SerializedDocumentGroupMembership
        {
            Membership = m.Membership,
            TagLine = m.TagLine,
            TagColumn = m.TagColumn,
            TagLength = m.TagLength
        };
    }

    public DocumentGroupMembership ToRuntime()
    {
        return new DocumentGroupMembership(Membership, TagLine, TagColumn, TagLength);
    }
}