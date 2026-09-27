// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: the document is an XML game file that nothing registers, so the engine never
///     opens it.
///     <para>
///         Measured: a file is read because a registry lists it, because the engine opens it by a
///         name compiled into the binary, or because it sits in a directory the engine walks. The
///         schema declares all three, so a document with no file type left after registration is
///         one the game will not read - and a file the game does not read cannot be told from one
///         it reads and ignores by looking at the file.
///     </para>
/// </summary>
/// <param name="FileName">The file's own name, which is what a registry entry would have to say.</param>
public sealed record XmlUnregisteredFileFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string FileName) : XmlFact(DocumentUri, Line, Column, Length);
