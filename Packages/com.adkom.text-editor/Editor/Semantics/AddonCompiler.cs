#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ADKOM.TextEditor.Scripting;

namespace ADKOM.TextEditor.Semantics
{
    /// <summary>
    /// Compiles one addon source file into an in-memory assembly with the
    /// bundled Roslyn, referencing everything the current editor domain has
    /// loaded (so addons can use AteApi, UnityEngine, UnityEditor, and the
    /// BCL). Discovered by AteAddonManager via TypeCache.
    /// </summary>
    public class AddonCompiler : IAddonCompiler
    {
        static List<MetadataReference> _refs;

        public bool TryCompile(string path, out Assembly assembly, out string[] errors)
        {
            assembly = null;
            try
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path),
                    new CSharpParseOptions(LanguageVersion.Latest), path);
                var compilation = CSharpCompilation.Create(
                    "AteAddon_" + Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N"),
                    new[] { tree }, References,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using (var ms = new MemoryStream())
                {
                    var result = compilation.Emit(ms);
                    if (!result.Success)
                    {
                        var errs = new List<string>();
                        foreach (var d in result.Diagnostics)
                            if (d.Severity == DiagnosticSeverity.Error)
                            {
                                var pos = d.Location.GetLineSpan();
                                errs.Add(Path.GetFileName(path) + ":" + (pos.StartLinePosition.Line + 1) +
                                    ": " + d.GetMessage());
                            }
                        errors = errs.ToArray();
                        return false;
                    }
                    assembly = Assembly.Load(ms.ToArray());
                    errors = Array.Empty<string>();
                    return true;
                }
            }
            catch (Exception ex)
            {
                errors = new[] { ex.Message };
                return false;
            }
        }

        static IReadOnlyList<MetadataReference> References
        {
            get
            {
                if (_refs != null) return _refs;
                _refs = new List<MetadataReference>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
                        if (!File.Exists(asm.Location) || !seen.Add(asm.Location)) continue;
                        _refs.Add(MetadataReference.CreateFromFile(asm.Location));
                    }
                    catch (Exception) { }
                }
                return _refs;
            }
        }
    }
}
#endif
