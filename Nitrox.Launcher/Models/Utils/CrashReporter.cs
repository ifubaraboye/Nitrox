using System;
using System.IO;
using System.Linq;
using Nitrox.Model.Helper;
using Nitrox.Model.Logger;
using Nitrox.Model.Platforms.OS.Shared;

namespace Nitrox.Launcher.Models.Utils;

internal static class CrashReporter
{
    private const string CRASH_REPORT_FILE_NAME = "Nitrox.Launcher.crash";

    public static void ReportAndExit(Exception ex)
    {
        LaunchDiagnostics.Write("CrashReporter.ReportAndExit", ex);
        Log.Error(ex, "!!!Nitrox Launcher Crash!!!");

        try
        {
            string crashPath = NitroxDirectory.CrashLogsPath;
            if (!string.IsNullOrWhiteSpace(crashPath))
            {
                Directory.CreateDirectory(crashPath);
                string crashFile = GetNewCrashFilePath(crashPath);
                File.WriteAllText(crashFile, ex.ToString());
                LaunchDiagnostics.Write($"Crash file written to '{crashFile}'");
                ProcessEx.StartSelf("--crash-report");
            }
            else
            {
                Log.Error(ex, "Unable to get crash file path for writing crash report.");
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception);
            LaunchDiagnostics.Write("Failed to write crash file", exception);
        }
        finally
        {
            LaunchDiagnostics.Write("CrashReporter: exiting with code 1");
            Environment.Exit(1);
        }

        static string GetNewCrashFilePath(string crashPath)
        {
            string crashDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            int fileInDayCount = Directory.EnumerateFiles(crashPath).Count(f => f.Contains(crashDate));
            string crashFileName = Path.GetFileNameWithoutExtension(CRASH_REPORT_FILE_NAME);
            return Path.Combine(crashPath, $"{crashFileName}_{crashDate}_{(fileInDayCount + 1).ToString().PadLeft(3, '0')}{Path.GetExtension(CRASH_REPORT_FILE_NAME)}");
        }
    }

    public static string? GetLastReport()
    {
        string crashLogsPath = NitroxDirectory.CrashLogsPath;
        if (Directory.Exists(crashLogsPath) && Directory.EnumerateFiles(crashLogsPath).OrderDescending().FirstOrDefault() is { } crashLog)
        {
            return File.ReadAllText(crashLog);
        }
        return null;
    }
}
