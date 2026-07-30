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
    public sealed class RoslynSemanticProvider : ADKOM.TextEditor.ISemanticProvider,
        ADKOM.TextEditor.ISemanticRefactorings, ADKOM.TextEditor.ISemanticCompletion
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
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Could not capture the assembly list for semantics: " + ex.Message);
                }
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
            int unreadable = 0;
            foreach (var src in asm.SourceList)
            {
                try
                {
                    string full = Norm(src);
                    trees[full] = CSharpSyntaxTree.ParseText(File.ReadAllText(full), parse, full);
                }
                catch (Exception) { unreadable++; }
            }
            var refs = new List<MetadataReference>();
            int badRefs = 0;
            foreach (var r in asm.References.Distinct())
            {
                try { if (File.Exists(r)) refs.Add(MetadataReference.CreateFromFile(r)); }
                catch (Exception) { badRefs++; }
            }
            if (unreadable > 0 || badRefs > 0) // one aggregate breadcrumb, not per-file spam
                AteConsole.Warn($"[ADKOM Text Editor] Semantics compilation for {asm.Name}: " +
                    $"{unreadable} unreadable source file(s), {badRefs} unloadable reference(s) skipped.");
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

        // --- References & rename (workspace-free: manual symbol matching
        // over the assembly's compilation) ---

        ISymbol SymbolAt(SemanticModel model, SyntaxTree tree, int offset)
        {
            var root = tree.GetRoot();
            offset = Math.Max(0, Math.Min(offset, root.FullSpan.End - 1));
            return ResolveSymbol(model, root.FindToken(offset));
        }

        void CollectMatches(Compilation comp, SyntaxTree tree, ISymbol target,
            Action<Microsoft.CodeAnalysis.Text.TextSpan, SyntaxTree> onMatch)
        {
            var model = comp.GetSemanticModel(tree);
            foreach (var token in tree.GetRoot().DescendantTokens())
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken) || token.ValueText != target.Name)
                    continue;
                var node = token.Parent;
                if (node == null) continue;
                var sym = model.GetSymbolInfo(node).Symbol ?? model.GetDeclaredSymbol(node);
                if (sym == null) continue;
                if (SymbolEqualityComparer.Default.Equals(sym.OriginalDefinition, target.OriginalDefinition))
                    onMatch(token.Span, tree);
            }
        }

        public bool TryFindReferences(string path, string text, int offset,
            out System.Collections.Generic.List<ADKOM.TextEditor.SymbolReference> refs)
        {
            refs = null;
            var (model, tree) = GetModel(path, text);
            if (model == null) return false;
            var target = SymbolAt(model, tree, offset);
            if (target == null) return false;
            var result = new System.Collections.Generic.List<ADKOM.TextEditor.SymbolReference>();
            var comp = model.Compilation;
            foreach (var t in comp.SyntaxTrees)
            {
                var srcText = t.GetText();
                CollectMatches(comp, t, target, (span, tr) =>
                {
                    var pos = tr.GetLineSpan(span);
                    int line = pos.StartLinePosition.Line;
                    string lineText = srcText.Lines.Count > line
                        ? srcText.Lines[line].ToString().Trim() : string.Empty;
                    result.Add(new ADKOM.TextEditor.SymbolReference
                    {
                        Path = tr.FilePath,
                        Line = line,
                        Column = pos.StartLinePosition.Character,
                        LineText = lineText
                    });
                });
            }
            refs = result;
            return result.Count > 0;
        }

        public bool TryGetRenameSpans(string path, string text, int offset,
            out System.Collections.Generic.List<(int start, int length)> spans, out string symbolName)
        {
            spans = null;
            symbolName = null;
            var (model, tree) = GetModel(path, text);
            if (model == null) return false;
            var target = SymbolAt(model, tree, offset);
            if (target == null || target.Locations.All(l => !l.IsInSource))
                return false; // metadata symbols cannot be renamed
            var result = new System.Collections.Generic.List<(int, int)>();
            CollectMatches(model.Compilation, tree, target,
                (span, _) => result.Add((span.Start, span.Length)));
            if (result.Count == 0) return false;
            result.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            spans = result;
            symbolName = target.Name;
            return true;
        }

        // --- IntelliSense completions ---

        /// <summary>Candidates at <paramref name="offset"/>: after a '.' the
        /// accessible members of the left-hand expression (instance members for
        /// a value, statics + nested types for a type name, types + child
        /// namespaces for a namespace); otherwise every symbol in scope
        /// (LookupSymbols). One query serves the whole word — the editor
        /// filters by the typed prefix locally.</summary>
        public bool TryGetCompletions(string path, string text, int offset,
            out List<ADKOM.TextEditor.CompletionItem> items)
        {
            items = null;
            var (model, tree) = GetModel(path, text);
            if (model == null) return false;
            var root = tree.GetRoot();
            offset = Math.Max(0, Math.Min(offset, root.FullSpan.End));

            IEnumerable<ISymbol> found;
            var lhs = MemberAccessLhs(root, offset);
            if (lhs != null)
            {
                var sym = model.GetSymbolInfo(lhs).Symbol
                    ?? model.GetSymbolInfo(lhs).CandidateSymbols.FirstOrDefault();
                if (sym is INamespaceSymbol ns)
                    found = ns.GetMembers().Where(m =>
                        m is INamespaceSymbol ||
                        (m is INamedTypeSymbol nt && model.IsAccessible(offset, nt)));
                else if (sym is ITypeSymbol typeRef)      // "Debug." — static context
                    found = StaticMembers(model, offset, typeRef);
                else
                {
                    var type = model.GetTypeInfo(lhs).Type; // "myVar." — instance context
                    if (type == null) return false;
                    found = InstanceMembers(model, offset, type);
                }
            }
            else
            {
                found = model.LookupSymbols(offset);      // everything in scope
            }

            // Dedupe by name (overload groups collapse to one row with a count).
            var byName = new Dictionary<string, (ISymbol first, int n)>(StringComparer.Ordinal);
            foreach (var s in found)
            {
                if (s.IsImplicitlyDeclared || !s.CanBeReferencedByName) continue;
                if (s.Name.Length == 0 || s.Name[0] == '<') continue;
                if (byName.TryGetValue(s.Name, out var g)) byName[s.Name] = (g.first, g.n + 1);
                else byName[s.Name] = (s, 1);
                if (byName.Count > 5000) break;
            }
            if (byName.Count == 0) return false;

            var result = new List<ADKOM.TextEditor.CompletionItem>(byName.Count);
            var fmt = SymbolDisplayFormat.MinimallyQualifiedFormat;
            foreach (var kv in byName)
            {
                string detail;
                try
                {
                    var s = kv.Value.first;
                    detail = s is INamespaceSymbol ? "namespace"
                        : s is INamedTypeSymbol nt ? nt.TypeKind.ToString().ToLowerInvariant()
                        : s.ToDisplayString(fmt);
                    if (detail.Length > 64) detail = detail.Substring(0, 63) + "…";
                    if (kv.Value.n > 1) detail += " (+" + (kv.Value.n - 1) + ")";
                }
                catch (Exception) { detail = null; }
                result.Add(new ADKOM.TextEditor.CompletionItem
                {
                    Insert = kv.Key,
                    Display = kv.Key,
                    Detail = detail,
                    Kind = Map(kv.Value.first)
                });
            }
            result.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
            items = result;
            return true;
        }

        /// <summary>The expression left of the '.' when <paramref name="offset"/>
        /// sits right after a dot or inside the member name being typed.</summary>
        static Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax MemberAccessLhs(SyntaxNode root, int offset)
        {
            var token = root.FindToken(Math.Max(0, offset - 1));
            if (token.IsKind(SyntaxKind.DotToken))
            {
                if (token.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax ma) return ma.Expression;
                if (token.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax qn) return qn.Left;
                return null;
            }
            if (token.IsKind(SyntaxKind.IdentifierToken) &&
                token.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.SimpleNameSyntax sn)
            {
                if (sn.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax ma && ma.Name == sn) return ma.Expression;
                if (sn.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax qn && qn.Right == sn) return qn.Left;
            }
            return null;
        }

        static IEnumerable<ISymbol> StaticMembers(SemanticModel model, int offset, ITypeSymbol type)
        {
            for (var t = type; t != null; t = t.BaseType)
                foreach (var m in t.GetMembers())
                    if ((m.IsStatic || m is INamedTypeSymbol || t.TypeKind == TypeKind.Enum) &&
                        model.IsAccessible(offset, m))
                        yield return m;
        }

        static IEnumerable<ISymbol> InstanceMembers(SemanticModel model, int offset, ITypeSymbol type)
        {
            for (var t = type; t != null; t = t.BaseType)
                foreach (var m in t.GetMembers())
                    if (!m.IsStatic && !(m is ITypeSymbol) && model.IsAccessible(offset, m))
                        yield return m;
        }

        // --- "From metadata" stub view ---

        public bool TryGetMetadataSource(string path, string text, int offset,
            out string title, out string source, out int line)
        {
            title = null; source = null; line = 0;
            var (model, tree) = GetModel(path, text);
            if (model == null) return false;
            var root = tree.GetRoot();
            offset = Math.Max(0, Math.Min(offset, root.FullSpan.End - 1));
            var symbol = ResolveSymbol(model, root.FindToken(offset));
            if (symbol == null || symbol.Locations.Any(l => l.IsInSource)) return false;

            var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            if (type == null) return false;

            title = type.Name + " [metadata]";
            var fmt = SymbolDisplayFormat.MinimallyQualifiedFormat;
            var sb = new System.Text.StringBuilder(4096);
            int currentLine = 0;
            void Line(string s) { sb.Append(s).Append('\n'); currentLine++; }

            Line("// From metadata: " + (type.ContainingAssembly?.Name ?? "?") + ".dll — signatures only.");
            Line("");
            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                Line("namespace " + type.ContainingNamespace.ToDisplayString());
                Line("{");
            }
            string indent = type.ContainingNamespace.IsGlobalNamespace ? "" : "    ";
            string kind = type.TypeKind switch
            {
                TypeKind.Interface => "interface",
                TypeKind.Struct => "struct",
                TypeKind.Enum => "enum",
                TypeKind.Delegate => "delegate",
                _ => "class"
            };
            string bases = "";
            if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object &&
                type.TypeKind == TypeKind.Class)
                bases = " : " + type.BaseType.ToDisplayString(fmt);
            if (SymbolEqualityComparer.Default.Equals(type, symbol)) line = currentLine;
            Line(indent + "public " + kind + " " + type.ToDisplayString(fmt) + bases);
            Line(indent + "{");

            var members = type.GetMembers()
                .Where(m => !m.IsImplicitlyDeclared &&
                    (m.DeclaredAccessibility == Accessibility.Public ||
                     m.DeclaredAccessibility == Accessibility.Protected))
                .Where(m => !(m is IMethodSymbol ms &&
                    (ms.MethodKind == MethodKind.PropertyGet || ms.MethodKind == MethodKind.PropertySet ||
                     ms.MethodKind == MethodKind.EventAdd || ms.MethodKind == MethodKind.EventRemove)))
                .OrderBy(m => m.Kind).ThenBy(m => m.Name, StringComparer.Ordinal);

            foreach (var m in members)
            {
                if (SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, symbol.OriginalDefinition))
                    line = currentLine;
                string sig;
                try { sig = m.ToDisplayString(fmt); }
                catch (Exception) { sig = m.Name; }
                Line(indent + "    public " + sig + ";");
            }

            Line(indent + "}");
            if (!type.ContainingNamespace.IsGlobalNamespace) Line("}");
            source = sb.ToString();
            return true;
        }
    }
}
#endif
