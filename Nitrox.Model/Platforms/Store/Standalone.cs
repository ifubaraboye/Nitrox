using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Nitrox.Model.Constants;
using Nitrox.Model.Helper;
using Nitrox.Model.Platforms.OS.Shared;

namespace Nitrox.Model.Platforms.Store;

public static class Standalone
{
    public static Task<ProcessEx> StartGameAsync(string pathToGameExe, string launchArguments, IEnumerable<(string, string)>? extraEnvironmentVariables = null)
    {
        List<(string, string)> environmentVariables = [];
        string? launcherPath = NitroxUser.LauncherPath;
        if (!string.IsNullOrWhiteSpace(launcherPath))
        {
            // Passing the launcher folder as argument too: some game setups (cracked wrappers, re-launchers) do not
            // inherit the environment variables set below, and NitroxPatcher needs the launcher folder to load its DLLs.
            launchArguments = $"{launchArguments} --nitrox \"{launcherPath}\"";
            environmentVariables.Add((NitroxUser.LAUNCHER_PATH_ENV_KEY, launcherPath));
        }
        environmentVariables.Add((NitroxConstants.HOST_HOME_ENV_VAR_NAME, NitroxDirectory.HomePath));
        if (extraEnvironmentVariables != null)
        {
            environmentVariables.AddRange(extraEnvironmentVariables);
        }

        return Task.FromResult(
            ProcessEx.Start(
                pathToGameExe,
                environmentVariables,
                Path.GetDirectoryName(pathToGameExe),
                launchArguments
            )
        );
    }
}
