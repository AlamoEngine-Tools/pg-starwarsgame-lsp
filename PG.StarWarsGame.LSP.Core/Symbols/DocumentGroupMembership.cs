// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Runtime-only (not serialized) pairing of a <see cref="GroupMembership" /> with the source-text span
///     of the group-key tag value. The tag span is used by <c>XmlPositionResolver</c> to detect when the
///     cursor is positioned on a group-key value; <see cref="Membership" /> supplies the navigation target.
/// </summary>
public sealed record DocumentGroupMembership(
    GroupMembership Membership,
    int TagLine,
    int TagColumn,
    int TagLength
);