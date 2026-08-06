#if UNITY_EDITOR
using System;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Asset Store variant: refuses, and never references
    /// UnityEditor.PackageManager at all. Submissions must not add, update or
    /// remove packages in a user's project, so the store artifact carries no
    /// code that could — callers check <see cref="Supported"/> and tell the
    /// user what to do in the Package Manager instead.
    /// </summary>
    internal static class AtePackageInstaller
    {
        internal static bool Supported => false;

        internal static void Install(string gitUrl, string version, Action<bool, string> completed) =>
            completed?.Invoke(false, "this build does not install packages");

        internal static void Remove(string packageName, Action<bool, string> completed) =>
            completed?.Invoke(false, "this build does not remove packages");
    }
}
#endif
