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
    }
}
