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

        public static ISyntaxClassifier ForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".cs" => CSharp,
                ".md" => Markdown,
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
