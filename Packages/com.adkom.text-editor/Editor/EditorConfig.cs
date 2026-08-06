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
            // Inverted (unlocked, not locked) so sessions saved before the
            // lock existed deserialize to false = locked, today's default.
            public bool mdUnlocked;
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

        /// <summary>After a post-close Save All, re-marks the matching stored
        /// session tabs clean (by path; a doc that just gained a path via
        /// Save As replaces the remaining untitled dirty entry) so they don't
        /// reopen dirty. The tab list and order are otherwise untouched.</summary>
        public static void MergeSessionSaveState(System.Collections.Generic.List<SessionTab> savedState)
        {
            var tabs = LoadSession(out int active);
            foreach (var saved in savedState)
            {
                if (saved.dirty || string.IsNullOrEmpty(saved.path)) continue; // still dirty / still untitled
                var match = tabs.Find(t => !string.IsNullOrEmpty(t.path) &&
                    string.Equals(System.IO.Path.GetFullPath(t.path), saved.path,
                        System.StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    match = tabs.Find(t => string.IsNullOrEmpty(t.path) && t.dirty); // was untitled, now saved
                if (match == null) continue;
                match.path = saved.path;
                match.dirty = false;
                match.content = null;
            }
            SaveSession(tabs, active);
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

        const string TabColorKey = "ADKOM.TextEditor.TabColor";

        /// <summary>Base color for the tab strip; each tab renders a stable
        /// per-document shade of it. Machine-wide preference.</summary>
        public static Color TabColor
        {
            get => ColorUtility.TryParseHtmlString(
                EditorPrefs.GetString(TabColorKey, "#4A6A8A"), out var c) ? c : new Color(0.29f, 0.42f, 0.54f);
            set => EditorPrefs.SetString(TabColorKey, "#" + ColorUtility.ToHtmlStringRGB(value));
        }

        const string AutoCloseKey = "ADKOM.TextEditor.AutoCloseBrackets";

        /// <summary>Auto-closing pairs while typing (machine-wide preference,
        /// default on): openers insert their closer, closers type over,
        /// selections get wrapped, Backspace removes empty pairs.</summary>
        public static bool AutoCloseBrackets
        {
            get => EditorPrefs.GetBool(AutoCloseKey, true);
            set => EditorPrefs.SetBool(AutoCloseKey, value);
        }

        const string WelcomeShownKey = "ADKOM.TextEditor.WelcomeShown";

        /// <summary>Set once the first-run welcome tabs (README +
        /// RELEASE-NOTES) have been opened in this project.</summary>
        public static bool WelcomeShown
        {
            get => EditorPrefs.GetBool(ProjectScoped(WelcomeShownKey), false);
            set => EditorPrefs.SetBool(ProjectScoped(WelcomeShownKey), value);
        }

        const string CopilotKey = "ADKOM.TextEditor.CopilotEnabled";

        /// <summary>GitHub Copilot inline completions (machine-wide, default
        /// off; requires Node.js and the user's own Copilot subscription).</summary>
        public static bool CopilotEnabled
        {
            get => EditorPrefs.GetBool(CopilotKey, false);
            set => EditorPrefs.SetBool(CopilotKey, value);
        }

        const string ConsoleHeightKey = "ADKOM.TextEditor.ConsoleHeight";

        /// <summary>Console pane height in px, set by dragging the splitter
        /// above the console (machine-wide, default 150).</summary>
        public static float ConsoleHeight
        {
            get => EditorPrefs.GetFloat(ConsoleHeightKey, 150f);
            set => EditorPrefs.SetFloat(ConsoleHeightKey, value);
        }

        const string AutoReloadKey = "ADKOM.TextEditor.AutoReloadFromDisk";

        /// <summary>Automatically reload a document when its file changes on
        /// disk and the buffer has no unsaved edits (machine-wide, default
        /// off). Dirty buffers still get the banner so edits are never lost
        /// silently.</summary>
        public static bool AutoReloadFromDisk
        {
            get => EditorPrefs.GetBool(AutoReloadKey, false);
            set => EditorPrefs.SetBool(AutoReloadKey, value);
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

        const string SpellCheckKey = "ADKOM.TextEditor.SpellCheck";

        /// <summary>Optional spell checking (comments/strings in code, whole
        /// text in markdown/plain). Machine-wide user preference. Default off.</summary>
        public static bool SpellCheckEnabled
        {
            get => EditorPrefs.GetBool(SpellCheckKey, false);
            set => EditorPrefs.SetBool(SpellCheckKey, value);
        }

        const string AutoSaveFocusKey = "ADKOM.TextEditor.AutoSaveOnFocusLoss";

        /// <summary>Save every dirty FILE-BACKED document when the ATE window
        /// loses focus (untitled buffers are skipped — no dialogs fire without
        /// the user asking). Per project. Default off.</summary>
        public static bool AutoSaveOnFocusLoss
        {
            get => EditorPrefs.GetBool(ProjectScoped(AutoSaveFocusKey), false);
            set => EditorPrefs.SetBool(ProjectScoped(AutoSaveFocusKey), value);
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

        const string GamesEnabledKey = "ADKOM.TextEditor.GamesEnabled";

        /// <summary>In-editor gaming (the Games menu: Z-Machine plus addon
        /// games). Off by default — ATE is a work tool first, and the games
        /// are a strictly opt-in extra. Machine-wide, like the addons folder
        /// the games live in. The documentation shelf under Help is NOT
        /// gated: reading about the games requires nothing.</summary>
        public static bool GamesEnabled
        {
            get => EditorPrefs.GetBool(GamesEnabledKey, false);
            set => EditorPrefs.SetBool(GamesEnabledKey, value);
        }

        const string MdLockByDefaultKey = "ADKOM.TextEditor.MdLockByDefault";

        /// <summary>Rendered Markdown opens locked (read-only, default on):
        /// clicks select text for copying instead of opening block editors.
        /// Machine-wide; the toolbar lock button still switches per tab.</summary>
        public static bool MdLockByDefault
        {
            get => EditorPrefs.GetBool(MdLockByDefaultKey, true);
            set => EditorPrefs.SetBool(MdLockByDefaultKey, value);
        }

        const string TimeLapseWindowKey = "ADKOM.TextEditor.TimeLapseWindowSize";

        /// <summary>Default sliding-window size for Git Time Lapse windows:
        /// how many revision contents stay fetched around the slider
        /// position. Machine-wide user preference; each Time Lapse window
        /// seeds from this and can override its own copy.</summary>
        public static int TimeLapseWindowSize
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(TimeLapseWindowKey, 50), 5, 500);
            set => EditorPrefs.SetInt(TimeLapseWindowKey, Mathf.Clamp(value, 5, 500));
        }

        const string TimeLapseWrapKey = "ADKOM.TextEditor.TimeLapseWrapSearches";

        /// <summary>Whether Time Lapse change navigation (▲/▼) wraps around
        /// at the first/last change instead of stopping. Machine-wide,
        /// default on.</summary>
        public static bool TimeLapseWrapSearches
        {
            get => EditorPrefs.GetBool(TimeLapseWrapKey, true);
            set => EditorPrefs.SetBool(TimeLapseWrapKey, value);
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
