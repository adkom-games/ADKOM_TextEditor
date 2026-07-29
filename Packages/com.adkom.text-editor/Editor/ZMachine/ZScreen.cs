#if UNITY_EDITOR
// ATE Z-Machine — terminal screen over an ATE game-mode document.
//
// The transcript is a GROWING, scrollable document: new output is appended and
// the view scrolls to the bottom, so the player can scroll UP through history
// (real scrollback). The status line (location + score/moves) is a PINNED
// overlay above the transcript (SetStatusBar) — it never scrolls. Output is
// word-wrapped by the screen to the viewport width, so a line never needs a
// horizontal scrollbar. Input is echoed inline; Enter submits to the interpreter.
//
// NOTE: the screen no longer fits itself to the viewport height. The old
// fixed-grid terminal re-measured the viewport every render and rebuilt a
// height-sized canvas; a measurement feedback loop shaved a row off per command
// until it collapsed to a few lines. Growing the doc and letting the editor
// scroll removes that height dependency entirely (only the wrap WIDTH is read).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ADKOM.TextEditor.Scripting;
using UnityEngine;

namespace AteZMachine
{
    public sealed class ZScreen : IZScreen
    {
        int _w = 80;                              // wrap width (fitted to viewport)

        AteDocument _doc;
        readonly List<string> _lines = new List<string> { "" };  // transcript; last = current line
        string _statusLeft = "", _statusRight = "";
        bool _inputMode;
        bool _pendingScrollEnd;                   // force scroll to the input line after a restore
        int _inputDocRow = 2;                     // doc row of the input line (set by Render)
        int _coloredRow = -1;                     // row currently carrying the prompt color
        readonly StringBuilder _input = new StringBuilder();

        static readonly Color StatusFg = Color.black;
        static readonly Color StatusBg = new Color(0.75f, 0.75f, 0.75f);
        static readonly Color PromptCol = new Color(0.5f, 0.85f, 1f);

        public AteDocument Doc => _doc;
        public bool IsValid => _doc != null && _doc.IsValid;
        public bool InputMode => _inputMode;

        public Action<string> OnLine;

        public void Attach()
        {
            _doc = AteApi.NewDocument("");
            _doc.SetTitle("Z-Machine");
            _doc.SetFont("Consolas", 15);
            _doc.GameMode = true;
            UpdateWidth();
        }

        public void Close()
        {
            if (IsValid && _doc.GameMode) { _doc.GameMode = false; _doc.Close(discardChanges: true); }
            _doc = null;
        }

        /// <summary>Tracks the viewport WIDTH for wrapping. Height is irrelevant
        /// now — the transcript grows and the editor scrolls it. Returns true if
        /// the width changed.</summary>
        bool UpdateWidth()
        {
            if (!IsValid) return false;
            if (!_doc.TryGetViewport(out _, out int c)) return false;
            c = Mathf.Max(20, c);
            if (c == _w) return false;
            _w = c;
            return true;
        }

        // ---- IZScreen ----

        public void Print(string s)
        {
            foreach (char ch in s ?? "")
            {
                if (ch == '\n') { _lines.Add(""); continue; }
                if (ch == '\r') continue;
                string cur = _lines[_lines.Count - 1];
                if (cur.Length >= _w)
                {
                    int sp = cur.LastIndexOf(' ');
                    if (sp > 0 && sp > _w - 20)
                    {
                        string tail = cur.Substring(sp + 1);
                        _lines[_lines.Count - 1] = cur.Substring(0, sp);
                        _lines.Add(tail);
                    }
                    else _lines.Add("");
                }
                _lines[_lines.Count - 1] += ch;
            }
            TrimScrollback();
            Render();
        }

        public void SetStatus(string location, int a, int b, bool timeGame)
        {
            // The bar lays out with flex (location left, score/moves right), so
            // no space padding — just the two strings.
            _statusRight = timeGame
                ? string.Format("Time: {0}:{1:00}", ((a + 11) % 12) + 1, b)
                : string.Format("Score: {0}   Moves: {1}", a, b);
            _statusLeft = location ?? "";
            Render();
        }

