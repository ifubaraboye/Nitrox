using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Nitrox.Model.Constants;
using Nitrox.Model.Helper;
using Nitrox.Model.Logger;
using Nitrox.Model.Platforms.OS.Shared;

namespace Nitrox.Model.Platforms.Store;

/// <summary>
///     Launches the Windows build of Subnautica through Wine on Linux, instead of going through Steam/Proton.
/// </summary>
public static class Wine
{
    private static readonly string[] executableNames = { "wine", "wine64" };

    private static readonly string[] extraSearchPaths =
    {
        "/usr/local/bin",
        "/usr/bin",
        "/opt/wine-stable/bin",
        "/opt/wine-staging/bin",
        "/var/lib/flatpak/exports/bin"
    };

    /// <summary>
    ///     Gets the Wine executable to use. The configured path takes precedence, otherwise Wine is searched on PATH and
    ///     in the usual install locations.
    /// </summary>
    /// <param name="configuredPath">User configured Wine executable path, or null/empty to auto-detect.</param>
    /// <returns>The full path to the Wine executable, or null if Wine could not be found.</returns>
    public static string? FindExecutable(string? configuredPath = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return null;
        }

        foreach (string? candidate in new[] { configuredPath, Environment.GetEnvironmentVariable("NITROX_WINE_EXECUTABLE"), Environment.GetEnvironmentVariable("WINE") })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }
            string expanded = ExpandHome(candidate);
            if (File.Exists(expanded))
            {
                return Path.GetFullPath(expanded);
            }
        }

        IEnumerable<string> searchDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
                                                .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
                                                .Concat(extraSearchPaths);
        foreach (string directory in searchDirectories.Distinct())
        {
            foreach (string executableName in executableNames)
            {
                string candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     Gets the Wine prefix to run the game with. If not configured, the prefix is inferred from the game path by
    ///     looking for its "drive_c" directory. If that fails, Wine's default prefix (~/.wine) is used.
    /// </summary>
    public static string? FindPrefix(string? pathToGameExe, string? configuredPrefix = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPrefix))
        {
            return Path.GetFullPath(ExpandHome(configuredPrefix));
        }
        if (string.IsNullOrWhiteSpace(pathToGameExe))
        {
            return null;
        }

        DirectoryInfo? directory = new FileInfo(pathToGameExe).Directory;
        while (directory != null)
        {
            if (directory.Name.Equals("drive_c", StringComparison.OrdinalIgnoreCase))
            {
                return directory.Parent?.FullName;
            }
            directory = directory.Parent;
        }

        return null;
    }

    public static Task<ProcessEx> StartGameAsync(string pathToGameExe, string launchArguments, string? winePrefix = null, string? wineExecutable = null)
    {
        string executable = FindExecutable(wineExecutable) ?? throw new FileNotFoundException("Wine was not found. Install Wine or set the Wine executable path in the launcher options.");
        string? prefix = FindPrefix(pathToGameExe, winePrefix);

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            Arguments = $"{QuoteArgument(pathToGameExe)} {launchArguments}",
            WorkingDirectory = Path.GetDirectoryName(pathToGameExe),
            UseShellExecute = false
        };

        // The game runs in Wine/Windows, so launcher and home paths must be Windows paths. Wine maps the Linux root to Z:.
        SetWinePathEnvironmentVariable(startInfo, NitroxUser.LAUNCHER_PATH_ENV_KEY, NitroxUser.LauncherPath);
        SetWinePathEnvironmentVariable(startInfo, NitroxConstants.HOST_HOME_ENV_VAR_NAME, NitroxDirectory.HomePath);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            startInfo.EnvironmentVariables["WINEPREFIX"] = prefix;
        }

        Log.Info($"Starting game through Wine: WINEPREFIX=\"{prefix ?? "(default)"}\" \"{executable}\" {startInfo.Arguments}");

        return Task.FromResult(ProcessEx.From(startInfo) ?? throw new Exception("Wine failed to start the game process."));
    }

    private static void SetWinePathEnvironmentVariable(ProcessStartInfo startInfo, string key, string? linuxPath)
    {
        if (string.IsNullOrWhiteSpace(linuxPath))
        {
            return;
        }
        startInfo.EnvironmentVariables[key] = $"Z:{linuxPath.Replace("/", "\\")}";
    }

    private static string QuoteArgument(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }
        return path;
    }
}
