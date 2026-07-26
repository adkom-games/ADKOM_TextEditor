#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Enables the semantics module's compilation gate. The module's asmdef is
    /// constrained on ADKOM_TE_ROSLYN so it only compiles when a Roslyn
    /// (Microsoft.CodeAnalysis.CSharp) assembly exists in the project; this
    /// bootstrap detects Roslyn and sets the define. Without Roslyn the module
    /// stays dormant and the editor falls back to heuristic highlighting.
    /// </summary>
    [InitializeOnLoad]
    static class SemanticModuleBootstrap
    {
        const string Define = "ADKOM_TE_ROSLYN";

        static SemanticModuleBootstrap()
        {
            EditorApplication.delayCall += EnsureDefine;
        }

        static void EnsureDefine()
        {
            bool present = System.AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "Microsoft.CodeAnalysis.CSharp");
            if (!present) return;

            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            if (defines.Split(';').Contains(Define)) return;
            PlayerSettings.SetScriptingDefineSymbols(target,
                string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);
            UnityEngine.Debug.Log("[ADKOM Text Editor] Roslyn detected — enabling the semantics module (" + Define + ").");
        }
    }
}
#endif
