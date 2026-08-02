#if UNITY_EDITOR
// ATE Z-Machine — built-in launcher (Games → Z-Machine (Zork)…).
//
// The interpreter is part of the editor, not an addon: story-file downloads
// are a native ATE feature (like auto-update), trusted by installation. Key
// input goes through a core window hook rather than the addon input events
// (which an addon reload resets), so a game keeps working across reloads.
//
// MULTI-INSTANCE: any number of games can run at once. Each running game is
// one Instance (screen + machine + optional map/map view). The single core
// key hook routes to whichever game owns the ACTIVE document; tab titles are
// disambiguated ("Zork I", "Zork I (1)", …); and activating a game's tab
// brings THAT game's own map view into the Map console tab.
using System;
using System.Collections.Generic;
using System.IO;
using ADKOM.TextEditor;
using ADKOM.TextEditor.Scripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AteZMachine
{
    internal static class ZMachineGame
    {
        /// <summary>One running game: its screen, machine, and map. The map
        /// view is per game, so every game keeps its own zoom and layout.</summary>
        sealed class Instance
        {
            public string Title;
            public string StoryPath;
            public ZScreen Screen;
            public ZMachine Machine;
            public ZMap Map;
            public ZMapView MapView;
        }

        /// <summary>Where domain-reload snapshots live (one .azx + .log +
        /// .map trio per running game, keyed by the id stamped on its doc).</summary>
        static string SessionFolder => Path.Combine(ZStory.StoryFolder, "Session");

        static readonly List<Instance> _games = new List<Instance>();
        static TextEditorWindow _window;
        static bool _hooked;

        const string AutoMapPref = "ADKOM.ZMachine.AutoMap";
        static bool AutoMapOn { get => EditorPrefs.GetBool(AutoMapPref, true); set => EditorPrefs.SetBool(AutoMapPref, value); }

        /// <summary>Adds the Z-Machine items as a submenu of the given menu
        /// (Games → Z-Machine (Zork) → Zork I / … / Open Story File…). A real
        /// submenu, not a popped-up context menu — showing a second menu from
        /// a menu-item callback is silently dropped by Unity. Every pick
        /// starts a NEW game; already-running ones keep playing.</summary>
        internal static void AddMenuItems(GenericMenu m, string prefix, TextEditorWindow window)
        {
            foreach (var g in ZStory.Downloadable)
            {
                var game = g;
                bool have = File.Exists(Path.Combine(ZStory.StoryFolder, g.File));
                string label = prefix + (have ? game.Title : game.Title + " " + L10n.Tr("(download)"));
                m.AddItem(new GUIContent(label), false, () =>
                {
                    _window = window;
                    string p = ZStory.EnsureDownloaded(game, out string err);
                    if (p == null) { AteConsole.Warn("[ADKOM Text Editor] " + game.Title + " " + L10n.Tr("download failed: ") + err); return; }
                    Launch(p);
                });
            }
            m.AddItem(new GUIContent(prefix + L10n.Tr("Open Story File… (.z3)")), false, () =>
            {
                _window = window;
                string p = EditorUtility.OpenFilePanel(L10n.Tr("Open Z-machine story file"), ZStory.StoryFolder, "z3,dat");
                if (!string.IsNullOrEmpty(p)) Launch(p);
            });
            m.AddSeparator(prefix);
            m.AddItem(new GUIContent(prefix + L10n.Tr("Auto-map (build a map as you explore)")), AutoMapOn, () =>
            {
                AutoMapOn = !AutoMapOn;
                // Apply to every running game: attach missing maps, or drop
                // them all. New launches follow the preference.
                Prune();
                foreach (var inst in _games)
                {
                    if (AutoMapOn) AttachMap(inst);
                    else DetachMap(inst);
                }
            });
        }

        /// <summary>Starts a NEW game instance — several can run at once, so
        /// picking "Zork I" while one is playing opens "Zork I (1)".</summary>
        static void Launch(string path)
        {
            byte[] story = ZStory.Load(path, out string err);
            if (story == null) { AteConsole.Warn("[ADKOM Text Editor] could not read " + path + ": " + err); return; }

            Prune();
            var inst = new Instance { StoryPath = path };
            inst.Screen = new ZScreen();
            inst.Screen.Attach();
            inst.Title = UniqueTitle(ZStory.TitleForFile(path));
            inst.Screen.Doc.SetTitle(inst.Title);
            _games.Add(inst);
            EnsureHooks();
            try
            {
                inst.Machine = new ZMachine(story, inst.Screen);
                WireMachine(inst);

                // Attach only once the machine exists. During Launch, AttachMap
                // runs BEFORE Start(); the post-Start observe below captures
                // the real starting room.
                if (AutoMapOn) AttachMap(inst);
                inst.Machine.Start();
                inst.Map?.Observe(inst.Machine, "");
            }
            catch (Exception ex)
            {
                inst.Screen.Print("\n[cannot start: " + ex.Message + "]\n");
                AteConsole.Warn("[ADKOM Text Editor] Z-Machine: " + ex.Message);
            }
        }

        /// <summary>Machine ↔ screen ↔ launcher wiring, shared by Launch and
        /// the domain-reload resume path.</summary>
        static void WireMachine(Instance inst)
        {
            inst.Machine.ChooseSaveFile = save => save
                ? EditorUtility.SaveFilePanel(L10n.Tr("Save game"), ZStory.StoryFolder, "save.azs", "azs")
                : EditorUtility.OpenFilePanel(L10n.Tr("Restore game"), ZStory.StoryFolder, "azs");
            // The auto-map rides alongside the game save as a ".map" sidecar,
            // so restoring a saved game brings its explored map back too.
            inst.Machine.AfterSave = p =>
            {
                inst.Map?.SaveTo(p + ".map");
                inst.Screen?.SaveTranscript(p + ".log");
            };
            inst.Machine.AfterRestore = p =>
            {
                inst.Screen?.LoadTranscript(p + ".log"); // bring the scrollback back
                if (inst.Map != null)
                {
                    inst.Map.LoadFrom(p + ".map");        // no-op if the sidecar is missing
                    inst.MapView?.SetMap(inst.Map);
                }
            };
            inst.Screen.OnLine = line =>
            {
                inst.Machine.CompleteInput(line);
                inst.Map?.Observe(inst.Machine, line);
            };
        }

        /// <summary>"Zork I", then "Zork I (1)", "Zork I (2)" … — unique
        /// against the games still running.</summary>
        static string UniqueTitle(string baseTitle)
        {
            bool Taken(string t)
            {
                foreach (var g in _games) if (g.Title == t) return true;
                return false;
            }
            if (!Taken(baseTitle)) return baseTitle;
            for (int i = 1; ; i++)
                if (!Taken(baseTitle + " (" + i + ")")) return baseTitle + " (" + i + ")";
        }

        static void EnsureHooks()
        {
            if (!_hooked)
            {
                AteApi.documentClosed += OnDocClosed;
                AteApi.activeDocumentChanged += OnActiveChanged;
                // A domain reload (or quit) wipes every VM; snapshot the
                // games first so Rehydrate can resume them afterwards.
                AssemblyReloadEvents.beforeAssemblyReload += SnapshotAll;
                EditorApplication.quitting += SnapshotAll;
                _hooked = true;
            }
            // One core key hook for ALL games; OnKey routes by active document.
            if (_window != null) _window.GameKeyHandler = OnKey;
        }

        /// <summary>A domain reload (script compile / play mode) or editor
        /// quit is imminent. Snapshot every running game — VM (.azx),
        /// transcript (.log), map (.map) — into the session folder and stamp
        /// its doc with the snapshot id, so Rehydrate can rebuild the game
        /// around the surviving transcript tab afterwards. When a snapshot
        /// cannot be taken, fall back to the honest "(unloaded)" mark. Both
        /// this and the reload run on the main thread, so a running machine
        /// is always cleanly parked at WaitingInput here.</summary>
        static void SnapshotAll()
        {
            foreach (var g in _games)
            {
                if (g.Screen == null || !g.Screen.IsValid) continue;
                var doc = g.Screen.Doc.InternalDoc;
                bool ok = false;
                try
                {
                    if (g.Machine != null && g.Machine.State == ZState.WaitingInput &&
                        !string.IsNullOrEmpty(g.StoryPath) && File.Exists(g.StoryPath))
                    {
                        Directory.CreateDirectory(SessionFolder);
                        string id = string.IsNullOrEmpty(doc.ZmSnapshotId)
                            ? Guid.NewGuid().ToString("N") : doc.ZmSnapshotId;
                        string basePath = Path.Combine(SessionFolder, id);
                        ok = g.Machine.SnapshotTo(basePath + ".azx");
                        if (ok)
                        {
                            g.Screen.SaveTranscript(basePath + ".log");
                            g.Map?.SaveTo(basePath + ".map");
                            doc.ZmSnapshotId = id;
                            doc.ZmStoryPath = g.StoryPath;
                            doc.GameUnloaded = false;
                        }
                    }
                }
                catch (Exception) { ok = false; }
                if (!ok)
                {
                    doc.ZmSnapshotId = null;
                    doc.ZmStoryPath = null;
                    // A halted game is simply over — no mark needed.
                    if (g.Machine == null || g.Machine.State != ZState.Halted)
                    {
                        doc.GameUnloaded = true;
                        try { g.Screen.Doc.SetTitle(g.Title + " " + L10n.Tr("(unloaded)")); } catch { }
                    }
                }
            }
        }

        /// <summary>Domain-reload resume — called by the window once its UI
        /// and session are up. Rebuilds a running game around every document
        /// stamped with a snapshot id: same story, same VM state, same
        /// transcript and map; the input line re-arms where it was. On any
        /// failure the tab falls back to the "(unloaded)" mark instead.</summary>
        internal static void Rehydrate(TextEditorWindow window)
        {
            _window = window;
            foreach (var ad in AteApi.Documents)
            {
                var doc = ad.InternalDoc;
                if (string.IsNullOrEmpty(doc.ZmSnapshotId)) continue;
                if (GameFor(ad) != null) continue; // already live in this domain
                string basePath = Path.Combine(SessionFolder, doc.ZmSnapshotId);
                Instance inst = null;
                bool ok = false;
                try
                {
                    byte[] story = !string.IsNullOrEmpty(doc.ZmStoryPath) && File.Exists(doc.ZmStoryPath)
                        ? ZStory.Load(doc.ZmStoryPath, out _) : null;
                    if (story != null && File.Exists(basePath + ".azx"))
                    {
                        inst = new Instance { Title = ad.DisplayName, StoryPath = doc.ZmStoryPath };
                        inst.Screen = new ZScreen();
                        inst.Screen.Attach(ad, inst.Title);
                        inst.Machine = new ZMachine(story, inst.Screen);
                        WireMachine(inst);
                        inst.Screen.LoadTranscript(basePath + ".log"); // scrollback + status line
                        ok = inst.Machine.ResumeFrom(basePath + ".azx");
                    }
                }
                catch (Exception) { ok = false; }
                if (ok)
                {
                    _games.Add(inst);
                    EnsureHooks();
                    if (AutoMapOn)
                    {
                        AttachMap(inst, front: false); // register the map tab without stealing the console area
                        inst.Map.LoadFrom(basePath + ".map"); // no-op if missing
                        inst.MapView.SetMap(inst.Map);
                    }
                    doc.GameUnloaded = false;
                    AteConsole.Log(string.Format(L10n.Tr("Z-Machine game '{0}' resumed after the reload."), inst.Title));
                }
                else
                {
                    try { if (ad.IsValid) ad.GameMode = false; } catch { }
                    doc.ZmSnapshotId = null;
                    doc.ZmStoryPath = null;
                    doc.GameUnloaded = true;
                    try { ad.SetTitle(ad.DisplayName + " " + L10n.Tr("(unloaded)")); } catch { }
                }
            }
            // Surface the ACTIVE game (pinned status line + its map tab).
            var g = GameFor(AteApi.ActiveDocument);
            if (g != null) { g.Screen.Redraw(); ShowMap(g); }
            CleanSessionFolder();
        }

        /// <summary>Deletes session-snapshot files no open document references
        /// (closed tabs, failed resumes), so the folder never accumulates.</summary>
        static void CleanSessionFolder()
        {
            try
            {
                if (!Directory.Exists(SessionFolder)) return;
                var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var ad in AteApi.Documents)
                {
                    string id = ad.InternalDoc.ZmSnapshotId;
                    if (!string.IsNullOrEmpty(id)) live.Add(id);
                }
                foreach (var f in Directory.GetFiles(SessionFolder))
                    if (!live.Contains(Path.GetFileNameWithoutExtension(f)))
                        File.Delete(f);
            }
            catch (Exception) { }
        }

        /// <summary>Drops instances whose tab vanished without a documentClosed
        /// we saw (e.g. across a domain reload).</summary>
        static void Prune()
        {
            for (int i = _games.Count - 1; i >= 0; i--)
                if (_games[i].Screen == null || !_games[i].Screen.IsValid)
                    Remove(_games[i]);
        }

        static Instance GameFor(AteDocument d)
        {
            if (d == null) return null;
            foreach (var g in _games)
                if (g.Screen != null && g.Screen.IsValid && d.Equals(g.Screen.Doc)) return g;
            return null;
        }

        static void AttachMap(Instance inst, bool front = true)
        {
            if (inst.Map == null) inst.Map = new ZMap();
            if (inst.MapView == null) inst.MapView = new ZMapView();
            inst.MapView.SetMap(inst.Map);
            ShowMap(inst, front);
            // Observe only once the machine has actually started (its memory
            // pointers are set) — see the Launch-time note above.
            if (inst.Machine != null && inst.Machine.State != ZState.Halted) inst.Map.Observe(inst.Machine, "");
        }

        static void DetachMap(Instance inst)
        {
            _window?.RemoveGameMapTab(inst);
            inst.Map = null;
            inst.MapView = null;
        }

        /// <summary>Surfaces this game's OWN console tab (one map tab per
        /// game, labeled with the game's title) and brings it to the front —
        /// deliberately every time: activating a game always fronts its map,
        /// even if the user had switched the console area to another tab.
        /// The resume path passes front=false to register tabs quietly.</summary>
        static void ShowMap(Instance inst, bool front = true)
        {
            if (inst.MapView == null) return;
            _window?.ShowGameMapTab(inst, inst.Title, inst.MapView, front);
        }

        static void OnDocClosed(AteDocument d)
        {
            for (int i = _games.Count - 1; i >= 0; i--)
                if (_games[i].Screen != null && Equals(d, _games[i].Screen.Doc))
                    Stop(_games[i]);
        }

        // A game tab became active: repaint at once so the pinned status bar
        // (which only applies to the active document) reappears instantly —
        // and bring THAT game's map into the Map tab, if it has one.
        static void OnActiveChanged(AteDocument d)
        {
            var g = GameFor(d);
            if (g == null) return;
            g.Screen.Redraw();
            ShowMap(g);
        }

        static void Stop(Instance inst)
        {
            // The game is over for real — its reload snapshot (if any) is
            // obsolete: delete the files and clear the doc stamps.
            try
            {
                var doc = inst.Screen?.Doc?.InternalDoc;
                if (doc != null && !string.IsNullOrEmpty(doc.ZmSnapshotId))
                {
                    string basePath = Path.Combine(SessionFolder, doc.ZmSnapshotId);
                    foreach (var ext in new[] { ".azx", ".log", ".map" })
                        if (File.Exists(basePath + ext)) File.Delete(basePath + ext);
                    doc.ZmSnapshotId = null;
                    doc.ZmStoryPath = null;
                }
            }
            catch (Exception) { }
            inst.Screen?.Close();
            inst.Machine = null;
            Remove(inst);
        }

        static void Remove(Instance inst)
        {
            _window?.RemoveGameMapTab(inst);
            _games.Remove(inst);
            if (_games.Count == 0 && _window != null) _window.GameKeyHandler = null;
        }

        /// <summary>Core key hook (installed on the window while any game
        /// runs). Routes to the game owning the active document; returns true
        /// to consume the key.</summary>
        static bool OnKey(KeyDownEvent e)
        {
            var g = GameFor(AteApi.ActiveDocument);
            if (g == null) return false;
            if (!g.Screen.InputMode)
                return e.character != '\0' || e.keyCode != KeyCode.None; // swallow, do nothing
            // Enter arrives as BOTH a '\n' character event and a Return keycode
            // event; routing only PRINTABLE chars here (and Enter/Backspace via
            // keycode) submits each line exactly once.
            char c = e.character;
            if (c >= ' ' && c < 127) { g.Screen.Key(c); return true; }
            switch (e.keyCode)
            {
                case KeyCode.Return: case KeyCode.KeypadEnter: g.Screen.Key('\n'); return true;
                case KeyCode.Backspace: g.Screen.Key('\b'); return true;
                default:
                    return e.keyCode != KeyCode.None && e.keyCode != KeyCode.LeftShift && e.keyCode != KeyCode.RightShift;
            }
        }
    }
}
#endif
