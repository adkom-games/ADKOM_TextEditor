#if UNITY_EDITOR
using UnityEditor.PackageManager;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// The one place that knows which package ATE is running from. Everything
    /// that needs a path into the package goes through here, resolved from
    /// the ASSEMBLY rather than a hardcoded package name — the Asset Store
    /// build ships under a different name (com.adkomgames.text-editor vs the
    /// GitHub build's com.adkom.text-editor), and any name-based lookup would
    /// silently break one flavor or the other.
    /// </summary>
    internal static class AtePackage
    {
        static PackageInfo _info;
        static bool _resolved;

        internal static PackageInfo Info
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;   // PackageInfo is stable per domain load
                    _info = PackageInfo.FindForAssembly(typeof(AtePackage).Assembly);
                }
                return _info;
            }
        }

        /// <summary>Virtual root ("Packages/&lt;name&gt;") for AssetDatabase
        /// loads. Falls back to the GitHub name so a broken resolve degrades
        /// to the old behavior instead of a null path.</summary>
        internal static string AssetRoot => Info?.assetPath ?? "Packages/com.adkom.text-editor";

        /// <summary>Absolute folder on disk, for file IO. Null when the
        /// package cannot be resolved.</summary>
        internal static string DiskRoot => Info?.resolvedPath;
    }
}
#endif
