#if UNITY_EDITOR
// ATE Z-Machine — terminal screen over an ATE game-mode document.
//
// A scrolling transcript with a pinned status line on top, word-wrapped to
// the window width. The document is sized to the actual viewport, so it has
// no scrollbar of its own: content fills from the top and scrolls up only
// once it overflows — a real terminal. Input is echoed inline; Enter submits
// the line to the interpreter.
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
        const int StatusRows = 1;                 // document line 1 = status
        int _w = 80, _h = 24;                     // grid size (fitted to viewport)

        AteDocument _doc;
        readonly List<string> _lines = new List<string> { "" };  // scrollback; last = current line
        string _status = "";
        bool _inputMode;
        int _inputDocRow = 2;                     // doc row of the input line (set by Render)
        readonly StringBuilder _input = new StringBuilder();

        static readonly Color StatusFg = Color.black;
        static readonly Color StatusBg = new Color(0.75f, 0.75f, 0.75f);
        static readonly Color TextCol = new Color(0.85f, 0.85f, 0.85f);
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
            Resize();                             // may stay default until layout resolves
        }

        public void Close()
        {
            if (IsValid && _doc.GameMode) { _doc.GameMode = false; _doc.Close(discardChanges: true); }
            _doc = null;
        }

        /// <summary>Fits the document to the viewport (rows × cols). Returns
        /// true if the size changed (and the blank canvas was rebuilt).</summary>
        bool Resize()
        {
            if (!IsValid) return false;
            if (!_doc.TryGetViewport(out int r, out int c)) { EnsureCanvas(); return false; }
            r = Mathf.Max(4, r);
            c = Mathf.Max(20, c);
            if (r == _h && c == _w && _doc.LineCount == _h) return false;
            _h = r; _w = c;
            RebuildCanvas();
            return true;
        }

        void EnsureCanvas()
        {
            if (_doc.LineCount != _h) RebuildCanvas();
        }

        void RebuildCanvas()
        {
            var sb = new StringBuilder();
            for (int y = 0; y < _h; y++) sb.Append(new string(' ', _w)).Append(y < _h - 1 ? "\n" : "");
            _doc.SetText(sb.ToString());
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
            string right = timeGame
                ? string.Format("Time: {0}:{1:00}", ((a + 11) % 12) + 1, b)
                : string.Format("Score: {0}   Moves: {1}", a, b);
            // The right side (score/moves) must always show; cap the location.
            string loc = location ?? "";
            int maxLoc = _w - right.Length - 3;
            if (maxLoc < 1) maxLoc = 1;
            if (loc.Length > maxLoc) loc = loc.Substring(0, Math.Max(1, maxLoc - 1)) + "…";
            string left = " " + loc;
            int pad = _w - left.Length - right.Length - 1;
            _status = left + (pad > 0 ? new string(' ', pad) : " ") + right + " ";
            if (_status.Length > _w) _status = _status.Substring(0, _w);
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
            else if (c >= ' ' && c < 127 && _input.Length < _w - 4) _input.Append(c);
            RenderInputLine(); // only the input row changes while typing
        }

        /// <summary>Fast path for keystrokes: rewrite ONLY the input line (the
        /// transcript is unchanged), instead of the whole screen — a single
        /// document edit per character rather than one per row.</summary>
        void RenderInputLine()
        {
            if (!IsValid || !_inputMode) return;
            string line = _lines[_lines.Count - 1] + _input + "_";
            // Overflow: show the tail so the cursor stays on screen.
            if (line.Length > _w) line = line.Substring(line.Length - _w);
            _doc.WriteAt(_inputDocRow, 1, line.PadRight(_w));
            _doc.SetColor(_inputDocRow, 1, _w + 1, PromptCol, null);
            _doc.GoTo(_inputDocRow, 1);
        }

        // ---- Transcript persistence (sidecar to the game save) ----
        // Restore reloads game state; without this the on-screen history would
        // be only what was typed since launch, leaving the transcript near-empty
        // after a restore. The transcript rides alongside the save so restoring
        // brings the scrollback back too.

        const string TransHeader = "ATE-ZSCROLL\t1";

        public void SaveTranscript(string path)
        {
            try
            {
                using (var w = new StreamWriter(File.Create(path)))
                {
                    w.WriteLine(TransHeader);
                    w.WriteLine(_status ?? "");
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
                    _status = r.ReadLine() ?? "";
                    if (!int.TryParse(r.ReadLine(), out int n) || n < 0) return false;
                    _lines.Clear();
                    for (int i = 0; i < n; i++) _lines.Add(r.ReadLine() ?? "");
                    if (_lines.Count == 0) _lines.Add("");
                }
                _inputMode = false;
                _input.Clear();
                Render();
                return true;
            }
            catch { return false; }
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
            Resize();                               // adapt to window/zoom changes
            EnsureCanvas();

            string status = (_status ?? "").PadRight(_w);
            if (status.Length > _w) status = status.Substring(0, _w);
            _doc.WriteAt(1, 1, status);
            _doc.SetColor(1, 1, _w + 1, StatusFg, StatusBg);

            int rows = _h - StatusRows;             // transcript rows: doc lines 2.._h
            var view = new List<string>(_lines);
            if (_inputMode) view[view.Count - 1] = view[view.Count - 1] + _input + "_";

            int first = Math.Max(0, view.Count - rows);
            int inputRow = 1 + StatusRows;
            for (int r = 0; r < rows; r++)
            {
                int idx = first + r;
                string line = idx < view.Count ? view[idx] : "";
                if (line.Length > _w) line = line.Substring(0, _w);
                int docRow = 1 + StatusRows + r;
                _doc.WriteAt(docRow, 1, line.PadRight(_w));
                _doc.SetColor(docRow, 1, _w + 1, TextCol, null);
                if (idx == view.Count - 1) inputRow = docRow;
            }
            _inputDocRow = inputRow;
            if (_inputMode) _doc.SetColor(inputRow, 1, _w + 1, PromptCol, null);

            // Park the caret at the input line (row 1 when idle). When the
            // document is fitted to the viewport there is no scroll region, so
            // this never pulls the title off the top.
            _doc.GoTo(_inputMode ? inputRow : 1, 1);
        }
    }
}
#endif
