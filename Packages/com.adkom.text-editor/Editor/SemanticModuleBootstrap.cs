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
    /// Go to Definition). Everything ships inside the main package; when the
    /// user consents (Settings toggle or the first-use dialog), this copies
    /// the bundled Roslyn assemblies into Assets/Plugins — only if the
    /// project has no Roslyn already — and sets the compile-gate define for
    /// the semantics asmdef. Steps that recompile resume on the next load.
    /// Disabling the setting only gates the features — nothing is
    /// uninstalled.
    /// </summary>
    [InitializeOnLoad]
    public static class SemanticSetup
    {
        const string Define = "ADKOM_TE_ROSLYN";
        const string PackageName = "com.adkom.text-editor";
        const string RoslynDestDir = "Assets/Plugins/ADKOM.TextEditor/Roslyn";

        static SemanticSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorConfig.SemanticsEnabled) EnsureInstalled(silent: true);
            };
        }

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
            var pkg = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == PackageName);
            if (pkg == null) return;
            string src = Path.Combine(pkg.resolvedPath, "RoslynBinaries~");
            if (!Directory.Exists(src))
            {
                AteConsole.Error("[ADKOM Text Editor] Bundled Roslyn binaries missing (RoslynBinaries~). Reinstall the package.");
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
                "(MIT-licensed, © .NET Foundation — see the package's THIRD-PARTY-NOTICES.md).");
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
