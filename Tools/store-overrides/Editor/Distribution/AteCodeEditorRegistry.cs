#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.CodeEditor;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Asset Store variant: returns nothing, because Unity exposes no public
    /// accessor for its registered external code editors and submissions may
    /// not read the private one.
    ///
    /// Consequence for this build: with ATE selected as the External Script
    /// Editor, files ATE cannot open go to the OS default application instead
    /// of a fallback IDE, and .csproj/.sln generation is left to Unity's own
    /// IDE packages. Users who need Rider/Visual Studio project sync should
    /// keep that IDE as the External Script Editor — ATE still opens text
    /// assets from the Project window either way.
    /// </summary>
    internal static class AteCodeEditorRegistry
    {
        internal static IEnumerable<IExternalCodeEditor> Registered() =>
            Enumerable.Empty<IExternalCodeEditor>();
    }
}
#endif
