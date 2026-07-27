#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// File dialogs and disk I/O for the text editor. Refreshes the AssetDatabase
    /// when a saved file lands inside the host project's Assets folder.
    /// </summary>
    public static class FileService
    {
        const string LastDirKey = "ADKOM.TextEditor.LastDirectory";

        /// <summary>The one path-equality used everywhere paths are compared
        /// (dedupe, recent files): full path, case-insensitive (Windows).</summary>
        public static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return a == b;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                    System.StringComparison.OrdinalIgnoreCase);
            }
            catch (System.Exception) { return false; }
        }

        static string LastDirectory
        {
            // Per project: an open/save dialog should not remember another
            // project's folder.
            get => EditorPrefs.GetString(EditorConfig.ProjectScoped(LastDirKey),
                EditorPrefs.GetString(LastDirKey, Application.dataPath));
            set => EditorPrefs.SetString(EditorConfig.ProjectScoped(LastDirKey), value);
        }

        /// <summary>Shows an Open dialog. Returns the chosen path or null if cancelled.</summary>
        public static string PromptOpen()
        {
            string path = EditorUtility.OpenFilePanel("Open File", LastDirectory, "");
            if (string.IsNullOrEmpty(path)) return null;
            LastDirectory = Path.GetDirectoryName(path);
            return path;
        }

        /// <summary>Shows a Save As dialog. Returns the chosen path or null if cancelled.</summary>
        public static string PromptSaveAs(string defaultName)
        {
            string path = EditorUtility.SaveFilePanel("Save File As", LastDirectory,
                string.IsNullOrEmpty(defaultName) ? "Untitled.txt" : defaultName, "");
            if (string.IsNullOrEmpty(path)) return null;
            LastDirectory = Path.GetDirectoryName(path);
            return path;
        }

        /// <summary>Saves the document to its current path (or prompts if it has none).
        /// Returns true if the file was written.</summary>
        /// <summary>On-save cleanups (per-project settings): trim trailing
        /// whitespace and/or ensure a single final newline. Mutates
        /// doc.Content — the window refreshes the view after saving.</summary>
        static void ApplySaveTransforms(TextDocument doc)
        {
            string c = doc.Content ?? string.Empty;
            if (EditorConfig.TrimTrailingOnSave)
            {
                var lines = c.Split('\n');
                for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd(' ', '\t');
                c = string.Join("\n", lines);
            }
            if (EditorConfig.FinalNewlineOnSave)
            {
                c = c.TrimEnd('\n') + "\n";
                if (c == "\n") c = string.Empty; // don't turn an empty doc into one newline
            }
            doc.Content = c;
        }

        public static bool Save(TextDocument doc)
        {
            string path = doc.FilePath;
            if (string.IsNullOrEmpty(path))
            {
                path = PromptSaveAs(doc.DisplayName);
                if (path == null) return false;
            }
            ApplySaveTransforms(doc);
            doc.SaveTo(path);
            RefreshIfInAssets(path);
            Scripting.AteApi.NotifySaved(doc);
            return true;
        }

        /// <summary>Always prompts for a path. Returns true if the file was written.</summary>
        public static bool SaveAs(TextDocument doc)
        {
            string path = PromptSaveAs(doc.DisplayName);
            if (path == null) return false;
            ApplySaveTransforms(doc);
            doc.SaveTo(path);
            RefreshIfInAssets(path);
            Scripting.AteApi.NotifySaved(doc);
            return true;
        }

        static void RefreshIfInAssets(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (full.StartsWith(assets, System.StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh();
        }
    }
}
#endif
