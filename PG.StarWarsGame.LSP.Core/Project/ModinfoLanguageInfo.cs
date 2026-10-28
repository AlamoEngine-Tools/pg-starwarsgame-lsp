// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;

namespace PG.StarWarsGame.LSP.Core.Project;

[PublicAPI]
public sealed record ModinfoLanguageInfo(string Code, int Support);