// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Serilog;
using Serilog.Core;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The DEBUG file log: one file per day at the path <see cref="ServerLogPath" /> resolves.
///     <para>
///         The files of every project of a workspace share one folder in the user's own store, and
///         nothing else ever clears it, so the log bounds itself: a file older than
///         <see cref="RetainedDays" /> goes when a newer one is opened. Serilog matches files by the
///         stem it rolls, so one project's server never removes another project's files.
///     </para>
/// </summary>
public static class ServerFileLog
{
    /// <summary>How many days of log files stay beside the current one.</summary>
    public const int RetainedDays = 14;

    /// <summary>The logger writing to <paramref name="path" />, a stem Serilog appends the date to.</summary>
    public static Logger Create(string path)
    {
        return new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File(path, rollingInterval: RollingInterval.Day,
                retainedFileTimeLimit: TimeSpan.FromDays(RetainedDays))
            .MinimumLevel.Debug()
            .CreateLogger();
    }
}