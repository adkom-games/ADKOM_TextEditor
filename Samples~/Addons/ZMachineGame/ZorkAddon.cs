// ATE Z-Machine addon — the [AteAddon] entry, lifecycle, menus, key routing.
//
// Plays any version-3 story file (.z3) — the format the MIT-licensed Zork
// trilogy compiles to. Story files are the USER'S: open a local file, or use
// the one-click download of Zork I/II/III (fetched, on your action, from
// their MIT-licensed preservation repo to your machine — ATE ships no game).
using System;
using System.IO;
using ADKOM.TextEditor.Scripting;
using UnityEditor;
using UnityEngine;

namespace AteZMachine
{
    [AteAddon(Name = "Z-Machine (Zork)", Category = "Games", ApiVersion = "1.1")]
    public class ZorkAddon : IAteAddonLifecycle
    {
        ZScreen _screen;
        ZMachine _machine;

        public void OnLoad()
        {
            AteApi.keyDown += OnKey;
            AteApi.documentClosed += d =>
            {
                if (_screen != null && Equals(d, _screen.Doc)) Stop();
            };
        }

        public void OnUnload() => Stop();
        public void OnFocusGained() { }
        public void OnFocusLost() { }

        /// <summary>Menu pick: choose a story file, then launch. If a game is
        /// already running, just focus it.</summary>
        public void Run()
        {
            if (_screen != null && _screen.IsValid) { _screen.Doc.Activate(); return; }

            string path = ChooseStory();
            if (string.IsNullOrEmpty(path)) return;
            Launch(path);
        }

        /// <summary>A tiny picker: the three downloadable Zorks plus "open a
        /// local .z3". Uses a menu at the mouse rather than a custom window.</summary>
        string ChooseStory()
        {
            string chosen = null;
            var menu = new GenericMenu();
            foreach (var g in ZStory.Downloadable)
            {
                var game = g;
                bool have = File.Exists(Path.Combine(ZStory.StoryFolder, g.File));
                menu.AddItem(new GUIContent(have ? game.Title : game.Title + "  (download)"), false, () =>
                {
                    string p = ZStory.EnsureDownloaded(game, out string err);
                    if (p == null) { Debug.LogWarning("[Z-Machine] " + game.Title + " download failed: " + err); return; }
                    Launch(p);
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open Story File… (.z3)"), false, () =>
            {
                string p = EditorUtility.OpenFilePanel("Open Z-machine story file", ZStory.StoryFolder, "z3,dat,zip");
                if (!string.IsNullOrEmpty(p)) Launch(p);
            });
            menu.ShowAsContext();
            return chosen; // launching happens in the item callbacks
        }

        void Launch(string path)
        {
            byte[] story = ZStory.Load(path, out string err);
            if (story == null) { Debug.LogWarning("[Z-Machine] could not read " + path + ": " + err); return; }

            _screen = new ZScreen();
            _screen.Attach();
            try
            {
                _machine = new ZMachine(story, _screen);
                _machine.ChooseSaveFile = save =>
                {
                    return save
                        ? EditorUtility.SaveFilePanel("Save game", ZStory.StoryFolder, "save.azs", "azs")
                        : EditorUtility.OpenFilePanel("Restore game", ZStory.StoryFolder, "azs");
                };
                _screen.OnLine = line => _machine.CompleteInput(line);
                _screen.Doc.SetTitle(Path.GetFileNameWithoutExtension(path));
                _machine.Start();
            }
            catch (Exception ex)
            {
                _screen.Print("\n[cannot start: " + ex.Message + "]\n");
                Debug.LogWarning("[Z-Machine] " + ex.Message);
            }
        }

        void Stop()
        {
            _screen?.Close();
            _screen = null;
            _machine = null;
        }

        void OnKey(AteKeyEvent e)
        {
            if (_screen == null || !_screen.IsValid) return;
            var active = AteApi.ActiveDocument;
            if (active == null || !active.Equals(_screen.Doc)) return;
            if (!_screen.InputMode)
            {
                // Not awaiting input (game printing / ended) — swallow so keys
                // don't leak into the editor, but do nothing.
                if (e.Character != '\0' || e.Key != KeyCode.None) e.Handled = true;
                return;
            }
            if (e.Character != '\0')
            {
                _screen.Key(e.Character);
                e.Handled = true;
            }
            else switch (e.Key)
            {
                case KeyCode.Return: case KeyCode.KeypadEnter: _screen.Key('\n'); e.Handled = true; break;
                case KeyCode.Backspace: _screen.Key('\b'); e.Handled = true; break;
                default:
                    if (e.Key != KeyCode.None && e.Key != KeyCode.LeftShift && e.Key != KeyCode.RightShift)
                        e.Handled = true;
                    break;
            }
        }
    }
}
