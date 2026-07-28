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
// itself is in the Rogue* modules.
using System;
using ADKOM.TextEditor.Scripting;
using UnityEngine;

namespace AteRogue
{
    [AteAddon(Name = "Rogue", Category = "Games", ApiVersion = "1.1")]
    public class RogueAddon : IAteAddonLifecycle
    {
        Game _game;

        public void OnLoad()
        {
            AteApi.keyDown += OnKey;
            AteApi.documentClosed += d =>
            {
                if (_game != null && Equals(d, _game.Term.Doc)) Stop();
            };
        }

        public void OnUnload() => Stop();
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
            term.Attach(Term.NewScreenDocument());
            _game = new Game(term);
            _game.OnQuitRequested = Stop;
            _game.Start();
        }

        void Stop()
        {
            var g = _game;
            _game = null;
            if (g != null && g.Term.IsValid && g.Term.Doc.GameMode)
            {
                g.Term.Doc.GameMode = false;
                g.Term.Doc.Close(discardChanges: true);
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
                case KeyCode.LeftArrow: g.Key('h', false); e.Handled = true; break;
                case KeyCode.DownArrow: g.Key('j', false); e.Handled = true; break;
                case KeyCode.UpArrow: g.Key('k', false); e.Handled = true; break;
                case KeyCode.RightArrow: g.Key('l', false); e.Handled = true; break;
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
