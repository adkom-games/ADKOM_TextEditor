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
                _screen.OnLine = line => _machine.CompleteInput(line);
                _screen.Doc.SetTitle(Path.GetFileNameWithoutExtension(path));

                if (!_hooked) { AteApi.documentClosed += OnDocClosed; _hooked = true; }
                _window.GameKeyHandler = OnKey;

                _machine.Start();
            }
            catch (Exception ex)
            {
                _screen.Print("\n[cannot start: " + ex.Message + "]\n");
                AteConsole.Warn("[ADKOM Text Editor] Z-Machine: " + ex.Message);
            }
        }

        static void OnDocClosed(AteDocument d)
        {
            if (_screen != null && Equals(d, _screen.Doc)) Stop();
        }

        static void Stop()
        {
            if (_window != null) _window.GameKeyHandler = null;
            _screen?.Close();
            _screen = null;
            _machine = null;
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
