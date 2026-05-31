// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public record TagValidationOverride
{
    public required string ValidationId { get; init; }
    public ValidationOverrideMode Mode { get; init; } = ValidationOverrideMode.Additive;
    public ValidationOverrideOrder Order { get; init; } = ValidationOverrideOrder.Append;
}