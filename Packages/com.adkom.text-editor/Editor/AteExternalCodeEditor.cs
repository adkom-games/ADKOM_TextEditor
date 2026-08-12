#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Registers ADKOM Text Editor as an option in Preferences → External
    /// Tools → External Script Editor. Text files open in the ATE window at
    /// the requested line/column; everything ATE cannot handle (solutions,
    /// binaries, project sync) forwards to the configurable fallback editor —
    /// or, with no fallback set, to the OS default application.
    /// </summary>
    [InitializeOnLoad]
    public class AteExternalCodeEditor : IExternalCodeEditor
    {
        static AteExternalCodeEditor()
        {
            CodeEditor.Register(new AteExternalCodeEditor());

            // Heal a selection left stale by a Unity version change: the pref
            // stores the absolute path of the editor binary ATE registered
            // under, so after an upgrade it names the OLD install's Unity.exe.
            // Deferred because other IExternalCodeEditors may not have
            // registered yet while InitializeOnLoad ctors are still running.
            EditorApplication.delayCall += () =>
            {
                string current = CodeEditor.CurrentEditorInstallation;
                if (IsAteEditorPath(current) &&
                    !string.Equals(Norm(current), Norm(AtePath), System.StringComparison.OrdinalIgnoreCase))
                    CodeEditor.SetExternalScriptEditor(AtePath);
            };
        }

        // ATE lives inside Unity, so its "installation" is the Unity Editor
        // itself — the standard registration trick for in-editor editors.
        static string AtePath => EditorApplication.applicationPath;

        static string Norm(string p) => (p ?? string.Empty).Replace('\\', '/').TrimEnd('/');

        /// <summary>
        /// True when the path means "ATE". Any Unity Editor binary counts, not
        /// just the running one: the External Script Editor pref persists the
        /// absolute path ATE registered under, so after the project moves to a
        /// new Unity version the stored path still names the old install. If
        /// ATE only claimed the exact running binary, nothing would claim the
        /// stale path and Unity's DefaultExternalCodeEditor would "open" the
        /// script by launching it — booting a second Unity Editor that grabs
        /// the last-used project and dies on its lock file.
        /// </summary>
        internal static bool IsAteEditorPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string norm = Norm(path);
            if (string.Equals(norm, Norm(AtePath), System.StringComparison.OrdinalIgnoreCase))
                return true;
            string name = Path.GetFileName(norm);
            return name.Equals("Unity.exe", System.StringComparison.OrdinalIgnoreCase)
                || name.Equals("Unity.app", System.StringComparison.OrdinalIgnoreCase)
                || name.Equals("Unity", System.StringComparison.Ordinal);
        }

        static readonly HashSet<string> EditableExtensions = new HashSet<string>
        {
            ".cs", ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".ini",
            ".cfg", ".log", ".uss", ".tss", ".uxml", ".asmdef", ".asmref",
            ".shader", ".cginc", ".hlsl", ".compute", ".gitignore",
            ".gitattributes", ".csv", ".html", ".htm", ".bytes", ".fnt"
        };

        public CodeEditor.Installation[] Installations => new[]
        {
            new CodeEditor.Installation { Name = "ADKOM Text Editor (in-Editor)", Path = AtePath }
        };

        public void Initialize(string editorInstallationPath) { }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            if (IsAteEditorPath(editorPath))
            {
                installation = Installations[0];
                return true;
            }
            installation = default;
            return false;
        }

        public void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Text files open in the ADKOM Text Editor window. Anything else " +
                "(solutions, C# project requests, binaries) is forwarded to the " +
                "fallback editor below.", MessageType.Info);
            DrawFallbackPicker();
        }

        public static void DrawFallbackPicker()
        {
            var found = CodeEditor.Editor.GetFoundScriptEditorPaths()
                .Where(kv => !IsAteEditorPath(kv.Key))
                .ToList();
            var labels = new List<string> { "(OS default application)" };
            labels.AddRange(found.Select(kv => kv.Value));
            int current = 0;
            for (int i = 0; i < found.Count; i++)
                if (found[i].Key == EditorConfig.FallbackEditorPath) current = i + 1;
            int picked = EditorGUILayout.Popup("Fallback Editor", current, labels.ToArray());
            if (picked != current)
                EditorConfig.FallbackEditorPath = picked == 0 ? string.Empty : found[picked - 1].Key;
        }

        public static bool IsEditablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return EditableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        public bool OpenProject(string path = "", int line = -1, int column = -1)
        {
            if (IsEditablePath(path))
            {
                TextEditorWindow.OpenExternal(path, line, column);
                return true;
            }
            return ForwardOpen(path, line, column);
        }

        public void SyncAll() => FindFallback(out _)?.SyncAll();

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles,
            string[] movedFiles, string[] movedFromFiles, string[] importedFiles) =>
            FindFallback(out _)?.SyncIfNeeded(addedFiles, deletedFiles, movedFiles, movedFromFiles, importedFiles);

        static bool ForwardOpen(string path, int line, int column)
        {
            var fallback = FindFallback(out _);
            if (fallback != null)
            {
                try { return fallback.OpenProject(path, line, column); }
                catch (System.Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Fallback editor failed: " + ex.Message);
                }
            }
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                EditorUtility.OpenWithDefaultApp(path);
                return true;
            }
            return false;
        }

        /// <summary>Resolves the configured fallback editor's IExternalCodeEditor
        /// from the registry (they stay registered even when not current).</summary>
        static IExternalCodeEditor FindFallback(out CodeEditor.Installation installation)
        {
            installation = default;
            string path = EditorConfig.FallbackEditorPath;
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var editor in RegisteredEditors())
            {
                if (editor is AteExternalCodeEditor) continue;
                if (editor.TryGetInstallationForPath(path, out installation))
                {
                    editor.Initialize(path);
                    return editor;
                }
            }
            return null;
        }

        // Lives in Editor/Distribution because reading Unity's private editor
        // registry is one of the things the Asset Store build must not do.
        static IEnumerable<IExternalCodeEditor> RegisteredEditors() =>
            AteCodeEditorRegistry.Registered();
    }
}
#endif
