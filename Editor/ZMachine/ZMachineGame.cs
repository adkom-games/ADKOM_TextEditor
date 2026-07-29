#if UNITY_EDITOR
// ATE Z-Machine — built-in launcher (Tools → Z-Machine (Zork)…).
//
// The interpreter is part of the editor, not an addon: story-file downloads
// are a native ATE feature (like auto-update), trusted by installation. Key
// input goes through a core window hook rather than the addon input events
// (which an addon reload resets), so a game keeps working across reloads.
using System;
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
        static ZScreen _screen;
        static ZMachine _machine;
        static TextEditorWindow _window;
        static bool _hooked;
        static ZMap _map;
        static ZMapView _mapView;

        const string AutoMapPref = "ADKOM.ZMachine.AutoMap";
        static bool AutoMapOn { get => EditorPrefs.GetBool(AutoMapPref, true); set => EditorPrefs.SetBool(AutoMapPref, value); }

        /// <summary>Adds the Z-Machine items as a submenu of the given menu
        /// (Tools → Z-Machine (Zork) → Zork I / … / Open Story File…). A real
        /// submenu, not a popped-up context menu — showing a second menu from
        /// a menu-item callback is silently dropped by Unity.</summary>
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
                    if (Focused()) return;
                    string p = ZStory.EnsureDownloaded(game, out string err);
                    if (p == null) { AteConsole.Warn("[ADKOM Text Editor] " + game.Title + " " + L10n.Tr("download failed: ") + err); return; }
                    Launch(p);
                });
            }
            m.AddItem(new GUIContent(prefix + L10n.Tr("Open Story File… (.z3)")), false, () =>
            {
                _window = window;
                if (Focused()) return;
                string p = EditorUtility.OpenFilePanel(L10n.Tr("Open Z-machine story file"), ZStory.StoryFolder, "z3,dat");
                if (!string.IsNullOrEmpty(p)) Launch(p);
            });
            m.AddSeparator(prefix);
            m.AddItem(new GUIContent(prefix + L10n.Tr("Auto-map (build a map as you explore)")), AutoMapOn, () =>
            {
                AutoMapOn = !AutoMapOn;
                if (_map != null && !AutoMapOn) DetachMap();
                else if (_machine != null && AutoMapOn) AttachMap();
            });
        }

        static bool Focused()
        {
            if (_screen != null && _screen.IsValid) { _screen.Doc.Activate(); return true; }
            return false;
        }

        static void Launch(string path)
        {
            byte[] story = ZStory.Load(path, out string err);
            if (story == null) { AteConsole.Warn("[ADKOM Text Editor] could not read " + path + ": " + err); return; }

            _screen = new ZScreen();
            _screen.Attach();
            try
            {
                _machine = new ZMachine(story, _screen);
                _machine.ChooseSaveFile = save => save
                    ? EditorUtility.SaveFilePanel(L10n.Tr("Save game"), ZStory.StoryFolder, "save.azs", "azs")
                    : EditorUtility.OpenFilePanel(L10n.Tr("Restore game"), ZStory.StoryFolder, "azs");
                // The auto-map rides alongside the game save as a ".map" sidecar,
                // so restoring a saved game brings its explored map back too.
                _machine.AfterSave = p => _map?.SaveTo(p + ".map");
                _machine.AfterRestore = p =>
                {
                    if (_map == null) return;
                    _map.LoadFrom(p + ".map"); // no-op if the sidecar is missing
                    _mapView?.SetMap(_map);
                };
                _screen.OnLine = OnPlayerLine;
                _screen.Doc.SetTitle(Path.GetFileNameWithoutExtension(path));

                if (!_hooked) { AteApi.documentClosed += OnDocClosed; _hooked = true; }
                _window.GameKeyHandler = OnKey;

                if (AutoMapOn) AttachMap();
                _machine.Start();
                _map?.Observe(_machine, ""); // capture the starting room
            }
            catch (Exception ex)
            {
                _screen.Print("\n[cannot start: " + ex.Message + "]\n");
                AteConsole.Warn("[ADKOM Text Editor] Z-Machine: " + ex.Message);
            }
        }

        static void OnPlayerLine(string line)
        {
            _machine.CompleteInput(line);
            _map?.Observe(_machine, line);
        }

        static void AttachMap()
        {
            if (_map == null) _map = new ZMap();
            if (_mapView == null) _mapView = new ZMapView();
            _mapView.SetMap(_map);
            _window?.SetGameMapView(_mapView);
            // Observe only once the machine has actually started (its memory
            // pointers are set). During Launch, AttachMap runs BEFORE Start();
            // the post-Start observe there captures the real starting room.
            if (_machine != null && _machine.State != ZState.Halted) _map.Observe(_machine, "");
        }

        static void DetachMap()
        {
            _window?.SetGameMapView(null);
            _map = null;
            _mapView = null;
        }

        static void OnDocClosed(AteDocument d)
        {
            if (_screen != null && Equals(d, _screen.Doc)) Stop();
        }

        static void Stop()
        {
            if (_window != null) { _window.GameKeyHandler = null; _window.SetGameMapView(null); }
            _screen?.Close();
            _screen = null;
            _machine = null;
            _map = null;
            _mapView = null;
        }

        /// <summary>Core key hook (installed on the window while a game runs).
        /// Returns true to consume the key.</summary>
        static bool OnKey(KeyDownEvent e)
        {
            if (_screen == null || !_screen.IsValid) return false;
            var active = AteApi.ActiveDocument;
            if (active == null || !active.Equals(_screen.Doc)) return false;
            if (!_screen.InputMode)
                return e.character != '\0' || e.keyCode != KeyCode.None; // swallow, do nothing
            // Enter arrives as BOTH a '\n' character event and a Return keycode
            // event; routing only PRINTABLE chars here (and Enter/Backspace via
            // keycode) submits each line exactly once.
            char c = e.character;
            if (c >= ' ' && c < 127) { _screen.Key(c); return true; }
            switch (e.keyCode)
            {
                case KeyCode.Return: case KeyCode.KeypadEnter: _screen.Key('\n'); return true;
                case KeyCode.Backspace: _screen.Key('\b'); return true;
                default:
                    return e.keyCode != KeyCode.None && e.keyCode != KeyCode.LeftShift && e.keyCode != KeyCode.RightShift;
            }
        }
    }
}
#endif
