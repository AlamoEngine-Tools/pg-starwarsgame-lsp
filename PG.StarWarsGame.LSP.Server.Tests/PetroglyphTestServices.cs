// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using AnakinRaW.CommonUtilities.Hashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     What a test container needs before the Petroglyph <c>Support*</c> extensions are useful.
/// </summary>
internal static class PetroglyphTestServices
{
    /// <summary>
    ///     Registers PG.Commons' hashing, which the DAT, MTD and MEG readers all resolve for their
    ///     CRC-32.
    ///     <para>
    ///         Needed since the 4.1.4 packages: <c>SupportDAT</c> used to TryAdd
    ///         <see cref="IHashingService" /> on the way through and no longer does, which left
    ///         every container that only called <c>SupportLocalisationBaseline</c> resolving
    ///         nothing. <c>ServerConfigurator</c> does the same thing for the real server.
    ///     </para>
    ///     <para>
    ///         TryAdd, never Add: <see cref="HashingService" />'s constructor walks every
    ///         <c>IHashAlgorithmProvider</c> it can see and rejects a duplicate <c>CRC32</c> key, so
    ///         a second registration throws at resolution rather than being ignored.
    ///     </para>
    /// </summary>
    public static IServiceCollection SupportPetroglyphHashing(this IServiceCollection services)
    {
        services.TryAddSingleton<IHashingService>(sp => new HashingService(sp));
        return services;
    }
}
