#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    public enum KeymapLayout { VisualStudio, Rider, VSCode }

    /// <summary>Editor-wide settings persisted in EditorPrefs.</summary>
    public static class EditorConfig
    {
        // SCOPING POLICY: settings that describe the USER (keymap, fonts,
        // theme, scrolling, defaults) are machine-wide; settings that describe
        // the PROJECT (tab size convention, semantics consent/install,
        // update behavior, recent files, session, last-seen version, last
        // dialog directory) are project-scoped via ProjectScoped(). Reads
        // fall back to the legacy machine-wide value for migration.
        internal static string ProjectScoped(string key) =>
            key + "." + Application.dataPath.GetHashCode().ToString("X8");

        const string TabSizeKey = "ADKOM.TextEditor.TabSize";
        const string KeymapKey = "ADKOM.TextEditor.Keymap";

        /// <summary>Per project: an indentation convention, not a user taste.</summary>
        public static int TabSize
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(ProjectScoped(TabSizeKey),
                EditorPrefs.GetInt(TabSizeKey, 4)), 1, 16);
            set => EditorPrefs.SetInt(ProjectScoped(TabSizeKey), Mathf.Clamp(value, 1, 16));
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

        const string SemanticsKey = "ADKOM.TextEditor.SemanticsEnabled";

        /// <summary>Semantic features (compiler-accurate colors, Go to
        /// Definition). OFF by default: enabling installs the semantics module
        /// and, if needed, the bundled Roslyn assemblies into the project.</summary>
        /// <summary>Per project: enabling consents to installing Roslyn into
        /// THIS project — one project's consent must not leak to others.</summary>
        public static bool SemanticsEnabled
        {
            get => EditorPrefs.GetBool(ProjectScoped(SemanticsKey),
                EditorPrefs.GetBool(SemanticsKey, false));
            set => EditorPrefs.SetBool(ProjectScoped(SemanticsKey), value);
        }

        const string SmoothScrollKey = "ADKOM.TextEditor.SmoothScrolling";

        /// <summary>Animate wheel scrolling instead of stepping (default on).</summary>
        public static bool SmoothScrolling
        {
            get => EditorPrefs.GetBool(SmoothScrollKey, true);
            set => EditorPrefs.SetBool(SmoothScrollKey, value);
        }

        const string RecentMaxKey = "ADKOM.TextEditor.RecentFilesMax";
        // The list is per-project (recent files from another project would be
        // noise); the count setting is machine-wide like every other setting.
        static string RecentListKey => ProjectScoped("ADKOM.TextEditor.RecentFiles");

        /// <summary>How many entries the File → Recent Files menu keeps.</summary>
        public static int RecentFilesMax
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(RecentMaxKey, 5), 1, 30);
            set
            {
                EditorPrefs.SetInt(RecentMaxKey, Mathf.Clamp(value, 1, 30));
                TrimRecentFiles();
            }
        }

        // Parsed once and invalidated on write (defect #10: the File menu
        // re-read and re-split EditorPrefs on every open).
        static System.Collections.Generic.List<string> _recentCache;

        /// <summary>Most-recent-first absolute paths, at most RecentFilesMax.</summary>
        public static System.Collections.Generic.List<string> RecentFiles
        {
            get
            {
                if (_recentCache == null)
                {
                    _recentCache = new System.Collections.Generic.List<string>();
                    string raw = EditorPrefs.GetString(RecentListKey, string.Empty);
                    foreach (var p in raw.Split('\n'))
                        if (!string.IsNullOrEmpty(p)) _recentCache.Add(p);
                }
                return new System.Collections.Generic.List<string>(_recentCache);
            }
        }

        static void WriteRecent(System.Collections.Generic.List<string> list)
        {
            _recentCache = list;
            EditorPrefs.SetString(RecentListKey, string.Join("\n", list));
        }

        public static void AddRecentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var list = RecentFiles;
            list.RemoveAll(p => FileService.PathsEqual(p, path));
            list.Insert(0, path);
            if (list.Count > RecentFilesMax) list.RemoveRange(RecentFilesMax, list.Count - RecentFilesMax);
            WriteRecent(list);
        }

        public static void RemoveRecentFile(string path)
        {
            var list = RecentFiles;
            list.RemoveAll(p => FileService.PathsEqual(p, path));
            WriteRecent(list);
        }

        public static void ClearRecentFiles() => WriteRecent(new System.Collections.Generic.List<string>());

        static void TrimRecentFiles()
        {
            var list = RecentFiles;
            int max = RecentFilesMax;
            if (list.Count > max)
            {
                list.RemoveRange(max, list.Count - max);
                WriteRecent(list);
            }
        }

        // Open-tab session, saved when the ATE window closes and restored when
        // it reopens. Lives in Library/ (per project, not versioned) as JSON
        // so dirty tabs can carry their unsaved CONTENT — closing the window
        // never needs an unsaved-changes dialog; the buffers just come back.
        [System.Serializable]
        public class SessionTab
        {
            public string path;      // empty for untitled documents
            public bool dirty;
            public string content;   // only stored when dirty
            public bool mdRendered;
        }

        [System.Serializable]
        class SessionData
        {
            public int active;
            public System.Collections.Generic.List<SessionTab> tabs;
        }

        static string SessionFilePath => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.dataPath),
            "Library", "ADKOMTextEditor", "session.json");

        static string LegacySessionKey => ProjectScoped("ADKOM.TextEditor.Session");

        public static void SaveSession(System.Collections.Generic.List<SessionTab> tabs, int activeIndex)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SessionFilePath));
                System.IO.File.WriteAllText(SessionFilePath,
                    JsonUtility.ToJson(new SessionData { active = activeIndex, tabs = tabs }));
            }
            catch (System.Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Could not save the tab session: " + ex.Message);
            }
        }

        public static System.Collections.Generic.List<SessionTab> LoadSession(out int activeIndex)
        {
            activeIndex = 0;
            try
            {
                if (System.IO.File.Exists(SessionFilePath))
                {
                    var d = JsonUtility.FromJson<SessionData>(System.IO.File.ReadAllText(SessionFilePath));
                    if (d != null && d.tabs != null) { activeIndex = d.active; return d.tabs; }
                }
            }
            catch (System.Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Tab session file unreadable, starting empty: " + ex.Message);
            }

            // One-time migration from the old EditorPrefs path-list format.
            var tabs = new System.Collections.Generic.List<SessionTab>();
            string raw = EditorPrefs.GetString(LegacySessionKey, string.Empty);
            if (raw.Length > 0)
            {
                var lines = raw.Split('\n');
                int.TryParse(lines[0], out activeIndex);
                for (int i = 1; i < lines.Length; i++)
                    if (!string.IsNullOrEmpty(lines[i]))
                        tabs.Add(new SessionTab { path = lines[i] });
                EditorPrefs.DeleteKey(LegacySessionKey);
            }
            return tabs;
        }

        const string TrimSaveKey = "ADKOM.TextEditor.TrimTrailingOnSave";
        const string FinalNewlineKey = "ADKOM.TextEditor.FinalNewlineOnSave";

        /// <summary>Strip trailing spaces/tabs from every line when saving.
        /// Per project (a formatting convention). Default off.</summary>
        public static bool TrimTrailingOnSave
        {
            get => EditorPrefs.GetBool(ProjectScoped(TrimSaveKey), false);
            set => EditorPrefs.SetBool(ProjectScoped(TrimSaveKey), value);
        }

        /// <summary>Guarantee exactly one trailing newline when saving.
        /// Per project. Default off.</summary>
        public static bool FinalNewlineOnSave
        {
            get => EditorPrefs.GetBool(ProjectScoped(FinalNewlineKey), false);
            set => EditorPrefs.SetBool(ProjectScoped(FinalNewlineKey), value);
        }

        const string MdOpenRenderedKey = "ADKOM.TextEditor.MdOpenRendered";

        /// <summary>Default view for .md files when opened: rendered (WYSIWYG)
        /// when true, source when false (default). The release-notes tab shown
        /// after an update always opens rendered regardless.</summary>
        public static bool MdOpenRendered
        {
            get => EditorPrefs.GetBool(MdOpenRenderedKey, false);
            set => EditorPrefs.SetBool(MdOpenRenderedKey, value);
        }

        const string AutoUpdateKey = "ADKOM.TextEditor.AutoUpdate";
        const string UpdateFreqKey = "ADKOM.TextEditor.UpdateFrequencyDays";

        /// <summary>Automatic update checks (default on). Per project: the
        /// package install and its version live in the project.</summary>
        public static bool AutoUpdate
        {
            get => EditorPrefs.GetBool(ProjectScoped(AutoUpdateKey),
                EditorPrefs.GetBool(AutoUpdateKey, true));
            set
            {
                // Forensic breadcrumb: a field report showed this OFF after an
                // update-install; the default is ON, so log who writes false.
                if (value != AutoUpdate && !value)
                    AteConsole.Info("[ADKOM Text Editor] Automatic updates disabled.\n" +
                        new System.Diagnostics.StackTrace(1, false));
                EditorPrefs.SetBool(ProjectScoped(AutoUpdateKey), value);
            }
        }

        /// <summary>Days between automatic update checks (per project).
        /// Minimum 1: checks can never run more than once per day.</summary>
        public static int UpdateFrequencyDays
        {
            get => Mathf.Max(1, EditorPrefs.GetInt(ProjectScoped(UpdateFreqKey),
                EditorPrefs.GetInt(UpdateFreqKey, 1)));
            set => EditorPrefs.SetInt(ProjectScoped(UpdateFreqKey), Mathf.Max(1, value));
        }
    }
}
#endif
