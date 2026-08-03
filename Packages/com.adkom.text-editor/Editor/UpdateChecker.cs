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
    ///
    /// In the Asset Store build (<see cref="AteBuildFlavor.AssetStore"/>)
    /// nothing here reaches the network on its own and nothing installs:
    /// submissions may not contact the outside world without consent, nor add
    /// or update packages in a user's project. A check still runs when the
    /// user explicitly asks for one in Settings, and the result then points at
    /// the Package Manager instead of offering to install.
    /// </summary>
    [InitializeOnLoad]
    public static class UpdateChecker
    {
        // The releases Atom feed on github.com — unauthenticated and NOT subject
        // to the api.github.com rate limit (60 requests/hr/IP), which returned
        // HTTP 403 Forbidden on shared/NAT'd networks (and some corporate
        // proxies block api.github.com outright). The feed lists releases newest
        // first, so the first entry is the latest.
        const string ReleasesFeed = "https://github.com/adkom-games/ADKOM_TextEditor/releases.atom";
        const string GitInstallUrl = "https://github.com/adkom-games/ADKOM_TextEditor.git";
        const string LastCheckKey = "ADKOM.TextEditor.LastUpdateCheckTicks";
        // Per project: a machine-wide key let whichever project ran a new
        // version first suppress the release-notes tab (and the first-run
        // update check) in every other project (issue #5). The legacy global
        // key is read as a fallback so existing installs migrate cleanly.
        const string LegacyLastSeenVersionKey = "ADKOM.TextEditor.LastSeenVersion";
        static string LastSeenVersionKey => EditorConfig.ProjectScoped(LegacyLastSeenVersionKey);
        const double RearmIntervalSeconds = 3600; // re-evaluate hourly in long-lived editors

        static double _nextRearmTime;
        static bool _checkInFlight;

        // Persisted (per project): AvailableVersion was static-only state, so
        // every domain reload (compile, play mode) wiped it and the green icon
        // vanished until the NEXT scheduled check — even though the update was
        // still pending. The icon must stay until the update is performed.
        static string AvailableVersionKey => EditorConfig.ProjectScoped("ADKOM.TextEditor.AvailableVersion");

        /// <summary>The newer version a check discovered, or null when current.
        /// Windows show a green update icon by the settings gear while set.</summary>
        public static string AvailableVersion { get; private set; }
        public static event Action<string> onAvailableVersionChanged;

        static void SetAvailable(string version)
        {
            if (string.IsNullOrEmpty(version)) EditorPrefs.DeleteKey(AvailableVersionKey);
            else EditorPrefs.SetString(AvailableVersionKey, version);
            if (AvailableVersion == version) return;
            AvailableVersion = version;
            onAvailableVersionChanged?.Invoke(version);
        }

        static UpdateChecker()
        {
            EditorApplication.update += OnEditorUpdate;
            // Deferred: PackageInfo (CurrentVersion) is not reliable inside
            // InitializeOnLoad. Windows that built their toolbar before this
            // runs are updated through onAvailableVersionChanged.
            EditorApplication.delayCall += RestorePersistedAvailable;
        }

        static void RestorePersistedAvailable()
        {
            string stored = EditorPrefs.GetString(AvailableVersionKey, string.Empty);
            if (stored.Length == 0) return;
            if (CompareVersions(stored, CurrentVersion()) > 0) SetAvailable(stored);
            else EditorPrefs.DeleteKey(AvailableVersionKey); // update performed / stale
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
            string previous = EditorPrefs.GetString(LastSeenVersionKey, string.Empty);
            if (previous.Length == 0) // migrate from the pre-#5 global key
                previous = EditorPrefs.GetString(LegacyLastSeenVersionKey, string.Empty);
            if (previous == current) return false;
            EditorPrefs.SetString(LastSeenVersionKey, current);
            if (!string.IsNullOrEmpty(previous))
            {
                // An update (not a fresh install): show what's new.
                AteConsole.Info($"[ADKOM Text Editor] Updated {previous} → {current}.");
                TextEditorWindow.ShowReleaseNotes(current);
            }
            if (!AutoCheckAllowed) return false;
            AteConsole.Info($"[ADKOM Text Editor] First run of version {current} — checking for updates.");
            CheckNow(manual: false);
            return true;
        }

        /// <summary>Whether a check may run without the user asking for one.
        /// Never in the Asset Store build — an unprompted outbound request on
        /// import is exactly what the submission guidelines forbid.</summary>
        static bool AutoCheckAllowed => !AteBuildFlavor.AssetStore && EditorConfig.AutoUpdate;

        static void TryScheduledCheck()
        {
            if (!AutoCheckAllowed || _checkInFlight) return;
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

        internal static bool IsEmbeddedPackage => IsEmbedded();

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

            var req = UnityWebRequest.Get(ReleasesFeed);
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
                        if (manual) AteConsole.Info("[ADKOM Text Editor] " + err);
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
                        AteConsole.Info($"[ADKOM Text Editor] New version available: {latest} (installed: {current}). " +
                            (IsEmbedded()
                                ? "This copy is embedded for development — update manually via git."
                                : CanSelfInstall
                                    ? "You will be offered the update when the editor is idle."
                                    : "Update through Window → Package Manager."));
                        onResult?.Invoke("New version available: " + latest);
                        SetAvailable(latest);
                        if (CanSelfInstall) PromptWhenIdle(current, latest);
                    }
                    else
                    {
                        SetAvailable(null);
                        if (manual) AteConsole.Info($"[ADKOM Text Editor] Up to date ({current}).");
                        onResult?.Invoke("Up to date (" + current + ").");
                    }
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        internal static string ParseTagName(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // Atom feed (primary): the newest entry links to .../releases/tag/<TAG>.
            const string marker = "/releases/tag/";
            int a = text.IndexOf(marker, StringComparison.Ordinal);
            if (a >= 0)
            {
                int s = a + marker.Length, e = s;
                while (e < text.Length && text[e] != '"' && text[e] != '<' &&
                       text[e] != '/' && !char.IsWhiteSpace(text[e])) e++;
                if (e > s) return StripV(text.Substring(s, e - s));
            }

            // REST JSON fallback: "tag_name": "<TAG>".
            const string key = "\"tag_name\":";
            int i = text.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            int q1 = text.IndexOf('"', i + key.Length);
            if (q1 < 0) return null;
            int q2 = text.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return StripV(text.Substring(q1 + 1, q2 - q1 - 1));
        }

        static string StripV(string tag) =>
            tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;

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

        /// <summary>True from Install() until the request completes (success
        /// ends in a domain reload). Windows overlay-block their UI meanwhile:
        /// edits during the swap would be lost or corrupt the reload.</summary>
        public static bool InstallInProgress { get; private set; }
        public static event System.Action<bool> onInstallStateChanged;

        /// <summary>Whether this build can install an update itself. False for
        /// the Asset Store distribution (which must not touch the project's
        /// package set) and for embedded development copies.</summary>
        public static bool CanSelfInstall =>
            !AteBuildFlavor.AssetStore && AtePackageInstaller.Supported && !IsEmbedded();

        /// <summary>The git URL a user can paste into the Package Manager,
        /// shown wherever self-installing is unavailable.</summary>
        public static string ManualInstallUrl(string version) => GitInstallUrl + "#" + version;

        public static void Install(string version)
        {
            if (!CanSelfInstall)
            {
                // Store / embedded builds never reach here from the UI, but a
                // scripted caller might: say what to do instead of failing mute.
                AteConsole.Info("[ADKOM Text Editor] This build does not install updates itself — " +
                    "update through Window → Package Manager (" + ManualInstallUrl(version) + ").");
                return;
            }
            AteConsole.Info($"[ADKOM Text Editor] Updating to {version} via UPM…");
            InstallInProgress = true;
            onInstallStateChanged?.Invoke(true);
            // Install is async and was once fire-and-forget: a failed update
            // was completely silent and the project stayed on the old version.
            AtePackageInstaller.Install(GitInstallUrl, version, (ok, message) =>
            {
                InstallInProgress = false;
                onInstallStateChanged?.Invoke(false);
                if (ok)
                {
                    AteConsole.Info($"[ADKOM Text Editor] Updated to {message}.");
                }
                else
                {
                    // Completes async, possibly with nobody at the keyboard — a
                    // modal here would block the whole editor until dismissed.
                    AteConsole.Error("[ADKOM Text Editor] Update failed: " + message +
                        " — update manually via Package Manager → + → Add package from git URL: " +
                        ManualInstallUrl(version));
                }
            });
        }
    }

    /// <summary>Idle-time dialog offering to install a new version, with a
    /// checkbox that disables automatic updates (synced with Settings). For
    /// embedded (development) copies the dialog still opens — from the update
    /// icon too — but installing is disabled in favor of a manual-update hint.</summary>
    public class UpdatePromptWindow : EditorWindow
    {
        string _current, _latest;

        public static void Open(string current, string latest)
        {
            var w = CreateInstance<UpdatePromptWindow>();
            w._current = current;
            w._latest = latest;
            w.titleContent = new GUIContent("ADKOM Text Editor Update");
            w.minSize = w.maxSize = new Vector2(380, UpdateChecker.IsEmbeddedPackage ? 180 : 150);
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

            var title = new Label(L10n.Tr("A new version of the ADKOM Text Editor is available."));
            title.style.whiteSpace = WhiteSpace.Normal;
            root.Add(title);

            var versions = new Label(string.Format(L10n.Tr("Installed version:  {0}\nNew version:        {1}"), _current, _latest));
            versions.style.marginTop = 8;
            versions.style.whiteSpace = WhiteSpace.Pre;
            root.Add(versions);

            // No automatic checks exist in the Asset Store build, so offering
            // the toggle there would promise something that never happens.
            if (!AteBuildFlavor.AssetStore)
            {
                var auto = new Toggle(L10n.Tr("Check for updates automatically")) { value = EditorConfig.AutoUpdate };
                auto.RegisterValueChangedCallback(e => EditorConfig.AutoUpdate = e.newValue);
                auto.style.marginTop = 8;
                auto.tooltip = L10n.Tr("Check GitHub for new releases and offer to install them.");
                root.Add(auto);
            }

            if (UpdateChecker.IsEmbeddedPackage)
            {
                var embedded = new Label(L10n.Tr("This copy is embedded for development — update manually via git."));
                embedded.style.whiteSpace = WhiteSpace.Normal;
                embedded.style.marginTop = 8;
                embedded.style.opacity = 0.8f;
                root.Add(embedded);
            }
            else if (!UpdateChecker.CanSelfInstall)
            {
                var manual = new Label(L10n.Tr("Update this package from Window → Package Manager."));
                manual.style.whiteSpace = WhiteSpace.Normal;
                manual.style.marginTop = 8;
                manual.style.opacity = 0.8f;
                root.Add(manual);
            }

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 10;
            var later = new Button(Close) { text = L10n.Tr("Later"), tooltip = L10n.Tr("Keep the current version. The update icon stays by the settings gear until you update.") };
            var install = new Button(() => { UpdateChecker.Install(_latest); Close(); })
            { text = L10n.Tr("Install Now"), tooltip = L10n.Tr("Install the new version now via the Unity Package Manager.") };
            install.SetEnabled(UpdateChecker.CanSelfInstall);
            buttons.Add(later);
            buttons.Add(install);
            root.Add(buttons);
        }
    }
}
#endif
