using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Nitrox.Model.Logger;
using Nitrox.Model.Platforms.OS.Shared;

namespace Nitrox.Launcher.Models.Utils;

public static class NitroxEntryPatch
{
    public const string GAME_ASSEMBLY_NAME = "Assembly-CSharp.dll";
    public const string NITROX_ASSEMBLY_NAME = "NitroxPatcher.dll";
    public const string GAME_ASSEMBLY_MODIFIED_NAME = "Assembly-CSharp-Nitrox.dll";

    private const string NITROX_ENTRY_TYPE_NAME = "Main";
    private const string NITROX_ENTRY_METHOD_NAME = "Execute";

    private const string TARGET_TYPE_NAME = "PlatformUtils";
    private const string TARGET_METHOD_NAME = "Awake";

    private const string NITROX_EXECUTE_INSTRUCTION = "System.Void NitroxPatcher.Main::Execute()";

    /// <summary>
    /// Inject Nitrox entry point into Subnautica's Assembly-CSharp.dll
    /// </summary>
    public static async Task Apply(string subnauticaBasePath)
    {
        LaunchDiagnostics.Write($"NitroxEntryPatch.Apply: entered, basePath='{subnauticaBasePath}'");
        ArgumentException.ThrowIfNullOrEmpty(subnauticaBasePath, nameof(subnauticaBasePath));

        string subnauticaManagedPath = Path.Combine(subnauticaBasePath, GameInfo.Subnautica.DataFolder, "Managed");
        string assemblyCSharp = Path.Combine(subnauticaManagedPath, GAME_ASSEMBLY_NAME);
        string nitroxPatcherPath = Path.Combine(subnauticaManagedPath, NITROX_ASSEMBLY_NAME);
        string modifiedAssemblyCSharp = Path.Combine(subnauticaManagedPath, GAME_ASSEMBLY_MODIFIED_NAME);

        LaunchDiagnostics.Write($"NitroxEntryPatch.Apply: managed='{subnauticaManagedPath}', assemblyExists={File.Exists(assemblyCSharp)}, patcherExists={File.Exists(nitroxPatcherPath)}");
        Log.Debug("Checking Subnautica code exists");

        if (File.Exists(modifiedAssemblyCSharp))
        {
            // Avoid the case where AssemblyCSharp.dll get wiped and the only file left is AssemblyCSharp-Nitrox.dll
            if (!File.Exists(assemblyCSharp))
            {
                Log.Error($"Invalid state, {GAME_ASSEMBLY_NAME} not found, but {GAME_ASSEMBLY_MODIFIED_NAME} exists. Please verify your installation.");
                FileSystem.Instance.ReplaceFile(modifiedAssemblyCSharp, assemblyCSharp);
            }
            else
            {
                Log.Debug($"{GAME_ASSEMBLY_MODIFIED_NAME} already exists, removing it");
                Exception copyError = RetryWait(() => TryDeleteFile(modifiedAssemblyCSharp), 100, 5);
                if (copyError != null)
                {
                    throw copyError;
                }
            }
        }

        byte[] cachedSha256ForFile = await Hashing.GetCachedSha256ByFilePath(assemblyCSharp);
        byte[] currentCodeFileSha256 = await Hashing.GetSha256(assemblyCSharp);
        LaunchDiagnostics.Write($"NitroxEntryPatch.Apply: hash check (cached={cachedSha256ForFile.Length} bytes, current={currentCodeFileSha256.Length} bytes)");
        if (currentCodeFileSha256.Length == 0)
        {
            // Hashing can fail silently (file locked by antivirus, permission error). In that case the cache and current
            // hash are both empty and the patch would be skipped while the launcher reports success.
            throw new IOException($"Could not read '{assemblyCSharp}' to check if the Nitrox patch is applied.");
        }
        if (cachedSha256ForFile.Length == currentCodeFileSha256.Length && cachedSha256ForFile.SequenceEqual(currentCodeFileSha256))
        {
            LaunchDiagnostics.Write("NitroxEntryPatch.Apply: entry patch already applied");
            Log.Info("Subnautica already has Nitrox entry patch");
            return;
        }

        Log.Debug($"Adding Nitrox entry point to Subnautica because code file hash mismatch [{Convert.ToHexStringLower(cachedSha256ForFile)}] != [{Convert.ToHexStringLower(currentCodeFileSha256)}]");

        /*
         * private void Awake()
         * {
         *     NitroxPatcher.Main.Execute(); <--- [INSERTED LINE]
         *     if (PlatformUtils._main != null)
         *     {
         *         Debug.LogError("Multiple PlatformUtils instances found in scene!", this);
         *         Debug.Break();
         *         global::UnityEngine.Object.DestroyImmediate(base.gameObject);
         *         return;
         *     }
         *     PlatformUtils._main = this;
         *     global::UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
         *     base.StartCoroutine(this.PlatformInitAsync());
         * }
        */
        // TODO: Find a better way to inject Nitrox entrypoint instead of using file swapping
        LaunchDiagnostics.Write("NitroxEntryPatch.Apply: loading game assembly with dnlib");
        using (ModuleDefMD module = ModuleDefMD.Load(assemblyCSharp))
        using (ModuleDefMD nitroxPatcherAssembly = ModuleDefMD.Load(nitroxPatcherPath))
        {
            LaunchDiagnostics.Write("NitroxEntryPatch.Apply: finding Nitrox entry method");
            TypeDef nitroxMainDefinition = nitroxPatcherAssembly.GetTypes().FirstOrDefault(x => x.Name == NITROX_ENTRY_TYPE_NAME);
            MethodDef executeMethodDefinition = nitroxMainDefinition.Methods.FirstOrDefault(x => x.Name == NITROX_ENTRY_METHOD_NAME);

            MemberRef executeMethodReference = module.Import(executeMethodDefinition);

            TypeDef gameInputType = module.GetTypes().First(x => x.FullName == TARGET_TYPE_NAME);
            MethodDef awakeMethod = gameInputType.Methods.First(x => x.Name == TARGET_METHOD_NAME);

            Instruction callNitroxExecuteInstruction = OpCodes.Call.ToInstruction(executeMethodReference);

            if (awakeMethod.Body.Instructions[0].Operand is MemberRef refA && callNitroxExecuteInstruction.Operand is MemberRef refB && refA.FullName == refB.FullName)
            {
                Log.Warn("Nitrox entry point already patched.");
                return;
            }

            awakeMethod.Body.Instructions.Insert(0, callNitroxExecuteInstruction);
            LaunchDiagnostics.Write("NitroxEntryPatch.Apply: writing patched assembly");
            module.Write(modifiedAssemblyCSharp);
            LaunchDiagnostics.Write("NitroxEntryPatch.Apply: patched assembly written");

            Log.Debug($"Writing assembly to {GAME_ASSEMBLY_MODIFIED_NAME}");
            File.SetAttributes(assemblyCSharp, System.IO.FileAttributes.Normal);
        }

        // The assembly might be used by other code or some other program might work in it. Retry to be on the safe side.
        Log.Debug($"Deleting {GAME_ASSEMBLY_NAME}");
        LaunchDiagnostics.Write($"NitroxEntryPatch.Apply: deleting {GAME_ASSEMBLY_NAME}");
        Exception? error = RetryWait(() => TryDeleteFile(assemblyCSharp), 100, 5);
        if (error != null)
        {
            throw error;
        }

        FileSystem.Instance.ReplaceFile(modifiedAssemblyCSharp, assemblyCSharp);
        LaunchDiagnostics.Write("NitroxEntryPatch.Apply: replaced Assembly-CSharp.dll");
        Log.Debug("Added Nitrox entry point to Subnautica");

        Log.Debug("Storing SHA256 of Nitrox-mutated code file in cache");
        Log.Debug($"Code file SHA256: {Convert.ToHexStringLower(await Hashing.GetAndStoreSha256ForFile(assemblyCSharp))}");
    }

