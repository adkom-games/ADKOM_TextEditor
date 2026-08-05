// ATE Rogue 5.4.4 port — addon entry.
//
// A port of Rogue 5.4.4 (the classic BSD Rogue: "Exploring the Dungeons of
// Doom") to the ADKOM Text Editor game API. Based on the BSD-licensed
// Rogue 5.4.4 source (Copyright 1980-1983, 1985, Michael Toy, Ken Arnold
// and Glenn Wichman; distributed under a BSD-style license — see
// rogue.rogueforge.net). This port is a from-scratch C# translation of the
// original game rules, tables, and formulas for the ATE addon framework.
//
// Folder addon: all .cs files in this folder compile together (Multi-File
// Addons). Entry point + input routing + lifecycle live here; the game
// itself is in the Rogue* modules. STATEFUL (AteApi 1.2): the running
// dungeon survives domain reloads — RogueSave serializes the Game graph,
// the host stores it, and RestoreState rebuilds the game around the screen
// document that survived in the session (found by StateTag). A pending
// prompt (--More--, direction, …) does not survive; play resumes in Play
// mode on the same turn.
using System;
using ADKOM.TextEditor.Scripting;
using UnityEngine;

namespace AteRogue
{
    [AteAddon(Name = "Rogue", Category = "Games", ApiVersion = "1.2")]
    public class RogueAddon : IAteAddonStateful
    {
        const string Tag = "ate-rogue"; // StateTag claiming our screen document

        Game _game;
        bool _persisting; // SaveState returned state: OnUnload must keep the doc

        public void OnLoad()
        {
            AteApi.keyDown += OnKey;
            AteApi.documentClosed += d =>
            {
                if (_game != null && Equals(d, _game.Term.Doc)) Stop();
            };
        }

        public void OnUnload()
        {
            if (_persisting)
            {
                // The screen document lives on in the session; RestoreState
                // will re-claim it by its StateTag after the reload.
                _game = null;
                return;
            }
            Stop();
        }

        public void OnFocusGained() { }
        public void OnFocusLost() { }

        public void Run()
        {
            if (_game != null && _game.Term.IsValid)
            {
                _game.Term.Doc.Activate();
                return;
            }
            var term = new Term();
            var doc = Term.NewScreenDocument();
            doc.StateTag = Tag;
            term.Attach(doc);
            _game = new Game(term);
            _game.OnQuitRequested = Stop;
            _game.Start();
        }

        // ---- State persistence (AteApi 1.2) ----

        public string SaveState()
        {
            _persisting = false;
            var g = _game;
            if (g == null || !g.Term.IsValid || g.GameOver || g.Mode == InputMode.EndScreen)
                return null;
            try
            {
                string data = RogueSave.Serialize(g);
                _persisting = data != null;
                return data;
            }
            catch (Exception) { return null; } // unsupported state → fresh game
        }

        public void RestoreState(string state)
        {
            if (_game != null || string.IsNullOrEmpty(state)) return;
            AteDocument doc = null;
            foreach (var d in AteApi.Documents)
                if (d.IsValid && d.StateTag == Tag) { doc = d; break; }
            if (doc == null) return; // the screen tab is gone — start fresh via Run

            doc.SetTitle("Rogue");
            doc.GameMode = true;
            var term = new Term();
            term.Attach(doc); // forces a full repaint on the next Flush
            var g = new Game(term);
            if (!RogueSave.Restore(state, g))
            {
                doc.GameMode = false; // could not rebuild — leave the tab as text
                return;
            }
            g.OnQuitRequested = Stop;
            g.Mode = InputMode.Play;              // pending prompts do not survive
            Rnd.Seed(Environment.TickCount);      // fresh dice; documented divergence
            _game = g;
            g.Redraw();
        }

        void Stop()
        {
            var g = _game;
            _game = null;
            _persisting = false;
            if (g != null && g.Term.IsValid)
            {
                g.Term.Doc.StateTag = null; // a quit game never resurrects
                if (g.Term.Doc.GameMode)
                {
                    g.Term.Doc.GameMode = false;
                    g.Term.Doc.Close(discardChanges: true);
                }
            }
        }

        void OnKey(AteKeyEvent e)
        {
            var g = _game;
            if (g == null || !g.Term.IsValid) return;
            var active = AteApi.ActiveDocument;
            if (active == null || !active.Equals(g.Term.Doc)) return;
            // Rogue is char-driven: act on CHARACTER events (Unity sends each
            // press as keycode + character event) plus the few special keys.
            if (e.Character != '\0')
            {
                g.Key(e.Character, e.Ctrl);
                e.Handled = true;
            }
            else switch (e.Key)
            {
                // Shift+arrow runs, exactly like the shifted letters.
                case KeyCode.LeftArrow: g.Key(e.Shift ? 'H' : 'h', false); e.Handled = true; break;
                case KeyCode.DownArrow: g.Key(e.Shift ? 'J' : 'j', false); e.Handled = true; break;
                case KeyCode.UpArrow: g.Key(e.Shift ? 'K' : 'k', false); e.Handled = true; break;
                case KeyCode.RightArrow: g.Key(e.Shift ? 'L' : 'l', false); e.Handled = true; break;
                case KeyCode.Escape: g.Key((char)27, false); e.Handled = true; break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: g.Key('\n', false); e.Handled = true; break;
                default:
                    // Swallow bare modifier/keycode events over the game so
                    // nothing leaks into editor commands mid-game.
                    if (e.Key != KeyCode.None &&
                        e.Key != KeyCode.LeftShift && e.Key != KeyCode.RightShift &&
                        e.Key != KeyCode.LeftControl && e.Key != KeyCode.RightControl &&
                        e.Key != KeyCode.LeftAlt && e.Key != KeyCode.RightAlt)
                        e.Handled = true;
                    break;
            }
        }
    }
}
