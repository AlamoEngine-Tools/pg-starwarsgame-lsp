// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an object element in a typed document carries no usable name - the attribute is
///     absent, empty, or only whitespace.
/// </summary>
/// <remarks>
///     Such an object is dead content. <c>XmlGameDocumentParser</c> skips it with a debug log, so it
///     never becomes a symbol: nothing can reference it, nothing can override it, and it is missing
///     from every list the author might check. The only place it exists is the file itself.
/// </remarks>
/// <param name="TypeName">The object type the document is registered for, e.g. <c>GameObjectType</c>.</param>
/// <param name="NameTag">The attribute that should have carried the name, from the type's schema.</param>
/// <param name="ElementName">The element as authored, e.g. <c>GroundVehicle</c>.</param>
public sealed record XmlUnnamedObjectFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TypeName,
    string NameTag,
    string ElementName) : XmlFact(DocumentUri, Line, Column, Length);
