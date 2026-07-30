#if UNITY_EDITOR
using System.Collections.Generic;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Unity shader classifier (.shader, .hlsl, .cginc, .compute): ShaderLab
    /// block keywords and HLSL/CG keywords, scalar/vector/matrix and
    /// texture/buffer types, #pragma/#include preprocessor lines, //-and-/**/
    /// comments (tracked across lines), strings, numbers, and calls
    /// (identifier before '(') as methods.
    /// </summary>
    public sealed class ShaderClassifier : ISyntaxClassifier, ICompletionKeywords
    {
        public string Name => "Shader";

        static readonly HashSet<string> Keywords = new HashSet<string>
        {
            // ShaderLab structure
            "Shader","Properties","SubShader","Pass","Tags","LOD","Fallback",
            "CustomEditor","Category","Name","UsePass","GrabPass","Stencil",
            "Blend","BlendOp","ZWrite","ZTest","Cull","Offset","ColorMask",
            "AlphaToMask","Conservative","CGPROGRAM","ENDCG","CGINCLUDE",
            "HLSLPROGRAM","ENDHLSL","HLSLINCLUDE",
            // HLSL / CG
            "if","else","for","while","do","switch","case","default","break",
            "continue","return","discard","struct","typedef","void","true",
            "false","in","out","inout","uniform","static","const","volatile",
            "extern","inline","register","packoffset","cbuffer","tbuffer",
            "row_major","column_major","numthreads","groupshared","unroll",
            "loop","branch","flatten",
        };

        static readonly HashSet<string> ScalarTypes = new HashSet<string>
        { "float", "half", "fixed", "int", "uint", "bool", "double", "min16float" };

        static readonly HashSet<string> NamedTypes = new HashSet<string>
        {
            "sampler1D","sampler2D","sampler3D","samplerCUBE","sampler",
            "SamplerState","SamplerComparisonState",
            "Texture1D","Texture2D","Texture3D","TextureCube","Texture2DArray",
            "TextureCubeArray","Texture2DMS","RWTexture1D","RWTexture2D",
            "RWTexture3D","StructuredBuffer","RWStructuredBuffer","ByteAddressBuffer",
            "RWByteAddressBuffer","AppendStructuredBuffer","ConsumeStructuredBuffer",
            "Buffer","RWBuffer","matrix","vector","string",
            "Color","Range","Float","Int","Vector","2D","3D","Cube",  // Properties block types
        };

        public IEnumerable<string> CompletionKeywords
        {
            get
            {
                foreach (var k in Keywords) yield return k;
                foreach (var t in NamedTypes) yield return t;
                foreach (var t in ScalarTypes) yield return t;
            }
        }

        /// <summary>float / float3 / float4x4 style type names.</summary>
        static bool IsScalarVariant(string word)
        {
            foreach (var b in ScalarTypes)
            {
                if (!word.StartsWith(b, System.StringComparison.Ordinal)) continue;
                string rest = word.Substring(b.Length);
                if (rest.Length == 0) return true;
                if (rest.Length == 1 && rest[0] >= '1' && rest[0] <= '4') return true;
                if (rest.Length == 3 && rest[0] >= '1' && rest[0] <= '4' &&
                    rest[1] == 'x' && rest[2] >= '1' && rest[2] <= '4') return true;
            }
            return false;
        }

        public List<SyntaxSpan> Classify(IReadOnlyList<string> lines)
        {
            var spans = new List<SyntaxSpan>(lines.Count * 6);
            bool inBlockComment = false;

            for (int li = 0; li < lines.Count; li++)
            {
                string line = lines[li];
                int i = 0, n = line.Length;

                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", System.StringComparison.Ordinal);
                    if (end < 0) { spans.Add(new SyntaxSpan(li, 0, n, TokenClass.Comment)); continue; }
                    spans.Add(new SyntaxSpan(li, 0, end + 2, TokenClass.Comment));
                    inBlockComment = false;
                    i = end + 2;
                }

                int ns = i;
                while (ns < n && (line[ns] == ' ' || line[ns] == '\t')) ns++;
                if (ns < n && line[ns] == '#')
                {
                    // #pragma / #include / #if... — comments may trail.
                    int cmt = line.IndexOf("//", ns, System.StringComparison.Ordinal);
                    int len = (cmt >= 0 ? cmt : n) - ns;
                    spans.Add(new SyntaxSpan(li, ns, len, TokenClass.Preprocessor));
                    if (cmt >= 0) spans.Add(new SyntaxSpan(li, cmt, n - cmt, TokenClass.Comment));
                    continue;
                }

                while (i < n)
                {
                    char c = line[i];
                    if (c == '/' && i + 1 < n && line[i + 1] == '/')
                    {
                        spans.Add(new SyntaxSpan(li, i, n - i, TokenClass.Comment));
                        break;
                    }
                    if (c == '/' && i + 1 < n && line[i + 1] == '*')
                    {
                        int end = line.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                        if (end < 0)
                        {
                            spans.Add(new SyntaxSpan(li, i, n - i, TokenClass.Comment));
                            inBlockComment = true;
                            break;
                        }
                        spans.Add(new SyntaxSpan(li, i, end + 2 - i, TokenClass.Comment));
                        i = end + 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        int s = i;
                        i++;
                        while (i < n && line[i] != '"') i++;
                        if (i < n) i++;
                        spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.String));
                        continue;
                    }
                    if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(line[i + 1])))
                    {
                        int s = i;
                        i++;
                        while (i < n && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == 'x' ||
                               line[i] == 'f' || line[i] == 'h' || line[i] == 'e')) i++;
                        spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Number));
                        continue;
                    }
                    if (char.IsLetter(c) || c == '_')
                    {
                        int s = i;
                        while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                        string word = line.Substring(s, i - s);
                        if (Keywords.Contains(word))
                            spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Keyword));
                        else if (NamedTypes.Contains(word) || IsScalarVariant(word))
                            spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Type));
                        else
                        {
                            int j = i;
                            while (j < n && (line[j] == ' ' || line[j] == '\t')) j++;
                            if (j < n && line[j] == '(')
                                spans.Add(new SyntaxSpan(li, s, i - s, TokenClass.Method));
                        }
                        continue;
                    }
                    i++;
                }
            }
            return spans;
        }
    }
}
#endif
