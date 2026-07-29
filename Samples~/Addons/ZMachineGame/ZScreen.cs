// ATE Z-Machine — terminal screen over an ATE game-mode document.
//
// A scrolling transcript with a status line on top, word-wrapped to the
// window width. Input is echoed inline; Enter submits the line to the
// interpreter. Colors use the game API's fg/bg overlay.
using System;
using System.Collections.Generic;
using System.Text;
using ADKOM.TextEditor.Scripting;
using UnityEngine;

namespace AteZMachine
{
    public sealed class ZScreen : IZScreen
    {
        public const int W = 80, H = 30;
        const int Top = 1;              // row 0 is the status line
        const int Rows = H - Top;       // transcript rows

        AteDocument _doc;
        readonly List<string> _lines = new List<string> { "" };  // scrollback; last = current line
        string _status = "";
        bool _inputMode;
        readonly StringBuilder _input = new StringBuilder();

        static readonly Color StatusFg = Color.black;
        static readonly Color StatusBg = new Color(0.75f, 0.75f, 0.75f);
        static readonly Color Text = new Color(0.85f, 0.85f, 0.85f);
        static readonly Color Prompt = new Color(0.5f, 0.85f, 1f);

        public AteDocument Doc => _doc;
        public bool IsValid => _doc != null && _doc.IsValid;
        public bool InputMode => _inputMode;

        public Action<string> OnLine;   // addon sets this to feed the interpreter

        public void Attach()
        {
            var blank = new StringBuilder();
            for (int y = 0; y < H; y++) blank.Append(new string(' ', W)).Append(y < H - 1 ? "\n" : "");
            _doc = AteApi.NewDocument(blank.ToString());
            _doc.SetTitle("Z-Machine");
            _doc.SetFont("Consolas", 15);
            _doc.GameMode = true;
        }

        public void Close()
        {
            if (IsValid && _doc.GameMode) { _doc.GameMode = false; _doc.Close(discardChanges: true); }
            _doc = null;
        }

        // ---- IZScreen ----

        public void Print(string s)
        {
            foreach (char c in s ?? "")
            {
                if (c == '\n') { _lines.Add(""); continue; }
                if (c == '\r') continue;
                string cur = _lines[_lines.Count - 1];
                if (cur.Length >= W)
                {
                    // Word-wrap: break at the last space if there is one.
                    int sp = cur.LastIndexOf(' ');
                    if (sp > 0 && sp > W - 20)
                    {
                        string tail = cur.Substring(sp + 1);
                        _lines[_lines.Count - 1] = cur.Substring(0, sp);
                        _lines.Add(tail);
                    }
                    else _lines.Add("");
                }
                _lines[_lines.Count - 1] += c;
            }
            TrimScrollback();
            Render();
        }

        public void SetStatus(string location, int a, int b, bool timeGame)
        {
            string right = timeGame
                ? string.Format("Time: {0}:{1:00}", ((a + 11) % 12) + 1, b)
                : string.Format("Score: {0}   Moves: {1}", a, b);
            string left = " " + (location ?? "");
            int pad = W - left.Length - right.Length - 1;
            _status = left + (pad > 0 ? new string(' ', pad) : " ") + right + " ";
            if (_status.Length > W) _status = _status.Substring(0, W);
            Render();
        }

        public void RequestLine()
        {
            _inputMode = true;
            _input.Clear();
            Render();
        }

        public void Quit(string message)
        {
            _inputMode = false;
            if (!string.IsNullOrEmpty(message)) Print("\n" + message);
            Print("\n[The game has ended. Close this tab.]\n");
        }

        // ---- Key input (called by the addon) ----

        public void Key(char c)
        {
            if (!_inputMode) return;
            if (c == '\n' || c == '\r')
            {
                string line = _input.ToString();
                _lines[_lines.Count - 1] += line;     // echo committed
                _lines.Add("");
                _inputMode = false;
                _input.Clear();
                Render();
                OnLine?.Invoke(line);
                return;
            }
            if (c == '\b')
            {
                if (_input.Length > 0) _input.Length--;
            }
            else if (c >= ' ' && c < 127 && _input.Length < W - 4)
                _input.Append(c);
            Render();
        }

        // ---- Rendering ----

        void TrimScrollback()
        {
            const int keep = 400;
            if (_lines.Count > keep) _lines.RemoveRange(0, _lines.Count - keep);
        }

        void Render()
        {
            if (!IsValid) return;

            // Status line (row 0), inverse.
            string status = _status.PadRight(W).Substring(0, W);
            _doc.WriteAt(1, 1, status);
            _doc.SetColor(1, 1, W + 1, StatusFg, StatusBg);

            // Build the visible transcript: the tail that fits, plus the input
            // line being typed when in input mode.
            var view = new List<string>(_lines);
            if (_inputMode)
            {
                view[view.Count - 1] = view[view.Count - 1] + _input.ToString() + "_";
            }
            int first = Math.Max(0, view.Count - Rows);
            for (int r = 0; r < Rows; r++)
            {
                int idx = first + r;
                string line = idx < view.Count ? view[idx] : "";
                if (line.Length > W) line = line.Substring(0, W);
                _doc.WriteAt(Top + r + 1, 1, line.PadRight(W));
                _doc.SetColor(Top + r + 1, 1, W + 1, Text, null);
            }
            // Tint the current input line so it reads as a prompt.
            if (_inputMode)
            {
                int row = Top + (Math.Min(view.Count, Rows)) ;
                if (row >= Top + 1 && row <= H)
                    _doc.SetColor(row, 1, W + 1, Prompt, null);
            }
            _doc.GoTo(H, W); // park caret out of the way
        }
    }
}
