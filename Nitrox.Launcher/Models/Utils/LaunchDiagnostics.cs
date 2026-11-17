using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nitrox.Model.Helper;

namespace Nitrox.Launcher.Models.Utils;

/// <summary>
///     Writes launcher launch diagnostics directly to disk, synchronously.
///     The normal Serilog file sink is asynchronous, so the last few log lines can be lost when the process dies
///     abruptly (native crash, stack overflow, unhandled thread exception, antivirus kill). This writer flushes every
///     line, which makes it possible to see the exact last step that ran before the launcher disappeared.
/// </summary>
internal static class LaunchDiagnostics
{
    private const int MaxFirstChanceExceptions = 300;
    private const int MaxStackFrames = 4;

    private static readonly object fileLock = new();
    private static readonly object firstChanceLock = new();
    private static readonly HashSet<string> seenFirstChanceExceptions = [];
    private static string? filePath;
    private static int firstChanceExceptionCount;
    private static bool isFirstChanceHandlerBusy;

    public static string FilePath => filePath ??= ResolveFilePath();

    private static string ResolveFilePath()
    {
        try
        {
            string directory = NitroxDirectory.LogsPath;
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "nitrox-launch-diagnostics.log");
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "nitrox-launch-diagnostics.log");
        }
    }

    public static void Write(string message, Exception? exception = null)
    {
        string line = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [p{Environment.ProcessId}] [T{Environment.CurrentManagedThreadId}] {message}";
        if (exception != null)
        {
            line += $"{Environment.NewLine}{DescribeException(exception)}";
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                lock (fileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    // AppendAllText opens/closes the file each call, so the data is on disk even if the process is killed next.
                    File.AppendAllText(FilePath, $"{line}{Environment.NewLine}");
                }
                return;
            }
            catch (IOException)
            {
                // Another instance (or a text editor) may briefly hold the file. Retry before giving up.
                Thread.Sleep(20);
            }
            catch
            {
                return;
            }
        }
    }

    public static void SetupGlobalHandlers()
    {
        Write($"=== Nitrox launcher started (pid {Environment.ProcessId}) ===");
        try
        {
            Write($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} / {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Write($"BaseDirectory: {AppContext.BaseDirectory}");
            Write($"CurrentDirectory: {Directory.GetCurrentDirectory()}");
            Write($"User: {Environment.UserName}, interactive={Environment.UserInteractive}");
            Write($"LauncherPath: '{NitroxUser.LauncherPath ?? "<null>"}', GamePath: '{NitroxUser.GamePath}', GamePlatform: '{NitroxUser.GamePlatform?.Name ?? "<null>"}'");
        }
        catch
        {
            // Environment details are optional.
        }

        AppDomain.CurrentDomain.UnhandledException += static (_, args) =>
        {
            Write($"UNHANDLED EXCEPTION (isTerminating={args.IsTerminating})", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"));
        };
        TaskScheduler.UnobservedTaskException += static (_, args) =>
        {
            Write("UNOBSERVED TASK EXCEPTION", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            Write($"=== ProcessExit: process is exiting (pid {Environment.ProcessId}, exitCode={Environment.ExitCode} / 0x{Environment.ExitCode:X8}) ===");
        };
        AppDomain.CurrentDomain.FirstChanceException += static (_, args) =>
        {
            // First-chance exceptions happen constantly during normal operation (Avalonia binding, COM, ...). Only the
            // first occurrence of each distinct exception is stored so the one right before an abrupt exit stands out.
            lock (firstChanceLock)
            {
                if (isFirstChanceHandlerBusy || firstChanceExceptionCount >= MaxFirstChanceExceptions)
                {
                    return;
                }
                isFirstChanceHandlerBusy = true;
                try
                {
                    string key = $"{args.Exception.GetType().FullName}|{args.Exception.Message}|{GetFirstStackFrame(args.Exception)}";
                    if (!seenFirstChanceExceptions.Add(key))
                    {
                        return;
                    }
                    firstChanceExceptionCount++;
                    Write($"First-chance exception #{firstChanceExceptionCount}", args.Exception);
                }
                finally
                {
                    isFirstChanceHandlerBusy = false;
                }
            }
        };
    }

    private static string DescribeException(Exception exception)
    {
        try
        {
            string description = $"{exception.GetType().FullName}: {exception.Message}";
            string[] stackFrames = (exception.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < stackFrames.Length && i < MaxStackFrames; i++)
            {
                description += $"{Environment.NewLine}    {stackFrames[i]}";
            }
            if (exception.InnerException != null)
            {
                description += $"{Environment.NewLine}  Inner: {DescribeException(exception.InnerException)}";
            }
            return description;
        }
        catch
        {
            return $"{exception.GetType().FullName}: <exception description failed>";
        }
    }

    private static string GetFirstStackFrame(Exception exception)
    {
        try
        {
            string? stackTrace = exception.StackTrace;
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return "";
            }
            int end = stackTrace.IndexOf('\n');
            return (end >= 0 ? stackTrace[..end] : stackTrace).Trim();
        }
        catch
        {
            return "";
        }
    }
}
