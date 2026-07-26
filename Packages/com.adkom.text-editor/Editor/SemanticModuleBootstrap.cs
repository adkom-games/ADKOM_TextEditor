#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Orchestrates the opt-in semantic features (compiler-accurate colors and
    /// Go to Definition). When the user enables them, this installs whatever is
    /// missing, in order: the companion package (UPM git URL), the bundled
    /// Roslyn assemblies (copied into Assets/Plugins only if the project has
    /// no Roslyn already), and the compile-gate define for the module's
    /// asmdef. Each step triggers a reload; the bootstrap resumes on load
    /// until everything is present. Disabling the setting only gates the
    /// features — nothing is uninstalled.
    /// </summary>
    [InitializeOnLoad]
    public static class SemanticSetup
    {
        const string Define = "ADKOM_TE_ROSLYN";
        const string ModuleName = "com.adkom.text-editor.semantics";
        const string ModuleGitUrl = "https://github.com/adkom-games/ADKOM_TextEditor.git#upm-semantics";
        const string RoslynDestDir = "Assets/Plugins/ADKOM.TextEditor/Roslyn";

        static SemanticSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorConfig.SemanticsEnabled) EnsureInstalled(silent: true);
            };
        }

        public static bool ModuleInstalled =>
            UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Any(p => p.name == ModuleName);

        public static bool RoslynPresent =>
            System.AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "Microsoft.CodeAnalysis.CSharp");

        public static bool Ready =>
            EditorConfig.SemanticsEnabled && SemanticServices.Provider != null;

        /// <summary>Drives installation as far as currently possible; steps
        /// that trigger package resolution or recompiles complete after the
        /// reload, where the bootstrap picks up again.</summary>
        public static void EnsureInstalled(bool silent = false)
        {
            if (!ModuleInstalled)
            {
                AteConsole.Info("[ADKOM Text Editor] Installing the semantics module via UPM…");
                UnityEditor.PackageManager.Client.Add(ModuleGitUrl);
                return; // resumes after the package resolves and reloads
            }
            if (!RoslynPresent)
            {
                CopyBundledRoslyn();
                return; // resumes after the plugin import reloads
            }
            EnsureDefine();
            if (!silent && SemanticServices.Provider == null)
                AteConsole.Info("[ADKOM Text Editor] Semantics module compiling — features available shortly.");
        }

        static void CopyBundledRoslyn()
        {
            var module = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == ModuleName);
            if (module == null) return;
            string src = Path.Combine(module.resolvedPath, "RoslynBinaries~");
            if (!Directory.Exists(src))
            {
                AteConsole.Error("[ADKOM Text Editor] Semantics module has no bundled Roslyn binaries (RoslynBinaries~ missing). Update the module.");
                return;
            }
            Directory.CreateDirectory(RoslynDestDir);
            int copied = 0;
            foreach (var dll in Directory.GetFiles(src, "*.dll"))
            {
                string dst = Path.Combine(RoslynDestDir, Path.GetFileName(dll));
                if (File.Exists(dst)) continue;
                File.Copy(dll, dst);
                copied++;
            }
            AteConsole.Info($"[ADKOM Text Editor] Installed {copied} bundled Roslyn assemblies to {RoslynDestDir} " +
                "(MIT-licensed, © .NET Foundation — see the module's THIRD-PARTY-NOTICES.md).");
            AssetDatabase.Refresh();
        }

        static void EnsureDefine()
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            if (defines.Split(';').Contains(Define)) return;
            PlayerSettings.SetScriptingDefineSymbols(target,
                string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);
            AteConsole.Info("[ADKOM Text Editor] Enabling the semantics module (" + Define + ").");
        }
    }
}
#endif
