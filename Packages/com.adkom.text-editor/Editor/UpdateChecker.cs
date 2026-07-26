#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Automatic update checks for the ADKOM Text Editor package. Polls the
    /// GitHub latest-release tag at most once per day (frequency configurable
    /// in Settings, disable-able), announces new versions in the console, and
    /// — when the editor is idle — offers to install via UPM (git URL). When
    /// the package is embedded (development), install is skipped and only the
    /// console announcement is made.
    /// </summary>
    [InitializeOnLoad]
    public static class UpdateChecker
    {
        const string RepoApiLatest = "https://api.github.com/repos/adkom-games/ADKOM_TextEditor/releases/latest";
        const string GitInstallUrl = "https://github.com/adkom-games/ADKOM_TextEditor.git";
        const string LastCheckKey = "ADKOM.TextEditor.LastUpdateCheckTicks";
        const string LastSeenVersionKey = "ADKOM.TextEditor.LastSeenVersion";
        const double RearmIntervalSeconds = 3600; // re-evaluate hourly in long-lived editors

        static double _nextRearmTime;
        static bool _checkInFlight;

        static UpdateChecker()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextRearmTime) return;
            _nextRearmTime = EditorApplication.timeSinceStartup + RearmIntervalSeconds;
            if (CheckOnFirstRunOfVersion()) return;
            TryScheduledCheck();
        }

        /// <summary>The first time any version runs (fresh install or upgrade),
        /// check immediately — bypassing the daily schedule once — so new
        /// installs are brought current right away. Returns true if it checked.</summary>
        static bool CheckOnFirstRunOfVersion()
        {
            string current = CurrentVersion();
            if (EditorPrefs.GetString(LastSeenVersionKey, string.Empty) == current) return false;
            EditorPrefs.SetString(LastSeenVersionKey, current);
            if (!EditorConfig.AutoUpdate) return false;
            Debug.Log($"[ADKOM Text Editor] First run of version {current} — checking for updates.");
            CheckNow(manual: false);
            return true;
        }

        static void TryScheduledCheck()
        {
            if (!EditorConfig.AutoUpdate || _checkInFlight) return;
            long ticks = long.TryParse(EditorPrefs.GetString(LastCheckKey, "0"), out var t) ? t : 0;
            var last = new DateTime(ticks, DateTimeKind.Utc);
            if ((DateTime.UtcNow - last).TotalDays < EditorConfig.UpdateFrequencyDays) return;
            CheckNow(manual: false);
        }

        public static string CurrentVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UpdateChecker).Assembly);
            return info != null ? info.version : "0.0.0";
        }

        static bool IsEmbedded()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UpdateChecker).Assembly);
            return info == null || info.source == PackageSource.Embedded || info.source == PackageSource.Local;
        }

        /// <summary>Starts an async check. Manual checks ignore the schedule
        /// but still record the check time.</summary>
        public static void CheckNow(bool manual, Action<string> onResult = null)
        {
            if (_checkInFlight) { onResult?.Invoke("A check is already running."); return; }
            _checkInFlight = true;
            EditorPrefs.SetString(LastCheckKey, DateTime.UtcNow.Ticks.ToString());

            var req = UnityWebRequest.Get(RepoApiLatest);
            req.SetRequestHeader("User-Agent", "ADKOM-Text-Editor-UpdateChecker");
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                _checkInFlight = false;
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        string err = "Update check failed: " + req.error;
                        if (manual) Debug.Log("[ADKOM Text Editor] " + err);
                        onResult?.Invoke(err);
                        return;
                    }
                    string latest = ParseTagName(req.downloadHandler.text);
                    string current = CurrentVersion();
                    if (latest == null)
                    {
                        onResult?.Invoke("Could not read the latest release.");
                        return;
                    }
                    if (CompareVersions(latest, current) > 0)
                    {
                        Debug.Log($"[ADKOM Text Editor] New version available: {latest} (installed: {current}). " +
                            (IsEmbedded()
                                ? "This copy is embedded for development — update manually via git."
                                : "You will be offered the update when the editor is idle."));
                        onResult?.Invoke("New version available: " + latest);
                        if (!IsEmbedded()) PromptWhenIdle(current, latest);
                    }
                    else
                    {
                        if (manual) Debug.Log($"[ADKOM Text Editor] Up to date ({current}).");
                        onResult?.Invoke("Up to date (" + current + ").");
                    }
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        internal static string ParseTagName(string json)
        {
            const string key = "\"tag_name\":";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            int q1 = json.IndexOf('"', i + key.Length);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            string tag = json.Substring(q1 + 1, q2 - q1 - 1);
            return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
        }

        internal static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            for (int i = 0; i < 3; i++)
            {
                int va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
                int vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        static void PromptWhenIdle(string current, string latest)
        {
            void WaitForIdle()
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                    EditorApplication.isPlayingOrWillChangePlaymode)
                    return; // stay subscribed; try again next tick
                EditorApplication.update -= WaitForIdle;
                UpdatePromptWindow.Open(current, latest);
            }
            EditorApplication.update += WaitForIdle;
        }

        public static void Install(string version)
        {
            Debug.Log($"[ADKOM Text Editor] Updating to {version} via UPM…");
            Client.Add(GitInstallUrl + "#" + version);
        }
    }

    /// <summary>Idle-time dialog offering to install a new version, with a
    /// checkbox that disables automatic updates (synced with Settings).</summary>
    public class UpdatePromptWindow : EditorWindow
    {
        string _current, _latest;

        public static void Open(string current, string latest)
        {
            var w = CreateInstance<UpdatePromptWindow>();
            w._current = current;
            w._latest = latest;
            w.titleContent = new GUIContent("ADKOM Text Editor Update");
            w.minSize = w.maxSize = new Vector2(380, 150);
            w.ShowUtility();
            w.BuildUI();
        }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            root.style.paddingTop = 10;

            var title = new Label("A new version of the ADKOM Text Editor is available.");
            title.style.whiteSpace = WhiteSpace.Normal;
            root.Add(title);

            var versions = new Label($"Installed version:  {_current}\nNew version:        {_latest}");
            versions.style.marginTop = 8;
            versions.style.whiteSpace = WhiteSpace.Pre;
            root.Add(versions);

            var auto = new Toggle("Check for updates automatically") { value = EditorConfig.AutoUpdate };
            auto.RegisterValueChangedCallback(e => EditorConfig.AutoUpdate = e.newValue);
            auto.style.marginTop = 8;
            root.Add(auto);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 10;
            var later = new Button(Close) { text = "Later" };
            var install = new Button(() => { UpdateChecker.Install(_latest); Close(); }) { text = "Install Now" };
            buttons.Add(later);
            buttons.Add(install);
            root.Add(buttons);
        }
    }
}
#endif
