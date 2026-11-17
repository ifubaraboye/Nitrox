using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nitrox.Launcher.Models.Design;
using Nitrox.Launcher.Models.Services;
using Nitrox.Launcher.Models.Utils;
using Nitrox.Launcher.ViewModels.Abstract;
using Nitrox.Model.Constants;
using Nitrox.Model.Core;
using Nitrox.Model.Helper;
using Nitrox.Model.Logger;
using Nitrox.Model.Platforms.Discovery.Models;
using Nitrox.Model.Platforms.OS.Shared;
using Nitrox.Model.Platforms.Store;

namespace Nitrox.Launcher.ViewModels;

internal partial class LaunchGameViewModel(DialogService dialogService, ServerService serverService, OptionsViewModel optionsViewModel, IKeyValueStore keyValueStore)
    : RoutableViewModelBase
{
    private static bool hasInstantLaunched;
    private readonly DialogService dialogService = dialogService;
    private readonly IKeyValueStore keyValueStore = keyValueStore;

    private readonly ServerService serverService = serverService;

    [ObservableProperty]
    public partial Platform GamePlatform { get; set; }

    [ObservableProperty]
    public partial string? PlatformToolTip { get; set; }

    public Bitmap[] GalleryImageSources { get; } =
    [
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-1.png", static stream => new Bitmap(stream)),
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-2.png", static stream => new Bitmap(stream)),
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-3.png", static stream => new Bitmap(stream)),
        AssetHelper.GetAssetFromStream("/Assets/Images/gallery/image-4.png", static stream => new Bitmap(stream))
    ];

    public string Version => $"{NitroxEnvironment.ReleasePhase} {NitroxEnvironment.Version}";

    internal override async Task ViewContentLoadAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            GamePlatform = NitroxUser.GamePlatform?.Platform ?? Platform.NONE;
            PlatformToolTip = GamePlatform.GetAttribute<DescriptionAttribute>()?.Description ?? "";
            HandleInstantLaunchForDevelopment();
        }, cancellationToken);
    }

    internal override Task ViewContentUnloadAsync()
    {
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StartSingleplayerAsync()
    {
        if (GameInspect.WarnIfGameProcessExists(GameInfo.Subnautica) && !keyValueStore.GetIsMultipleGameInstancesAllowed())
        {
            return;
        }

        Log.Info("Launching Subnautica in singleplayer mode");
        try
        {
            if (string.IsNullOrWhiteSpace(NitroxUser.GamePath) || !Directory.Exists(NitroxUser.GamePath))
            {
                ChangeView(optionsViewModel);
                LauncherNotifier.Warning("Location of Subnautica is unknown. Set the path to it in settings");
                return;
            }

            NitroxEntryPatch.Remove(NitroxUser.GamePath);
            await StartSubnauticaAsync();
        }
        catch (Exception ex)
        {
            LaunchDiagnostics.Write("StartSingleplayerAsync: caught exception", ex);
            Log.Error(ex, "Error while starting game in singleplayer mode:");
            await dialogService.ShowErrorAsync(ex, "Error while starting game in singleplayer mode");
        }
    }

    /// <summary>
    ///     Prepares Subnautica to load Nitrox, then starts Subnautica.
    /// </summary>
    [RelayCommand]
    private async Task StartMultiplayerAsync(string[]? args = null)
    {
        Log.Info("Launching Subnautica in multiplayer mode");
        LaunchDiagnostics.Write("StartMultiplayerAsync: begin");
        try
        {
            LaunchDiagnostics.Write($"StartMultiplayerAsync: gamePath='{NitroxUser.GamePath}', platform='{NitroxUser.GamePlatform?.Name ?? "<null>"}', launcherPath='{NitroxUser.ExecutableRootPath ?? "<null>"}'");

            // Validate and navigate on the UI thread. Doing this inside Task.Run can crash Avalonia.
            if (string.IsNullOrWhiteSpace(NitroxUser.GamePath) || !Directory.Exists(NitroxUser.GamePath))
            {
                LaunchDiagnostics.Write("StartMultiplayerAsync: game path is missing or invalid, opening options view");
                ChangeView(optionsViewModel);
                LauncherNotifier.Warning("Location of Subnautica is unknown. Set the path to it in settings");
                return;
            }

            string currentStep = "starting setup";
            Task<bool> setupTask = Task.Run(async () =>
            {
                try
                {
                    currentStep = "checking for a running Subnautica process";
                    LaunchDiagnostics.Write($"setup: {currentStep}");
                    if (GameInspect.WarnIfGameProcessExists(GameInfo.Subnautica) && !keyValueStore.GetIsMultipleGameInstancesAllowed())
                    {
                        LaunchDiagnostics.Write("setup: Subnautica is already running, aborting");
                        return false;
                    }

                    currentStep = "checking the game version";
                    LaunchDiagnostics.Write($"setup: {currentStep}");
                    if (await GameInspect.IsOutdatedGameAndNotify(NitroxUser.GamePath, dialogService))
                    {
                        LaunchDiagnostics.Write("setup: game version is outdated, aborting");
                        return false;
                    }

                    // TODO: The launcher should override FileRead win32 API for the Subnautica process to give it the modified Assembly-CSharp from memory
                    currentStep = "copying NitroxPatcher.dll into the game folder";
                    LaunchDiagnostics.Write($"setup: {currentStep}");
                    try
                    {
                        const string PATCHER_DLL_NAME = "NitroxPatcher.dll";

                        string launcherDirectory = NitroxUser.ExecutableRootPath ?? AppContext.BaseDirectory;
                        string patcherDllPath = Path.Combine(launcherDirectory, "lib", "net472", PATCHER_DLL_NAME);
                        if (!File.Exists(patcherDllPath))
                        {
                            // Fall back to the directory the launcher itself was started from.
                            patcherDllPath = Path.Combine(AppContext.BaseDirectory, "lib", "net472", PATCHER_DLL_NAME);
                        }
                        LaunchDiagnostics.Write($"setup: copying '{patcherDllPath}'");
                        if (!File.Exists(patcherDllPath))
                        {
                            LaunchDiagnostics.Write($"setup: patcher dll not found at '{patcherDllPath}'");
                            LauncherNotifier.Error("Launcher files seems corrupted, please contact us");
                            return false;
                        }

                        string targetPath = Path.Combine(NitroxUser.GamePath, GameInfo.Subnautica.DataFolder, "Managed", PATCHER_DLL_NAME);
                        File.Copy(patcherDllPath, targetPath, true);
                        LaunchDiagnostics.Write($"setup: copied patcher dll to '{targetPath}'");
                    }
                    catch (IOException ex)
                    {
                        Log.Error(ex, "Unable to move initialization dll to Managed folder. Still attempting to launch because it might exist from previous runs");
                        LaunchDiagnostics.Write("setup: patcher dll copy failed (IOException), continuing", ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log.Error(ex, "No permission to write the initialization dll to the Managed folder");
                        LaunchDiagnostics.Write("setup: patcher dll copy failed (UnauthorizedAccessException), aborting", ex);
                        LauncherNotifier.Error("Nitrox has no permission to write to the Subnautica folder. Run Nitrox as administrator or reinstall the game outside a protected folder.");
                        return false;
                    }

                    // Try inject Nitrox into Subnautica code.
                    currentStep = "patching Subnautica (Assembly-CSharp.dll)";
                    LaunchDiagnostics.Write($"setup: {currentStep}");
                    LaunchDiagnostics.Write("setup: before Apply");
                    bool patchApplied = await ApplyPatchInChildProcessAsync(NitroxUser.GamePath);
                    LaunchDiagnostics.Write($"setup: after Apply (success={patchApplied})");
                    if (!patchApplied)
                    {
                        return false;
                    }
                    LaunchDiagnostics.Write("setup: Subnautica patched");

                    currentStep = "checking for mod loaders";
                    LaunchDiagnostics.Write($"setup: {currentStep}");
                    if (QModHelper.IsQModInstalled(NitroxUser.GamePath))
                    {
                        Log.Warn("Seems like QModManager is installed");
                        LauncherNotifier.Warning("QModManager Detected in the game folder");
                    }
                    GameInspect.WarnIfBepInExMods(NitroxUser.GamePath);

                    LaunchDiagnostics.Write("setup: completed successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    LaunchDiagnostics.Write($"setup: FAILED at step '{currentStep}'", ex);
                    throw;
                }
            });

            // Do not let a stuck setup step look like a silent crash: report where it got stuck instead.
            if (await Task.WhenAny(setupTask, Task.Delay(TimeSpan.FromSeconds(120))) != setupTask)
            {
                string stuckStep = currentStep;
                Log.Warn($"Preparation for multiplayer is still running after 120 seconds at step: {stuckStep}");
                LaunchDiagnostics.Write($"setup: STILL RUNNING after 120 seconds at step '{stuckStep}'");
                await dialogService.ShowErrorAsync(
                    new TimeoutException($"Nitrox got stuck while preparing Subnautica: {stuckStep}"),
                    "Nitrox is taking too long",
                    $"Nitrox is still busy with: {stuckStep}.{Environment.NewLine}{Environment.NewLine}If this keeps happening, send the log files to the developer."
                );
                return;
            }

            bool setupResult = await setupTask;
            LaunchDiagnostics.Write($"StartMultiplayerAsync: setup result={setupResult}");
            if (!setupResult)
            {
                return;
            }

            LaunchDiagnostics.Write("StartSubnauticaAsync: begin");
            await StartSubnauticaAsync(args);
            LaunchDiagnostics.Write("StartSubnauticaAsync: returned");
        }
        catch (Exception ex)
        {
            LaunchDiagnostics.Write("StartMultiplayerAsync: caught exception", ex);
            Log.Error(ex, "Error while starting game in multiplayer mode:");
            await dialogService.ShowErrorAsync(ex, "Error while starting game in multiplayer mode");
        }
    }

    /// <summary>
    ///     Applies the Nitrox entry patch in a separate launcher process. Patching is isolated because a forced launcher
    ///     shutdown during this step would otherwise silently kill the launcher before Subnautica can start.
    /// </summary>
    private async Task<bool> ApplyPatchInChildProcessAsync(string gamePath)
    {
        string? launcherPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
        {
            LaunchDiagnostics.Write("[patch-game] launcher executable is unknown, applying the patch in-process");
            await NitroxEntryPatch.ApplyViaReflection(gamePath);
            return true;
        }

        ProcessStartInfo startInfo = new(launcherPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("patch-game");
        startInfo.ArgumentList.Add(gamePath);

        Process? process;
        try
        {
            LaunchDiagnostics.Write($"[patch-game] starting '{launcherPath}' with game path '{gamePath}'");
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            LaunchDiagnostics.Write("[patch-game] could not start the patch process, applying the patch in-process", ex);
            await NitroxEntryPatch.ApplyViaReflection(gamePath);
            return true;
        }

        if (process == null)
        {
            LaunchDiagnostics.Write("[patch-game] Process.Start returned null, applying the patch in-process");
            await NitroxEntryPatch.ApplyViaReflection(gamePath);
            return true;
        }

        using (process)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                LaunchDiagnostics.Write("[patch-game] patch process did not exit within 3 minutes, killing it");
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // Already gone.
                }

                await dialogService.ShowErrorAsync(
                    new TimeoutException("The Subnautica patch step did not finish within 3 minutes."),
                    "Nitrox is taking too long",
                    "Nitrox could not finish preparing Subnautica. Send the diagnostics log to the developer if this keeps happening."
                );
                return false;
            }

            int exitCode = process.ExitCode;
            LaunchDiagnostics.Write($"[patch-game] patch process exited with code {exitCode} (0x{exitCode:X8})");
            if (exitCode == 0)
            {
                // An exit code of 0 is not trustworthy on its own: a patch child killed by an interceptor or
                // crash handler can still exit 0. Only continue if the entry call is really in the assembly.
                if (!NitroxEntryPatch.IsPatchApplied(gamePath))
                {
                    LaunchDiagnostics.Write("[patch-game] exit code 0 but Assembly-CSharp.dll does NOT contain the Nitrox entry call");
                    await dialogService.ShowErrorAsync(
                        new InvalidOperationException("Assembly-CSharp.dll was not patched even though the patch helper reported success."),
                        "Could not prepare Subnautica",
                        $"The Nitrox entry point is missing from Subnautica's Assembly-CSharp.dll, so the game would start without multiplayer. Nothing was launched.{Environment.NewLine}{Environment.NewLine}Details were written to %APPDATA%\\Nitrox\\logs\\nitrox-launch-diagnostics.log"
                    );
                    return false;
                }

                LaunchDiagnostics.Write("[patch-game] parent confirmed the Nitrox entry call is present in Assembly-CSharp.dll");
                return true;
            }

            await dialogService.ShowErrorAsync(
                new InvalidOperationException($"The Subnautica patch helper stopped with exit code {exitCode} (0x{exitCode:X8})."),
                "Could not prepare Subnautica",
                $"The helper process that patches Subnautica stopped unexpectedly (exit code {exitCode}, 0x{exitCode:X8}).{Environment.NewLine}{Environment.NewLine}Details were written to:%APPDATA%\\Nitrox\\logs\\nitrox-launch-diagnostics.log"
            );
            return false;
        }
    }

    [RelayCommand]
    private void OpenContributionsOfYear()
    {
        string fromValue = HttpUtility.UrlEncode($"{DateTime.UtcNow.AddYears(-1):M/d/yyyy}");
        string toValue = HttpUtility.UrlEncode($"{DateTime.UtcNow:M/d/yyyy}");
        OpenUri($"github.com/SubnauticaNitrox/Nitrox/graphs/contributors?from={fromValue}&to={toValue}");
    }

    /// <summary>
    ///     Launches the server and Subnautica immediately if instant launch is active.
    /// </summary>
    [Conditional("DEBUG")]
    private void HandleInstantLaunchForDevelopment()
    {
        if (hasInstantLaunched)
        {
            return;
        }
        hasInstantLaunched = true;
        if (App.InstantLaunch == null)
        {
            return;
        }
        Task.Run(async () =>
        {
            // Start the server
            ServerEntry? server = await serverService.GetOrCreateServerAsync(App.InstantLaunch.SaveName);
            if (server == null)
            {
                throw new Exception("Failed to create new server save files");
            }
            server.Name = App.InstantLaunch.SaveName;
            Task serverStartTask = Dispatcher.UIThread.InvokeAsync(async () => await serverService.StartServerAsync(server)).ContinueWithHandleError();

            // Start a game in multiplayer for each player
            foreach (string playerName in App.InstantLaunch.PlayerNames)
            {
                await StartMultiplayerAsync(["--instantlaunch", playerName]).ContinueWithHandleError();
            }

            await serverStartTask;
        }).ContinueWithHandleError();
    }

    private async Task StartSubnauticaAsync(string[]? args = null) => await StartGameAsync(GameInfo.Subnautica, args);

    private async Task StartGameAsync(GameInfo gameInfo, string[]? args = null)
    {
        LauncherNotifier.Info("Starting game");
        LaunchDiagnostics.Write("StartGameAsync: begin");

        string gameExePathSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "MacOS" : string.Empty;
        string gameExePath = Path.Combine(NitroxUser.GamePath, gameExePathSuffix, gameInfo.ExeName);
        LaunchDiagnostics.Write($"StartGameAsync: gameExecutable='{gameExePath}'");
        if (!File.Exists(gameExePath))
        {
            throw new FileNotFoundException($"Unable to find {gameInfo.ExeName}");
        }

        // Start game & gaming platform if needed.
        string launchArguments = $"{keyValueStore.GetLaunchArguments(gameInfo)} {string.Join(" ", args ?? NitroxEnvironment.CommandLineArgs)}";

        LaunchDiagnostics.Write($"StartGameAsync: nitroxLauncherPath='{NitroxUser.LauncherPath ?? "<null>"}'");

        if (keyValueStore.GetIsDiscordEnabled() && !launchArguments.Contains(DiscordConstants.ENABLE_ARG, StringComparison.OrdinalIgnoreCase))
        {
            launchArguments = $"{launchArguments} {DiscordConstants.ENABLE_ARG}";
        }

        bool useWine = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && keyValueStore.GetUseWine(defaultValue: true);
        bool launchDirectlyWithoutSteam = !useWine && NitroxUser.GamePlatform is Steam && keyValueStore.GetLaunchDirectlyWithoutSteam(defaultValue: true);
        string launchMethodName = useWine ? nameof(Wine) : launchDirectlyWithoutSteam ? "direct launch" : NitroxUser.GamePlatform?.Name ?? nameof(Standalone);
        LaunchDiagnostics.Write($"StartGameAsync: launchMethod={launchMethodName}, platform={NitroxUser.GamePlatform?.Name ?? "<null>"}, args='{launchArguments}'");
        ProcessEx game;
        if (useWine)
        {
            game = await Wine.StartGameAsync(gameExePath, launchArguments, keyValueStore.GetWinePrefix(), keyValueStore.GetWineExecutable());
        }
        else if (launchDirectlyWithoutSteam)
        {
            Log.Info("Launching game directly without Steam");
            game = await Standalone.StartGameAsync(gameExePath, launchArguments,
            [
                ("SteamAppId", gameInfo.SteamAppId.ToString()),
                ("SteamGameId", gameInfo.SteamAppId.ToString())
            ]);
        }
        else
        {
            game = NitroxUser.GamePlatform switch
            {
                // TODO: Pass ILogger instead of "onWarning" Action<string>.
                Steam => await Steam.StartGameAsync(gameExePath, launchArguments, gameInfo.SteamAppId, ShouldSkipSteam(launchArguments), keyValueStore.GetUseBigPictureMode(), message =>
                {
                    Log.Warn(message);
                    LauncherNotifier.Warning(message);
                }),
                EpicGames => await EpicGames.StartGameAsync(gameExePath, launchArguments),
                HeroicGames => await HeroicGames.StartGameAsync(gameInfo.EgsNamespace, launchArguments),
                MSStore => await MSStore.StartGameAsync(gameExePath, launchArguments),
                Discord => await Discord.StartGameAsync(gameExePath, launchArguments),
                _ => await Standalone.StartGameAsync(gameExePath, launchArguments),
            };
        }

        if (game is null)
        {
            LaunchDiagnostics.Write($"StartGameAsync: process start through {launchMethodName} returned null");
            throw new Exception($"Game failed to start through {launchMethodName}");
        }
        LaunchDiagnostics.Write($"StartGameAsync: process started through {launchMethodName}");
    }

    private bool ShouldSkipSteam(string args)
    {
        // Check if Steam overlay is enabled by user setting
        if (keyValueStore.GetUseBigPictureMode())
        {
            return false; // Use Steam if overlay is enabled
        }

        if (App.InstantLaunch != null)
        {
            // Running through Steam is fine if single instance.
            return App.InstantLaunch is { PlayerNames.Length: > 1 };
        }
        if (args.Contains("-vrmode none", StringComparison.OrdinalIgnoreCase))
        {
            if (keyValueStore.GetIsMultipleGameInstancesAllowed())
            {
                return true;
            }
        }
        else if (args.Contains("-vrmode", StringComparison.OrdinalIgnoreCase))
        {
            // VR Mode. Can only work if NOT going through Steam as it will always add '-vrmode none' due to hard coded default Steam "launch option" args. See: https://steamdb.info/app/264710/config/
            return true;
        }

        return false; // Default: use Steam unless explicitly disabled for special cases
    }
}
