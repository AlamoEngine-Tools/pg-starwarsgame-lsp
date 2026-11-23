// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The file log: one file per day at the path <see cref="ServerLogPath" /> resolves, written
///     when <c>--log-file-level=</c> asks for it.
///     <para>
///         It was compiled into Debug builds alone, so a tester on a release could not be asked
///         for a log. Now any build writes it at the level asked, and a release that was not asked
///         writes none. A Debug build that was not asked keeps what it always did: the file, at
///         Debug.
///     </para>
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

    private const string Option = "--log-file-level=";

    /// <summary>
    ///     The level the file is written at, or null when no file is written. The last option
    ///     wins; <c>None</c> switches the file off in every build; anything unparseable counts as
    ///     not asked, as it does for <see cref="ServerLogLevel" />.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="debugBuild">Whether this is a Debug build, which writes the file unasked, at Debug.</param>
    public static LogLevel? Resolve(IReadOnlyList<string> args, bool debugBuild)
    {
        var option = args.LastOrDefault(a => a.StartsWith(Option, StringComparison.Ordinal));
        var asked = option is null ? string.Empty : option[Option.Length..].Trim();
        if (Enum.TryParse<LogLevel>(asked, true, out var level) && Enum.IsDefined(level))
            return level == LogLevel.None ? null : level;

        return debugBuild ? LogLevel.Debug : null;
    }

    /// <summary>
    ///     The logger writing to <paramref name="path" />, a stem Serilog appends the date to, at
    ///     <paramref name="level" /> and above. The file writes the home directory as <c>~</c>: see
    ///     <see cref="HomePath" />.
    /// </summary>
    public static Logger Create(string path, LogLevel level)
    {
        return new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File(new HomeAsTildeFormatter(), path, rollingInterval: RollingInterval.Day,
                retainedFileTimeLimit: TimeSpan.FromDays(RetainedDays))
            .MinimumLevel.Is(EventLevelOf(level))
            .CreateLogger();
    }

    /// <summary>
    ///     Adds the file log to the server's logging at its own level, or nothing when
    ///     <paramref name="level" /> is null.
    /// </summary>
    /// <remarks>
    ///     <see cref="ServerLogLevel.Apply" /> filters every provider at the server's level, which
    ///     is what the output channel shows. Serilog registers a rule of its own for its provider
    ///     that lets every level through, and that rule takes precedence, so the file's level is
    ///     the one its logger was created with: a file asked for at Debug holds the Debug lines
    ///     while the channel stays as quiet as it was.
    /// </remarks>
    /// <param name="builder">The server's logging.</param>
    /// <param name="level">The file's level, from <see cref="Resolve" />.</param>
    /// <param name="logger">The file's logger; the process-wide one when null.</param>
    public static ILoggingBuilder Apply(ILoggingBuilder builder, LogLevel? level, Logger? logger = null)
    {
        return level is null
            ? builder
            : builder.AddSerilog(logger, true);
    }

    /// <summary>The file sink's own line format, with the home directory written as <c>~</c>.</summary>
    private sealed class HomeAsTildeFormatter : ITextFormatter
    {
        private readonly MessageTemplateTextFormatter _line =
            new("{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

        public void Format(LogEvent logEvent, TextWriter output)
        {
            using var line = new StringWriter();
            _line.Format(logEvent, line);
            output.Write(HomePath.AsTilde(line.ToString()));
        }
    }

    private static LogEventLevel EventLevelOf(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Fatal
        };
    }
}