#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Shared look for ATE's result/log subviews — the console, Search
    /// Results, Scanner Results, and the Find/Replace results: a framed
    /// container, monospace rows, and alternating row background tones.
    /// Neutral translucent grays so both editor skins work unchanged.
    /// </summary>
    internal static class AteViewStyle
    {
        static readonly Color BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
        static readonly Color OddRow = new Color(0.5f, 0.5f, 0.5f, 0.10f);

        /// <summary>Row hover highlight (matches the selection tint used by
        /// the search-results rows).</summary>
        public static readonly Color HoverRow = new Color(0.25f, 0.42f, 0.6f, 0.4f);

        /// <summary>Frames a subview: 1px border with a slight inset margin,
        /// so the content reads as its own area instead of bleeding into the
        /// window.</summary>
        public static void Frame(VisualElement v)
        {
            v.style.borderTopWidth = 1;
            v.style.borderBottomWidth = 1;
            v.style.borderLeftWidth = 1;
            v.style.borderRightWidth = 1;
            v.style.borderTopColor = BorderColor;
            v.style.borderBottomColor = BorderColor;
            v.style.borderLeftColor = BorderColor;
            v.style.borderRightColor = BorderColor;
            v.style.marginTop = 4;
            v.style.marginBottom = 4;
            v.style.marginLeft = 4;
            v.style.marginRight = 4;
        }

        /// <summary>Applies the editor's monospace font to a container; the
        /// row labels inherit it.</summary>
        public static void Mono(VisualElement v)
        {
            var f = CodeView.MonoFont();
            if (f != null) v.style.unityFontDefinition = FontDefinition.FromFont(f);
        }

        /// <summary>The background tone for row <paramref name="index"/> —
        /// every other row gets a subtle tint.</summary>
        public static Color RowTone(int index) => (index & 1) == 1 ? OddRow : Color.clear;

        public static void Zebra(VisualElement row, int index)
            => row.style.backgroundColor = RowTone(index);
    }
}
#endif
