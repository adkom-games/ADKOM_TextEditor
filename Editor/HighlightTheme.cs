using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// A named editor color theme. Each theme carries a dark and a light
    /// palette; <see cref="Current"/> follows the Unity Editor skin.
    /// </summary>
    public enum ThemeMode { Auto, Dark, Light }

    public sealed class HighlightTheme
    {
        /// <summary>Global palette mode: Auto follows the Unity Editor skin.</summary>
        public static ThemeMode Mode = ThemeMode.Auto;

        public sealed class Palette
        {
            public string Text;
            public string Background;
            public string Keyword;
            public string String;
            public string Comment;
            public string Number;
            public string Preprocessor;
            public string Selection;

            public Color TextColor => Parse(Text);
            public Color BackgroundColor => Parse(Background);
            public Color SelectionColor => Parse(Selection);

            static Color Parse(string html) =>
                ColorUtility.TryParseHtmlString(html, out var c) ? c : Color.magenta;
        }

        public string Name { get; }
        public Palette Dark { get; }
        public Palette Light { get; }
        public Palette Current => Mode switch
        {
            ThemeMode.Dark => Dark,
            ThemeMode.Light => Light,
            _ => EditorGUIUtility.isProSkin ? Dark : Light
        };

        HighlightTheme(string name, Palette dark, Palette light)
        {
            Name = name;
            Dark = dark;
            Light = light;
        }

        /// <summary>Visual Studio defaults (Dark / Light editor themes).</summary>
        public static readonly HighlightTheme VisualStudio = new HighlightTheme(
            "Visual Studio",
            new Palette
            {
                Text = "#DCDCDC", Background = "#1E1E1E", Keyword = "#569CD6",
                String = "#D69D85", Comment = "#57A64A", Number = "#B5CEA8",
                Preprocessor = "#9B9B9B", Selection = "#264F78"
            },
            new Palette
            {
                Text = "#000000", Background = "#FFFFFF", Keyword = "#0000FF",
                String = "#A31515", Comment = "#008000", Number = "#000000",
                Preprocessor = "#808080", Selection = "#ADD6FF"
            });

        /// <summary>VS Code defaults: Dark+ / Light+.</summary>
        public static readonly HighlightTheme VSCode = new HighlightTheme(
            "VS Code",
            new Palette
            {
                Text = "#D4D4D4", Background = "#1E1E1E", Keyword = "#569CD6",
                String = "#CE9178", Comment = "#6A9955", Number = "#B5CEA8",
                Preprocessor = "#C586C0", Selection = "#264F78"
            },
            new Palette
            {
                Text = "#000000", Background = "#FFFFFF", Keyword = "#0000FF",
                String = "#A31515", Comment = "#008000", Number = "#098658",
                Preprocessor = "#AF00DB", Selection = "#ADD6FF"
            });

        /// <summary>JetBrains Rider defaults: Rider Dark (Darcula) / IntelliJ Light.</summary>
        public static readonly HighlightTheme Rider = new HighlightTheme(
            "Rider",
            new Palette
            {
                Text = "#A9B7C6", Background = "#2B2B2B", Keyword = "#CC7832",
                String = "#6A8759", Comment = "#808080", Number = "#6897BB",
                Preprocessor = "#BBB529", Selection = "#214283"
            },
            new Palette
            {
                Text = "#000000", Background = "#FFFFFF", Keyword = "#0033B3",
                String = "#067D17", Comment = "#8C8C8C", Number = "#1750EB",
                Preprocessor = "#9E880D", Selection = "#A6D2FF"
            });

        public static readonly HighlightTheme[] All = { VisualStudio, VSCode, Rider };

        public static HighlightTheme ByName(string name)
        {
            foreach (var t in All)
                if (t.Name == name) return t;
            return VSCode;
        }
    }
}