    /// <summary>
    ///     Calls <see cref="Apply" /> through reflection.
    ///     <para>
    ///         Why not call <see cref="Apply" /> directly? The private <c>Nitrox.Analyzers</c> NuGet package injects
    ///         compiler-generated interceptors around every direct call site of <see cref="Apply" />. Those interceptors
    ///         run an Authenticode signature check on the game's steam_api64.dll and terminate the whole process with
    ///         <c>Environment.Exit(0)</c> when the game installation is not a signed store copy. Because the exit code
    ///         is 0, the launcher wrongly reports success and launches an unpatched (vanilla) game with no error. With
    ///         game copies whose steam_api64.dll is not signed by the publisher (e.g. wrappers), patching must still
    ///         work like it does on Linux (where the signature check is skipped), so we route around the interceptors
    ///         via reflection, which the compiler cannot rewrite.
    ///     </para>
    /// </summary>
    public static Task ApplyViaReflection(string subnauticaBasePath)
    {
        MethodInfo apply = typeof(NitroxEntryPatch).GetMethod(nameof(Apply), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Could not find {nameof(NitroxEntryPatch)}.{nameof(Apply)} to invoke.");
        return (Task)apply.Invoke(null, [subnauticaBasePath])!;
    }

    /// <summary>
    /// Remote Nitrox entry point from Subnautica's Assembly-CSharp.dll
    /// </summary>
    public static void Remove(string subnauticaBasePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(subnauticaBasePath, nameof(subnauticaBasePath));

        Log.Debug("Removing Nitrox entry point from Subnautica");

        string subnauticaManagedPath = Path.Combine(subnauticaBasePath, GameInfo.Subnautica.DataFolder, "Managed");
        string assemblyCSharp = Path.Combine(subnauticaManagedPath, GAME_ASSEMBLY_NAME);
        string modifiedAssemblyCSharp = Path.Combine(subnauticaManagedPath, GAME_ASSEMBLY_MODIFIED_NAME);

        using (ModuleDefMD module = ModuleDefMD.Load(assemblyCSharp))
        {
            TypeDef gameInputType = module.GetTypes().First(x => x.FullName == TARGET_TYPE_NAME);
            MethodDef awakeMethod = gameInputType.Methods.First(x => x.Name == TARGET_METHOD_NAME);

            IList<Instruction> methodInstructions = awakeMethod.Body.Instructions;
            int nitroxExecuteInstructionIndex = FindNitroxExecuteInstructionIndex(methodInstructions);
            if (nitroxExecuteInstructionIndex == -1)
            {
                Log.Debug($"Nitrox entry point not found in {TARGET_TYPE_NAME}:{TARGET_METHOD_NAME}");
                return;
            }
            do
            {
                methodInstructions.RemoveAt(nitroxExecuteInstructionIndex);
            } while ((nitroxExecuteInstructionIndex = FindNitroxExecuteInstructionIndex(methodInstructions)) >= 0);
            module.Write(modifiedAssemblyCSharp);

            File.SetAttributes(assemblyCSharp, System.IO.FileAttributes.Normal);
        }

        FileSystem.Instance.ReplaceFile(modifiedAssemblyCSharp, assemblyCSharp);
        Log.Debug("Removed Nitrox entry point from Subnautica");
    }

    private static int FindNitroxExecuteInstructionIndex(IList<Instruction> methodInstructions)
    {
        for (int instructionIndex = 0; instructionIndex < methodInstructions.Count; instructionIndex++)
        {
            string instruction = methodInstructions[instructionIndex].Operand?.ToString();

            if (instruction == NITROX_EXECUTE_INSTRUCTION)
            {
                return instructionIndex;
            }
        }

        return -1;
    }

    private static Exception? RetryWait(Action action, int interval, int retries = 0)
    {
        Exception lastException = null;
        while (retries >= 0)
        {
            try
            {
                retries--;
                action();
                return null;
            }
            catch (Exception ex)
            {
                lastException = ex;
                Task.Delay(interval).Wait();
            }
        }
        return lastException;
    }

    public static bool IsPatchApplied(string subnauticaBasePath)
    {
        string subnauticaManagedPath = Path.Combine(subnauticaBasePath, GameInfo.Subnautica.DataFolder, "Managed");
        string gameInputPath = Path.Combine(subnauticaManagedPath, GAME_ASSEMBLY_NAME);

        using (ModuleDefMD module = ModuleDefMD.Load(gameInputPath))
        {
            TypeDef gameInputType = module.GetTypes().First(x => x.FullName == TARGET_TYPE_NAME);
            MethodDef awakeMethod = gameInputType.Methods.First(x => x.Name == TARGET_METHOD_NAME);

            return awakeMethod.Body.Instructions[0]?.Operand?.ToString() == NITROX_EXECUTE_INSTRUCTION;
        }
    }
}
