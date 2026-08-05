#if UNITY_EDITOR
using System.Linq;
using UnityEditor;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// The GitHub build (com.adkom.text-editor) and the Asset Store build
    /// (com.adkomgames.text-editor) are the same editor under two package
    /// names. Installed together they define the same assemblies and the
    /// project stops compiling with a bare "assembly already exists" error
    /// that names neither cause nor cure — so detect the pairing and say
    /// exactly what to do. Error, not Warn: it must be visible in Unity's
    /// console with no ATE window open (and if compilation already failed,
    /// this code may only run from the last good domain — either way the
    /// message lands).
    /// </summary>
    [InitializeOnLoad]
    static class AteDuplicateInstallCheck
    {
        static AteDuplicateInstallCheck()
        {
            EditorApplication.delayCall += () =>
            {
                var names = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Select(p => p.name).ToHashSet();
                if (names.Contains("com.adkom.text-editor") && names.Contains("com.adkomgames.text-editor"))
                    AteConsole.Error("[ADKOM Text Editor] Both the GitHub build (com.adkom.text-editor) and the " +
                        "Asset Store build (com.adkomgames.text-editor) are installed. They are the same editor and " +
                        "cannot coexist — remove one of the two in Window → Package Manager.");
            };
        }
    }
}
#endif
