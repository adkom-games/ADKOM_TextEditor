#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using UnityEditor;
using UnityEditor.Compilation;

namespace ADKOM.TextEditor.Semantics
{
    /// <summary>
    /// Compiler-backed ISemanticProvider using Roslyn. Builds a real
    /// CSharpCompilation for the Unity assembly containing the requested file
    /// (sources, defines, and references straight from Unity's
    /// CompilationPipeline), caches it, and answers classification and
    /// go-to-definition queries from background threads.
    /// </summary>
    public sealed class RoslynSemanticProvider : ADKOM.TextEditor.ISemanticProvider
    {
        public string Name => "Roslyn";

        // --- Unity assembly snapshot (CompilationPipeline is main-thread; the
        // data is captured at load / after each compilation for bg use) ---

        class AsmInfo
        {
            public string Name;
            public HashSet<string> Sources; // normalized full paths
            public string[] SourceList;
            public string[] Defines;
            public string[] References;
        }

        static volatile List<AsmInfo> _assemblies;

        [InitializeOnLoadMethod]
        static void CaptureAssemblies()
        {
            void Capture()
            {
                try
                {
                    _assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                        .Select(a => new AsmInfo
                        {
                            Name = a.name,
                            SourceList = a.sourceFiles,
                            Sources = new HashSet<string>(a.sourceFiles.Select(Norm)),
                            Defines = a.defines,
                            References = a.allReferences
                        }).ToList();
                    lock (_lock) _cache.Clear(); // assemblies changed; drop compilations
                }
                catch (Exception) { }
            }
            Capture();
            CompilationPipeline.compilationFinished += _ => Capture();
        }

        static string Norm(string p) => Path.GetFullPath(p).Replace('\\', '/');

        // --- Compilation cache ---

        class Cached
        {
            public CSharpCompilation Comp;
            public Dictionary<string, SyntaxTree> Trees; // by normalized path
            public CSharpParseOptions Parse;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<string, Cached> _cache = new Dictionary<string, Cached>();