        /// <summary>Repaints the whole screen — used when this document becomes
        /// the active tab again so the pinned status bar reappears at once.</summary>
        public void Redraw()
        {
            if (IsValid) Render();
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

        // ---- Key input (called by the core key hook) ----

        public void Key(char c)
        {
            if (!_inputMode) return;
            if (c == '\n' || c == '\r')
            {
                string line = _input.ToString();
                _lines[_lines.Count - 1] += line;   // echo the committed line
                _lines.Add("");
                _inputMode = false;
                _input.Clear();
                Render();
                OnLine?.Invoke(line);
                return;
            }
            if (c == '\b') { if (_input.Length > 0) _input.Length--; }
            else if (c >= ' ' && c < 127) _input.Append(c);
            RenderInputLine(); // only the input row changes while typing
        }

        /// <summary>Fast path for keystrokes: rewrite ONLY the input line (the
        /// transcript is unchanged), instead of the whole screen — a single
        /// document edit per character rather than a full SetText.</summary>
        void RenderInputLine()
        {
            if (!IsValid || !_inputMode) return;
            string line = _lines[_lines.Count - 1] + _input + "_";
            // Overflow: show the tail so the cursor stays on screen.
            int caretCol = Mathf.Max(1, Mathf.Min(line.Length, _w)); // AT the "_" glyph
            if (line.Length > _w) line = line.Substring(line.Length - _w);
            _doc.WriteAt(_inputDocRow, 1, line.PadRight(_w));
            _doc.SetColor(_inputDocRow, 1, _w + 1, PromptCol, null);
            _coloredRow = _inputDocRow;
            _doc.SetStatusBar(_statusLeft, _statusRight, StatusFg, StatusBg); // keep the bar present while typing
            _doc.GoTo(_inputDocRow, caretCol); // block caret coincides with the "_"; scrolls into view
        }

        // ---- Rendering ----

        void TrimScrollback()
        {
            const int keep = 500;
            if (_lines.Count > keep) _lines.RemoveRange(0, _lines.Count - keep);
        }

        void Render()
        {
            if (!IsValid) return;
            UpdateWidth();

            var view = new List<string>(_lines);
            if (_inputMode) view[view.Count - 1] = view[view.Count - 1] + _input + "_";

            // The document holds ONLY the transcript (grows and scrolls). The
            // status line is a pinned overlay (SetStatusBar) that never scrolls.
            var sb = new StringBuilder();
            for (int i = 0; i < view.Count; i++) { if (i > 0) sb.Append('\n'); sb.Append(view[i]); }
            _doc.SetText(sb.ToString());
            _doc.SetStatusBar(_statusLeft, _statusRight, StatusFg, StatusBg);

            // Colors are a positional overlay that survives SetText, so a prompt
            // color left on the previous input row would stain a now-committed
            // line — clear it before recoloring.
            if (_coloredRow > 0) { _doc.SetColor(_coloredRow, 1, _w + 1, null, null); _coloredRow = -1; }

            _inputDocRow = view.Count;
            if (_inputMode) { _doc.SetColor(_inputDocRow, 1, _w + 1, PromptCol, null); _coloredRow = _inputDocRow; }

            int lastLen = view.Count > 0 ? view[view.Count - 1].Length : 0;
            _doc.GoTo(_inputDocRow, Mathf.Max(1, Mathf.Min(lastLen, _w))); // caret at the cursor glyph; scroll into view

            // After a restore, force the view to the end through the post-restore
            // output until the game is waiting for input, so the active input
            // line is shown (not the top of the reloaded transcript).
            if (_pendingScrollEnd)
            {
                _doc.ScrollToEnd();
                if (_inputMode) _pendingScrollEnd = false;
            }
        }

        // ---- Transcript persistence (sidecar to the game save) ----
        // Restore reloads game state; without this the on-screen history would
        // be only what was typed since launch, leaving the transcript near-empty
        // after a restore. The transcript rides alongside the save so restoring
        // brings the scrollback back too.

        const string TransHeader = "ATE-ZSCROLL\t2";

        public void SaveTranscript(string path)
        {
            try
            {
                using (var w = new StreamWriter(File.Create(path)))
                {
                    w.WriteLine(TransHeader);
                    w.WriteLine(_statusLeft ?? "");
                    w.WriteLine(_statusRight ?? "");
                    w.WriteLine(_lines.Count);
                    foreach (var l in _lines) w.WriteLine(l ?? "");
                }
            }
            catch { /* transcript sidecar is best-effort */ }
        }

        public bool LoadTranscript(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var r = new StreamReader(File.OpenRead(path)))
                {
                    if (r.ReadLine() != TransHeader) return false;
                    _statusLeft = r.ReadLine() ?? "";
                    _statusRight = r.ReadLine() ?? "";
                    if (!int.TryParse(r.ReadLine(), out int n) || n < 0) return false;
                    _lines.Clear();
                    for (int i = 0; i < n; i++) _lines.Add(r.ReadLine() ?? "");
                    if (_lines.Count == 0) _lines.Add("");
                }
                _inputMode = false;
                _input.Clear();
                _pendingScrollEnd = true; // show the input line after the restore settles
                Render();
                return true;
            }
            catch { return false; }
        }
    }
}
#endif
