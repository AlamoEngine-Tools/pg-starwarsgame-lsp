// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public sealed record ParamDefinition
{
    /// <summary>0-based slot index. Event_Param1 = position 0, Reward_Param1 = position 0.</summary>
    public required int Position { get; init; }

    public required XmlValueType ValueType { get; init; }
    public ReferenceKind ReferenceKind { get; init; }
    public string? ReferenceType { get; init; }
    public string? EnumName { get; init; }
    public bool Optional { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();
}