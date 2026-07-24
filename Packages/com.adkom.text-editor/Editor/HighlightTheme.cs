using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// A named editor color theme. Each theme carries a dark and a light
    /// palette; <see cref="Current"/> follows the Unity Editor skin.
    /// </summary>
    public sealed class HighlightTheme
    {
        public sealed class Palette
        {
            public string Text;
            public string Background;
            public string Keyword;
            public string String;
            public string Comment;
            public string Number;
            public string Preprocessor;

            public Color TextColor => Parse(Text);
            public Color BackgroundColor => Parse(Background);

            static Color Parse(string html) =>
                ColorUtility.TryParseHtmlString(html, out var c) ? c : Color.magenta;
        }

        public string Name { get; }
        public Palette Dark { get; }
        public Palette Light { get; }
        public Palette Current => EditorGUIUtility.isProSkin ? Dark : Light;

        HighlightTheme(string name, Palette dark, Palette light)
        {
            Name = name;
            Dark = dark;
            Light = light;
        }

        /// <summary>VS Code defaults: Dark+ / Light+.</summary>
        public static readonly HighlightTheme VSCode = new HighlightTheme(
            "VS Code",
            new Palette
            {
                Text = "#D4D4D4", Background = "#1E1E1E", Keyword = "#569CD6",
                String = "#CE9178", Comment = "#6A9955", Number = "#B5CEA8",
                Preprocessor = "#C586C0"
            },
            new Palette
            {
                Text = "#000000", Background = "#FFFFFF", Keyword = "#0000FF",
                String = "#A31515", Comment = "#008000", Number = "#098658",
                Preprocessor = "#AF00DB"
            });

        /// <summary>JetBrains Rider defaults: Rider Dark (Darcula) / IntelliJ Light.</summary>
        public static readonly HighlightTheme Rider = new HighlightTheme(
            "Rider",
            new Palette
            {
                Text = "#A9B7C6", Background = "#2B2B2B", Keyword = "#CC7832",
                String = "#6A8759", Comment = "#808080", Number = "#6897BB",
                Preprocessor = "#BBB529"
            },
            new Palette
            {
                Text = "#000000", Background = "#FFFFFF", Keyword = "#0033B3",
                String = "#067D17", Comment = "#8C8C8C", Number = "#1750EB",
                Preprocessor = "#9E880D"
            });

        public static readonly HighlightTheme[] All = { VSCode, Rider };

        public static HighlightTheme ByName(string name)
        {
            foreach (var t in All)
                if (t.Name == name) return t;
            return VSCode;
        }
    }
}
