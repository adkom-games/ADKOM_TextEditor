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
