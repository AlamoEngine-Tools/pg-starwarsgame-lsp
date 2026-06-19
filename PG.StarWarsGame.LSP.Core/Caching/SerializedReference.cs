// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Caching;

[MessagePackObject]
public sealed class SerializedReference
{
    [Key(0)] public string TargetId { get; set; } = string.Empty;
    [Key(1)] public GameSymbolKind? ExpectedKind { get; set; }
    [Key(2)] public string? ExpectedTypeName { get; set; }
    [Key(3)] public string DocumentUri { get; set; } = string.Empty;
    [Key(4)] public int Line { get; set; }
    [Key(5)] public int Column { get; set; }
    [Key(6)] public int Length { get; set; }

    public static SerializedReference FromRuntime(GameReference r)
    {
        return new SerializedReference
        {
            TargetId = r.TargetId,
            ExpectedKind = r.ExpectedKind,
            ExpectedTypeName = r.ExpectedTypeName,
            DocumentUri = r.DocumentUri,
            Line = r.Line,
            Column = r.Column,
            Length = r.Length
        };
    }

    public GameReference ToRuntime()
    {
        return new GameReference(TargetId, ExpectedKind, ExpectedTypeName, DocumentUri, Line, Column, Length);
    }
}