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
        const string RoslynDestDir = "Assets/Plugins/ADKOM.TextEditor/Roslyn";

        const string ObsoleteModuleName = "com.adkom.text-editor.semantics";

        static SemanticSetup()
        {
            EditorApplication.delayCall += () =>
            {
                RemoveObsoleteModule();
                if (EditorConfig.SemanticsEnabled) EnsureInstalled(silent: true);
            };
        }

        /// <summary>The pre-0.6.0 companion module defines the same-named
        /// assembly the main package now ships; leaving both installed breaks
        /// compilation. Remove it automatically where that is allowed — the
        /// Asset Store build may not change a user's package set, so there it
        /// only says what to remove.</summary>
        static void RemoveObsoleteModule()
        {
            if (!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Any(p => p.name == ObsoleteModuleName))
                return;

            if (!AtePackageInstaller.Supported)
            {
                // Error, not Warn: this one blocks compilation project-wide,
                // so it has to be visible with no ATE window open.
                AteConsole.Error("[ADKOM Text Editor] The obsolete semantics module package (" + ObsoleteModuleName +
                    ") is installed and defines the same assembly this package now ships — compilation will keep " +
                    "failing until you remove 'ADKOM Text Editor — Semantics Module' in Window → Package Manager.");
                return;
            }

            AteConsole.Info("[ADKOM Text Editor] Removing the obsolete semantics module package (its features now ship in the main package)…");
            AtePackageInstaller.Remove(ObsoleteModuleName, (ok, message) =>
            {
                if (ok)
                    AteConsole.Info("[ADKOM Text Editor] Obsolete semantics module removed.");
                else
                    // Same blocking condition: the removal that would have
                    // fixed compilation did not happen.
                    AteConsole.Error("[ADKOM Text Editor] Could not remove the obsolete semantics module (" + message +
                        ") — remove 'ADKOM Text Editor — Semantics Module' in the Package Manager.");
            });
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
            // Repair path for installs made before importer settings were
            // forced: a DLL left on Unity's "Any Platform" default would ship
            // 14 MB of Roslyn in the user's player builds.
            EnforceEditorOnlyImport();
            EnsureDefine();
            if (!silent && SemanticServices.Provider == null)
                AteConsole.Info("[ADKOM Text Editor] Semantics module compiling — features available shortly.");
        }

        static void CopyBundledRoslyn()
        {
            // By assembly, never by package name: the Asset Store build ships
            // under a different name (com.adkomgames.text-editor), and a
            // hardcoded lookup would silently break Roslyn installation there.
            var pkg = AtePackage.Info;
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
            EnforceEditorOnlyImport();
        }

        /// <summary>Marks every installed Roslyn DLL editor-only. A managed
        /// plugin under Assets/ defaults to "Any Platform" (verified on a
        /// fresh copy), which would include all ~14 MB of Roslyn in the
        /// user's player builds — the package's whole promise is that nothing
        /// it does reaches a build. Runs after every install AND on every
        /// semantics bootstrap, so installs made by older versions are
        /// repaired the next time the editor loads.</summary>
        static void EnforceEditorOnlyImport()
        {
            if (!Directory.Exists(RoslynDestDir)) return;
            foreach (var dll in Directory.GetFiles(RoslynDestDir, "*.dll"))
            {
                string assetPath = dll.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(assetPath) is PluginImporter importer)) continue;
                if (!importer.GetCompatibleWithAnyPlatform() && importer.GetCompatibleWithEditor())
                    continue; // already correct — don't dirty the import
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SaveAndReimport();
                AteConsole.Log("[ADKOM Text Editor] Marked " + Path.GetFileName(dll) +
                    " editor-only so it can never reach a player build.");
            }
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