        Cached GetCompilation(string path)
        {
            string norm = Norm(path);
            var asms = _assemblies;
            if (asms == null) return null;
            var asm = asms.FirstOrDefault(a => a.Sources.Contains(norm));
            if (asm == null) return null;

            lock (_lock)
            {
                if (_cache.TryGetValue(asm.Name, out var hit)) return hit;
            }

            var parse = new CSharpParseOptions(LanguageVersion.Latest,
                preprocessorSymbols: asm.Defines);
            var trees = new Dictionary<string, SyntaxTree>();
            foreach (var src in asm.SourceList)
            {
                try
                {
                    string full = Norm(src);
                    trees[full] = CSharpSyntaxTree.ParseText(File.ReadAllText(full), parse, full);
                }
                catch (Exception) { }
            }
            var refs = new List<MetadataReference>();
            foreach (var r in asm.References.Distinct())
            {
                try { if (File.Exists(r)) refs.Add(MetadataReference.CreateFromFile(r)); }
                catch (Exception) { }
            }
            var comp = CSharpCompilation.Create(asm.Name, trees.Values, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var cached = new Cached { Comp = comp, Trees = trees, Parse = parse };
            lock (_lock) _cache[asm.Name] = cached;
            return cached;
        }

        (SemanticModel model, SyntaxTree tree) GetModel(string path, string text)
        {
            var cached = GetCompilation(path);
            if (cached == null) return (null, null);
            string norm = Norm(path);
            cached.Trees.TryGetValue(norm, out var oldTree);
            var newTree = CSharpSyntaxTree.ParseText(text, cached.Parse, norm);
            CSharpCompilation comp;
            if (oldTree != null && cached.Comp.SyntaxTrees.Contains(oldTree))
                comp = cached.Comp.ReplaceSyntaxTree(oldTree, newTree);
            else
                comp = cached.Comp.AddSyntaxTrees(newTree);
            return (comp.GetSemanticModel(newTree), newTree);
        }

        // --- Classification ---

        public bool TryGetClassifiedSpans(string path, string text, out List<SyntaxSpan> spans)
        {
            spans = null;
            var (model, tree) = GetModel(path, text);
            if (model == null) return false;

            var result = new List<SyntaxSpan>(512);
            var srcText = tree.GetText();
            var root = tree.GetRoot();

            foreach (var trivia in root.DescendantTrivia())
            {
                var kind = trivia.Kind();
                if (kind == SyntaxKind.SingleLineCommentTrivia || kind == SyntaxKind.MultiLineCommentTrivia ||
                    kind == SyntaxKind.SingleLineDocumentationCommentTrivia || kind == SyntaxKind.MultiLineDocumentationCommentTrivia)
                    AddSpan(result, srcText, trivia.Span, TokenClass.Comment);
                else if (trivia.IsDirective)
                    AddSpan(result, srcText, trivia.Span, TokenClass.Preprocessor);
            }

            foreach (var token in root.DescendantTokens())
            {
                var kind = token.Kind();
                if (SyntaxFacts.IsKeywordKind(kind))
                {
                    AddSpan(result, srcText, token.Span, TokenClass.Keyword);
                    continue;
                }
                if (kind == SyntaxKind.StringLiteralToken || kind == SyntaxKind.CharacterLiteralToken ||
                    kind == SyntaxKind.InterpolatedStringTextToken || kind == SyntaxKind.InterpolatedStringStartToken ||
                    kind == SyntaxKind.InterpolatedStringEndToken || kind == SyntaxKind.InterpolatedVerbatimStringStartToken)
                {
                    AddSpan(result, srcText, token.Span, TokenClass.String);
                    continue;
                }
                if (kind == SyntaxKind.NumericLiteralToken)
                {
                    AddSpan(result, srcText, token.Span, TokenClass.Number);
                    continue;
                }
                if (kind != SyntaxKind.IdentifierToken) continue;

                var symbol = ResolveSymbol(model, token);
                var cls = Map(symbol);
                if (cls != TokenClass.Default)
                    AddSpan(result, srcText, token.Span, cls);
            }
            spans = result;
            return true;
        }

        static ISymbol ResolveSymbol(SemanticModel model, SyntaxToken token)
        {
            var node = token.Parent;
            if (node == null) return null;
            var declared = model.GetDeclaredSymbol(node);
            if (declared != null) return declared;
            var info = model.GetSymbolInfo(node);
            return info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        }

        static TokenClass Map(ISymbol s)
        {
            if (s == null) return TokenClass.Default;
            switch (s.Kind)
            {
                case SymbolKind.NamedType:
                case SymbolKind.TypeParameter:
                    return TokenClass.Type;
                case SymbolKind.Method:
                    return TokenClass.Method;
                case SymbolKind.Parameter:
                    return TokenClass.Parameter;
                case SymbolKind.Local:
                case SymbolKind.Field:
                case SymbolKind.Property:
                case SymbolKind.Event:
                case SymbolKind.RangeVariable:
                    return TokenClass.Variable;
                default:
                    return TokenClass.Default;
            }
        }

        static void AddSpan(List<SyntaxSpan> spans, SourceText text, TextSpan span, TokenClass cls)
        {
            var lines = text.Lines.GetLinePositionSpan(span);
            for (int line = lines.Start.Line; line <= lines.End.Line; line++)
            {
                var lineSpan = text.Lines[line].Span;
                int start = Math.Max(span.Start, lineSpan.Start);
                int end = Math.Min(span.End, lineSpan.End);
                if (end > start)
                    spans.Add(new SyntaxSpan(line, start - lineSpan.Start, end - start, cls));
            }
        }

        // --- Go to definition ---

        public bool TryFindDefinition(string path, string text, int offset,
            out string defPath, out int line, out int column, out string metadataOrigin)
        {
            defPath = null; line = 0; column = 0; metadataOrigin = null;
            var (model, tree) = GetModel(path, text);
            if (model == null) return false;

            var root = tree.GetRoot();
            offset = Math.Max(0, Math.Min(offset, root.FullSpan.End - 1));
            var token = root.FindToken(offset);
            var symbol = ResolveSymbol(model, token);
            if (symbol == null) return false;
            if (symbol is IMethodSymbol ctor && ctor.MethodKind == MethodKind.Constructor &&
                ctor.Locations.All(l => !l.IsInSource))
                symbol = ctor.ContainingType; // new Foo() on implicit ctor → the type

            var src = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (src != null)
            {
                var pos = src.GetLineSpan();
                defPath = src.SourceTree.FilePath;
                line = pos.StartLinePosition.Line;
                column = pos.StartLinePosition.Character;
                return true;
            }
            metadataOrigin = symbol.ContainingAssembly?.Name ?? "metadata";
            return true;
        }
    }
}
#endif
