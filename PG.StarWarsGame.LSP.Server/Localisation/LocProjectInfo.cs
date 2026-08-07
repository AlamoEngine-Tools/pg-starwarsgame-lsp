// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

// ProjectName/Rank identify which .pgproj layer (root or dependency) this file belongs to —
// Label alone can collide across layers (e.g. two projects both using "MasterTextFile.csv").
// Category is LocCategory.Text or LocCategory.Credits; it decides which editing model the file
// gets, since credits files are ordered and allow duplicate keys.
// Language is set only for the single-language formats (.properties, .dat), which name their one
// language in the file name; the multi-language formats carry every language inside the file and
// leave it null. It exists so the navigator can present a set of language siblings as one entry
// instead of as unrelated files that happen to sort next to each other.
public sealed record LocProjectInfo(
    string Label,
    string FilePath,
    string ResourceType,
    string ProjectName,
    int Rank,
    string Category = LocCategory.Text,
    string? Language = null);