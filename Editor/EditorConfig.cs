#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    public enum KeymapLayout { VisualStudio, Rider, VSCode }

    /// <summary>Editor-wide settings persisted in EditorPrefs.</summary>
    public static class EditorConfig
    {
        const string TabSizeKey = "ADKOM.TextEditor.TabSize";
        const string KeymapKey = "ADKOM.TextEditor.Keymap";

        public static int TabSize
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(TabSizeKey, 4), 1, 16);
            set => EditorPrefs.SetInt(TabSizeKey, Mathf.Clamp(value, 1, 16));
        }

        public static KeymapLayout Keymap
        {
            get => (KeymapLayout)EditorPrefs.GetInt(KeymapKey, (int)KeymapLayout.VisualStudio);
            set => EditorPrefs.SetInt(KeymapKey, (int)value);
        }

        const string FontNameKey = "ADKOM.TextEditor.FontName";
        const string FontSizeKey = "ADKOM.TextEditor.FontSize";
        public const int DefaultFontSize = 13;

        /// <summary>OS font name for the code view; empty = bundled monospace
        /// default (RobotoMono).</summary>
        public static string FontName
        {
            get => EditorPrefs.GetString(FontNameKey, string.Empty);
            set => EditorPrefs.SetString(FontNameKey, value ?? string.Empty);
        }

        public static int FontSize
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(FontSizeKey, DefaultFontSize), 8, 40);
            set => EditorPrefs.SetInt(FontSizeKey, Mathf.Clamp(value, 8, 40));
        }

        const string FallbackEditorKey = "ADKOM.TextEditor.FallbackEditorPath";

        /// <summary>External editor to forward unhandled open/sync requests to
        /// when ATE is Unity's selected external script editor. Empty = none
        /// (fall back to the OS default application).</summary>
        public static string FallbackEditorPath
        {
            get => EditorPrefs.GetString(FallbackEditorKey, string.Empty);
            set => EditorPrefs.SetString(FallbackEditorKey, value ?? string.Empty);
        }

        const string SmoothScrollKey = "ADKOM.TextEditor.SmoothScrolling";

        /// <summary>Animate wheel scrolling instead of stepping (default on).</summary>
        public static bool SmoothScrolling
        {
            get => EditorPrefs.GetBool(SmoothScrollKey, true);
            set => EditorPrefs.SetBool(SmoothScrollKey, value);
        }

        const string AutoUpdateKey = "ADKOM.TextEditor.AutoUpdate";
        const string UpdateFreqKey = "ADKOM.TextEditor.UpdateFrequencyDays";

        /// <summary>Automatic update checks (default on).</summary>
        public static bool AutoUpdate
        {
            get => EditorPrefs.GetBool(AutoUpdateKey, true);
            set => EditorPrefs.SetBool(AutoUpdateKey, value);
        }

        /// <summary>Days between automatic update checks. Minimum 1: checks
        /// can never run more than once per day.</summary>
        public static int UpdateFrequencyDays
        {
            get => Mathf.Max(1, EditorPrefs.GetInt(UpdateFreqKey, 1));
            set => EditorPrefs.SetInt(UpdateFreqKey, Mathf.Max(1, value));
        }
    }
}
#endif
