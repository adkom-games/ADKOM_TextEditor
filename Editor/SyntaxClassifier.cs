#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace ADKOM.TextEditor
{
    /// <summary>Semantic classes a span of source text can belong to.</summary>
    public enum TokenClass
    {
        Default, Keyword, String, Comment, Number, Preprocessor,
        Type, Method, Variable, Parameter
    }

    /// <summary>A classified run of characters on a single line.</summary>
    public struct SyntaxSpan
    {
        public int Line;    // 0-based
        public int Start;   // column, 0-based
        public int Length;
        public TokenClass Class;

        public SyntaxSpan(int line, int start, int length, TokenClass cls)
        {
            Line = line; Start = start; Length = length; Class = cls;
        }
    }

    /// <summary>Produces classified spans for a document. Implementations must
    /// be fast enough to run synchronously on every keystroke (heuristic
    /// lexing); compiler-accurate spans arrive later via ISemanticProvider.</summary>
    public interface ISyntaxClassifier
    {
        string Name { get; }
        List<SyntaxSpan> Classify(IReadOnlyList<string> lines);
    }

    /// <summary>Optional classifier capability: language keywords offered by
    /// the autocomplete popup alongside harvested document words.</summary>
    public interface ICompletionKeywords
    {
        IEnumerable<string> CompletionKeywords { get; }
    }

    /// <summary>Maps a file path to its language classifier (null = plain).</summary>
    public static class SyntaxClassifiers
    {
        static readonly CSharpClassifier CSharp = new CSharpClassifier();
        static readonly MarkdownClassifier Markdown = new MarkdownClassifier();
        static readonly JsonClassifier Json = new JsonClassifier();
        static readonly ShaderClassifier Shader = new ShaderClassifier();

        public static ISyntaxClassifier ForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".cs" => CSharp,
                ".md" => Markdown,
                ".json" => Json,
                ".asmdef" => Json,
                ".asmref" => Json,
                ".shader" => Shader,
                ".hlsl" => Shader,
                ".cginc" => Shader,
                ".compute" => Shader,
                _ => null
            };
        }
    }

    /// <summary>
    /// Optional compiler-backed semantics, implemented by the companion
    /// package (com.adkom.text-editor.semantics) on top of Roslyn. Discovered
    /// via TypeCache; absent implementations degrade gracefully to heuristics
    /// and disable navigation.
    /// </summary>
    public interface ISemanticProvider
    {
        string Name { get; }

        /// <summary>Full-fidelity classification of the document (replaces the
        /// heuristic spans). May be called from a background thread.</summary>
        bool TryGetClassifiedSpans(string path, string text, out List<SyntaxSpan> spans);

        /// <summary>Finds the definition of the symbol at <paramref name="offset"/>.
        /// Returns true with defPath == null when the symbol lives in metadata
        /// (referenced assembly); metadataOrigin then names the assembly.</summary>
        bool TryFindDefinition(string path, string text, int offset,
            out string defPath, out int line, out int column, out string metadataOrigin);

        /// <summary>For a symbol defined in metadata, generates a C# signature
        /// stub of its containing type ("from metadata" view). line is the
        /// 0-based line of the requested symbol within the stub.</summary>
        bool TryGetMetadataSource(string path, string text, int offset,
            out string title, out string source, out int line);
    }

    /// <summary>One IntelliSense completion candidate.</summary>
    public struct CompletionItem
    {
        public string Insert;   // text inserted on accept
        public string Display;  // list label (usually == Insert)
        public string Detail;   // dimmed signature/type hint (may be null)
        public TokenClass Kind; // colors the label like the code it completes
        public string Snippet;  // non-null: accepting expands this snippet body
    }

    /// <summary>Optional provider capability: compiler-accurate completions
    /// (IntelliSense) — scope symbols at a position, or the members of the
    /// expression before a '.'. May be called from a background thread.</summary>
    public interface ISemanticCompletion
    {
        /// <summary>All candidates at <paramref name="offset"/> (the caret /
        /// word start). The caller filters by the typed prefix as it grows, so
        /// one query serves the whole word.</summary>
        bool TryGetCompletions(string path, string text, int offset, out List<CompletionItem> items);
    }

    /// <summary>One compiler diagnostic anchored to a span of one line.</summary>
    public struct DiagnosticItem
    {
        public int Line;      // 0-based
        public int Start;     // column, 0-based
        public int Length;    // >= 1
        public bool IsError;  // else warning
        public string Message; // "CS0103: The name 'x' does not exist ..."
    }

    /// <summary>Optional provider capability: live compiler diagnostics for a
    /// document (errors + warnings). May run on a background thread.</summary>
    public interface ISemanticDiagnostics
    {
        bool TryGetDiagnostics(string path, string text, out List<DiagnosticItem> items);
    }

    /// <summary>One generatable member for the override picker.</summary>
    public struct GenerationCandidate
    {
        public string Label; // e.g. "protected override void OnDestroy()"
        public string Stub;  // ready-to-insert member text ($END$ inside)
    }

    /// <summary>Optional provider capability: code generation context —
    /// what the class at a position already declares, and what it could
    /// override. Called on the main thread (one-shot menu interactions).</summary>
    public interface ISemanticGeneration
    {
        /// <summary>The class containing <paramref name="offset"/>: its
        /// directly-declared method names and whether it derives from
        /// UnityEngine.MonoBehaviour. False when no class is at the caret.</summary>
        bool TryGetTypeContext(string path, string text, int offset,
            out HashSet<string> declaredMethods, out bool isMonoBehaviour);

        /// <summary>Overridable members (methods and properties) of the base
        /// chain of the class at <paramref name="offset"/> that are not yet
        /// overridden there, each with a ready-to-insert stub.</summary>
        bool TryGetOverrideCandidates(string path, string text, int offset,
            out List<GenerationCandidate> candidates);
    }

    /// <summary>One occurrence of the symbol under the caret, for read/write
    /// reference highlighting.</summary>
    public struct SymbolOccurrence
    {
        public int Start, Length; // document offsets
        public bool IsWrite;      // assignment target / declaration / ref-out
    }

    /// <summary>Optional provider capability: all occurrences of the symbol at
    /// a position WITHIN the document, classified read vs write. May run on a
    /// background thread.</summary>
    public interface ISemanticOccurrences
    {
        bool TryGetOccurrences(string path, string text, int offset, out List<SymbolOccurrence> occurrences);
    }

    /// <summary>One reference to a symbol, for the Find All References list.</summary>
    public struct SymbolReference
    {
        public string Path;     // source file (may be the queried document)
        public int Line;        // 0-based
        public int Column;      // 0-based
        public string LineText; // trimmed preview
    }

    /// <summary>Optional provider capability: reference listing and in-file
    /// rename spans (workspace-free — scoped to the symbol's assembly).</summary>
    public interface ISemanticRefactorings
    {
        /// <summary>All references to the symbol at offset across the
        /// document's assembly sources. May run on a background thread.</summary>
        bool TryFindReferences(string path, string text, int offset, out List<SymbolReference> refs);

        /// <summary>Spans (start, length) of the symbol's occurrences WITHIN
        /// this document, for rename; also reports the current name.</summary>
        bool TryGetRenameSpans(string path, string text, int offset,
            out List<(int start, int length)> spans, out string symbolName);
    }

    public static class SemanticServices
    {
        static ISemanticProvider _provider;
        static bool _searched;

        public static ISemanticProvider Provider
        {
            get
            {
                if (_searched) return _provider;
                _searched = true;
                foreach (var t in TypeCache.GetTypesDerivedFrom<ISemanticProvider>())
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    try { _provider = (ISemanticProvider)System.Activator.CreateInstance(t); break; }
                    catch (System.Exception ex)
                    {
                        AteConsole.Warn("[ADKOM Text Editor] Semantic provider failed to load: " + ex.Message);
                    }
                }
                return _provider;
            }
        }
    }
}
#endif
