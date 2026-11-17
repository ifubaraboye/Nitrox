using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Svg.Skia;
using Nitrox.Launcher.Models.Utils;

namespace Nitrox.Launcher;

internal static class Program
{
    // Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code before AppMain is called
    // Things aren't initialized yet and stuff might break
    [STAThread]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.Handler;
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += AssemblyResolver.Handler;

        if (TryRunPatchGame(args))
        {
            return;
        }

        LoadAvalonia(args);
    }

    /// <summary>
    ///     Runs the Subnautica entry patch in a dedicated child process. The patch step is isolated because an abrupt
    ///     launcher shutdown during patching (e.g. a forced Environment.Exit from an unrelated background service) would
    ///     otherwise kill the whole launcher without any visible error. As a child process, the exit code can be reported.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryRunPatchGame(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "patch-game", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int exitCode = 3;
        try
        {
            LaunchDiagnostics.SetupGlobalHandlers();
            LaunchDiagnostics.Write($"[patch-game] child started, gamePath='{args[1]}'");
            NitroxEntryPatch.ApplyViaReflection(args[1]).GetAwaiter().GetResult();
            LaunchDiagnostics.Write("[patch-game] NitroxEntryPatch.Apply finished without error");
            if (NitroxEntryPatch.IsPatchApplied(args[1]))
            {
                LaunchDiagnostics.Write("[patch-game] verified: Assembly-CSharp.dll contains the Nitrox entry call");
                exitCode = 0;
            }
            else
            {
                LaunchDiagnostics.Write("[patch-game] verification FAILED: Assembly-CSharp.dll does not contain the Nitrox entry call");
                exitCode = 4;
            }
        }
        catch (Exception ex)
        {
            LaunchDiagnostics.Write("[patch-game] NitroxEntryPatch.Apply threw an exception", ex);
        }

        LaunchDiagnostics.Write($"[patch-game] exiting with code {exitCode} (0x{exitCode:X8})");
        Environment.Exit(exitCode);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadAvalonia(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            App.HandleUnhandledException(ex);
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
#if DEBUG
        // https://github.com/wieslawsoltes/Svg.Skia?tab=readme-ov-file#avalonia-previewer
        GC.KeepAlive(typeof(SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
#endif

        return App.Create();
    }

    private static class AssemblyResolver
    {
        private static string? currentExecutableDirectory;
        private static readonly Dictionary<string, Assembly> cache = [];

        public static Assembly? Handler(object sender, ResolveEventArgs args)
        {
            static Assembly? ResolveFromLib(ReadOnlySpan<char> dllName)
            {
                dllName = dllName.Slice(0, dllName.IndexOf(','));
                if (!dllName.EndsWith(".dll"))
                {
                    dllName = string.Concat(dllName, ".dll");
                }

                if (dllName.EndsWith(".resources.dll"))
                {
                    return null;
                }

                string dllNameStr = dllName.ToString();

                string dllPath = Path.Combine(GetExecutableDirectory(), "lib", dllNameStr);
                if (!File.Exists(dllPath))
                {
                    dllPath = Path.Combine(GetExecutableDirectory(), dllNameStr);
                }

                try
                {
                    return Assembly.LoadFile(dllPath);
                }
                catch
                {
                    return null;
                }
            }

            if (!cache.TryGetValue(args.Name, out Assembly assembly))
            {
                cache[args.Name] = assembly = ResolveFromLib(args.Name);
                if (assembly == null && !args.Name.Contains(".resources"))
                {
                    cache[args.Name] = assembly = Assembly.Load(args.Name);
                }
            }

            return assembly;
        }

        private static string GetExecutableDirectory()
        {
            if (currentExecutableDirectory != null)
            {
                return currentExecutableDirectory;
            }
            string pathAttempt = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(pathAttempt))
            {
                using Process proc = Process.GetCurrentProcess();
                pathAttempt = proc.MainModule?.FileName;
            }
            return currentExecutableDirectory = new Uri(Path.GetDirectoryName(pathAttempt ?? ".") ?? Directory.GetCurrentDirectory()).LocalPath;
        }
    }
}
