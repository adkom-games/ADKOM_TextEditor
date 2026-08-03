#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.CodeEditor;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Enumerates the IExternalCodeEditor implementations Unity has
    /// registered, so ATE can forward what it cannot handle (solutions,
    /// binaries, project sync) to the user's chosen fallback editor.
    ///
    /// Unity exposes no public accessor for that list, so this reads the
    /// private field — which Asset Store submissions may not do. CI replaces
    /// this file on the upm-store branch with a variant that returns nothing;
    /// callers already degrade to EditorUtility.OpenWithDefaultApp, and
    /// project sync is then left to Unity's own IDE packages.
    /// </summary>
    internal static class AteCodeEditorRegistry
    {
        internal static IEnumerable<IExternalCodeEditor> Registered()
        {
            if (AteBuildFlavor.AssetStore) return Enumerable.Empty<IExternalCodeEditor>();

            // The registry list is private; probe the known field names used by
            // Unity's CodeEditor across versions.
            foreach (var name in new[] { "m_ExternalCodeEditors", "externalCodeEditors" })
            {
                var fi = typeof(CodeEditor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi?.GetValue(CodeEditor.Editor) is IEnumerable<IExternalCodeEditor> list)
                    return list.ToArray();
            }
            return Enumerable.Empty<IExternalCodeEditor>();
        }
    }
}
#endif
