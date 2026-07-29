#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>A keyboard event delivered to addons. Set <see cref="Handled"/>
    /// to true to consume the key: the editor will not process it (no typing,
    /// no command dispatch). Events are not raised while the status-bar prompt
    /// is open, so a game's own <see cref="AteApi.Prompt"/> keeps working.</summary>
    public sealed class AteKeyEvent
    {
        public KeyCode Key { get; }
        /// <summary>The typed character for character events ('\0' on pure
        /// keycode events — Unity sends each key press as both).</summary>
        public char Character { get; }
        public bool Ctrl { get; }
        public bool Shift { get; }
        public bool Alt { get; }
        /// <summary>Set true to consume the event.</summary>
        public bool Handled { get; set; }

        internal AteKeyEvent(KeyCode key, char character, bool ctrl, bool shift, bool alt)
        { Key = key; Character = character; Ctrl = ctrl; Shift = shift; Alt = alt; }
    }

    /// <summary>A mouse event over the code area in TEXT coordinates (1-based
    /// line/column, like the rest of the API). For button-down events, set
    /// <see cref="Handled"/> to consume the click (no caret placement or
    /// selection). Move and button-up events are notifications only.</summary>
    public sealed class AteMouseEvent
    {
        /// <summary>1-based line under the pointer.</summary>
        public int Line { get; }
        /// <summary>1-based column under the pointer.</summary>
        public int Column { get; }
        /// <summary>0 = left, 1 = right, 2 = middle. -1 for move events.</summary>
        public int Button { get; }
        public bool Handled { get; set; }

        internal AteMouseEvent(int line, int column, int button)
        { Line = line; Column = column; Button = button; }
    }

    /// <summary>How <see cref="AteDocument.WriteAt"/> places text (API 1.1).
    /// Games use Overwrite (fixed grid); Insert shifts the rest of the line
    /// right, for text-tool addons. Note for games: keep drawn characters to
    /// ASCII (or glyphs your monospace font really has) — a glyph missing
    /// from the font renders at fallback width and visually misaligns the
    /// column grid even though the buffer is perfectly rectangular. Coloring
    /// spaces via SetColor's background is the alignment-proof way to draw.</summary>
    public enum AteWriteMode { Overwrite, Insert }

    /// <summary>Handle to a running game tick started with
    /// <see cref="AteApi.StartTick"/>. Call <see cref="Stop"/> from your
    /// addon's OnUnload (and when the game quits).</summary>
    public sealed class AteTick
    {
        internal IVisualElementScheduledItem Item;
        internal Action Callback;
        public bool IsRunning { get; internal set; }

        public void Stop()
        {
            IsRunning = false;
            Item?.Pause();
            AteApi.ForgetTick(this);
        }
    }

    // Game-support surface added in API 1.1 (keyboard events + polling, mouse
    // in text coordinates, status-bar prompt, and the ≤30 Hz game tick).
    public static partial class AteApi
    {
        // ---- Keyboard (1.1) ----

        /// <summary>Every key-down over the ATE window, before the editor
        /// handles it. Set e.Handled to consume. Not raised while the
        /// status-bar prompt is open.</summary>
        public static event Action<AteKeyEvent> keyDown;
        /// <summary>Every key-up over the ATE window. Consumable like
        /// <see cref="keyDown"/>.</summary>
        public static event Action<AteKeyEvent> keyUp;

        /// <summary>Polled key state for held-key movement. Maintained from
        /// key-down/up over the ATE window; all keys reset to up when the
        /// window loses focus (see IAteAddonLifecycle.OnFocusLost).</summary>
        public static bool IsKeyDown(KeyCode key) => _keysDown.Contains(key);

        // ---- Mouse (1.1) ----

        /// <summary>Pointer moved to a new (line, column) over the code area.</summary>
        public static event Action<AteMouseEvent> mouseMoved;
        /// <summary>Mouse button pressed over the code area. Set e.Handled to
        /// consume (no caret placement / selection / context menu).</summary>
        public static event Action<AteMouseEvent> mouseButtonDown;
        /// <summary>Mouse button released over the code area. Notification only.</summary>
        public static event Action<AteMouseEvent> mouseButtonUp;

        // ---- Status-bar prompt (1.1) ----

        /// <summary>Shows the status-bar input prompt (the Goto Line
        /// mini-buffer) with a custom prompt. Enter → onCommit(text),
        /// Escape/focus loss → onCancel. One prompt at a time; opening a new
        /// one cancels the previous.</summary>
        public static void Prompt(string prompt, Action<string> onCommit,
            Action onCancel = null, bool digitsOnly = false, string initialValue = "")
        {
            var w = Window(create: true);
            w?.ApiPrompt(prompt ?? string.Empty, digitsOnly, onCommit, onCancel, initialValue);
        }

        // ---- Game tick (1.1) ----

        /// <summary>Starts a repeating tick on the ATE window, clamped to
        /// [1, 30] Hz. Ticks pause while the window is unfocused and resume on
        /// focus. Exceptions in the callback are logged, not fatal. Keep the
        /// returned handle and Stop() it in OnUnload/when the game ends.</summary>
        public static AteTick StartTick(int hz, Action tick)
        {
            if (tick == null) throw new ArgumentNullException(nameof(tick));
            var w = Window(create: true);
            if (w == null) return new AteTick();
            long ms = 1000 / Mathf.Clamp(hz, 1, 30);
            var handle = new AteTick { Callback = tick, IsRunning = true };
            handle.Item = w.rootVisualElement.schedule.Execute(() =>
            {
                if (!handle.IsRunning) return;
                try { handle.Callback(); }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Game tick threw: " + ex.Message);
                }
            }).Every(ms);
            _ticks.Add(handle);
            return handle;
        }

        // ---- Internal plumbing (not part of the contract) ----

        static readonly HashSet<KeyCode> _keysDown = new HashSet<KeyCode>();
        static readonly List<AteTick> _ticks = new List<AteTick>();
        static int _lastMouseLine = -1, _lastMouseCol = -1;

        internal static void ForgetTick(AteTick t) => _ticks.Remove(t);

        /// <summary>Stops every running tick — called when addons unload so a
        /// stale assembly's delegates never keep firing.</summary>
        internal static void StopAllTicks()
        {
            for (int i = _ticks.Count - 1; i >= 0; i--)
            {
                var t = _ticks[i];
                t.IsRunning = false;
                t.Item?.Pause();
            }
            _ticks.Clear();
        }

        internal static void DropInputSubscribers()
        {
            keyDown = null; keyUp = null;
            mouseMoved = null; mouseButtonDown = null; mouseButtonUp = null;
        }

        internal static bool HasMouseSubscribers =>
            mouseMoved != null || mouseButtonDown != null || mouseButtonUp != null;

        internal static void NoteKeyDown(KeyCode k) { if (k != KeyCode.None) _keysDown.Add(k); }
        internal static void NoteKeyUp(KeyCode k) { if (k != KeyCode.None) _keysDown.Remove(k); }

        /// <summary>Window focus changed: reset polled key state (a key-up
        /// missed while unfocused would stick forever), pause/resume ticks,
        /// then tell lifecycle addons.</summary>
        internal static void NotifyWindowFocus(bool focused)
        {
            if (!focused) _keysDown.Clear();
            foreach (var t in _ticks)
                if (t.IsRunning) { if (focused) t.Item?.Resume(); else t.Item?.Pause(); }
            AteAddonManager.NotifyFocus(focused);
        }

        static bool RaiseKey(Action<AteKeyEvent> evt, KeyDownEvent d, KeyUpEvent u)
        {
            if (evt == null || _raising) return false;
            var e = d != null
                ? new AteKeyEvent(d.keyCode, d.character, d.ctrlKey || d.commandKey, d.shiftKey, d.altKey)
                : new AteKeyEvent(u.keyCode, u.character, u.ctrlKey || u.commandKey, u.shiftKey, u.altKey);
            _raising = true;
            try { evt(e); }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] A key event handler threw: " + ex.Message);
            }
            finally { _raising = false; }
            return e.Handled;
        }

        internal static bool RaiseKeyDown(KeyDownEvent e) => RaiseKey(keyDown, e, null);
        internal static bool RaiseKeyUp(KeyUpEvent e) => RaiseKey(keyUp, null, e);

        static bool RaiseMouse(Action<AteMouseEvent> evt, int line1, int col1, int button)
        {
            if (evt == null || _raising) return false;
            var e = new AteMouseEvent(line1, col1, button);
            _raising = true;
            try { evt(e); }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] A mouse event handler threw: " + ex.Message);
            }
            finally { _raising = false; }
            return e.Handled;
        }

        internal static void RaiseMouseMove(int line1, int col1)
        {
            if (line1 == _lastMouseLine && col1 == _lastMouseCol) return;
            _lastMouseLine = line1; _lastMouseCol = col1;
            RaiseMouse(mouseMoved, line1, col1, -1);
        }

        internal static bool RaiseMouseButtonDown(int line1, int col1, int button)
            => RaiseMouse(mouseButtonDown, line1, col1, button);

        internal static void RaiseMouseButtonUp(int line1, int col1, int button)
            => RaiseMouse(mouseButtonUp, line1, col1, button);
    }

    // Game-support members of the document handle (API 1.1).
    public sealed partial class AteDocument
    {
        /// <summary>Game mode: word wrap and syntax highlighting are off,
        /// programmatic writes bypass undo history (the document's undo stack
        /// is cleared on entry), and games own the input they consume. Turn
        /// off to return the document to normal editing.</summary>
        public bool GameMode
        {
            get { EnsureValid(); return _doc.GameMode; }
            set { EnsureValid(); _window.ApiSetGameMode(_doc, value); }
        }

        /// <summary>The visible size of the game screen in whole characters
        /// (this document's viewport). Sized to the window, so a full-screen
        /// game can fit itself exactly. False until the view has laid out or
        /// when the document is not the active tab.</summary>
        public bool TryGetViewport(out int rows, out int cols)
        {
            EnsureValid();
            return _window.ApiTryGetViewport(_doc, out rows, out cols);
        }

        /// <summary>The caret position (1-based) when this document is the
        /// active tab; false when it is not active.</summary>
        public bool TryGetCursor(out int line, out int column)
        {
            EnsureValid();
            return _window.ApiTryGetCursor(_doc, out line, out column);
        }

        /// <summary>Number of lines in the document (≥ 1).</summary>
        public int LineCount
        {
            get
            {
                EnsureValid();
                string v = _doc.Content ?? string.Empty;
                int n = 1;
                foreach (char c in v) if (c == '\n') n++;
                return n;
            }
        }

        /// <summary>The text of a 1-based line (without the newline).</summary>
        public string GetLine(int line)
        {
            EnsureValid();
            GetLineSpan(line, out string v, out int start, out int end);
            return v.Substring(start, end - start);
        }

        /// <summary>Reads up to <paramref name="length"/> characters at a
        /// 1-based (line, column), clamped to the end of that line.</summary>
        public string ReadAt(int line, int column, int length)
        {
            EnsureValid();
            if (length <= 0) return string.Empty;
            GetLineSpan(line, out string v, out int start, out int end);
            int from = start + Mathf.Clamp(column - 1, 0, end - start);
            int to = Mathf.Min(from + length, end);
            return to > from ? v.Substring(from, to - from) : string.Empty;
        }

        /// <summary>Writes text at a 1-based (line, column) — the game "draw
        /// text" call. Overwrite (default) replaces characters in place, the
        /// fixed-grid behavior games need; Insert shifts the rest of the line
        /// right. Either way the line is padded with spaces when shorter than
        /// <paramref name="column"/>, and text past the end of the line
        /// lengthens it. Text must not contain newlines (draw row by row).
        /// In game mode writes bypass undo and keep the caret put.</summary>
        public void WriteAt(int line, int column, string text, AteWriteMode mode = AteWriteMode.Overwrite)
        {
            EnsureValid();
            if (string.IsNullOrEmpty(text)) return;
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
                throw new ArgumentException("WriteAt text must be a single line — write row by row.", nameof(text));
            GetLineSpan(line, out string v, out int start, out int end);
            string cur = v.Substring(start, end - start);
            int col0 = Mathf.Max(0, column - 1);
            var sb = new System.Text.StringBuilder(Mathf.Max(cur.Length, col0 + text.Length));
            sb.Append(cur, 0, Mathf.Min(col0, cur.Length));
            if (col0 > cur.Length) sb.Append(' ', col0 - cur.Length);
            sb.Append(text);
            int tail = mode == AteWriteMode.Overwrite ? col0 + text.Length : col0;
            if (tail < cur.Length) sb.Append(cur, tail, cur.Length - tail);
            _window.ApiWriteLine(_doc, start, end, sb.ToString());
        }

        /// <summary>Sets the tab title of this document (display only — the
        /// file path, if any, is untouched). Games name their tab after the
        /// game. Null or empty restores the default name.</summary>
        public void SetTitle(string title)
        {
            EnsureValid();
            _window.ApiSetTitle(_doc, title);
        }

        // ---- Per-document font (1.1) ----

        /// <summary>Sets this document's font: an OS font family name (e.g.
        /// "Consolas") and an optional point size (clamped to [8, 40]; 0
        /// keeps the editor's current size). A missing/unloadable font falls
        /// back to the editor default. The override is per document, never
        /// shown in menus or Settings, and runtime-only (a domain reload
        /// drops it). User zoom gestures still work — they adjust this
        /// document's override, not the global font size. Games: monospace
        /// fonts keep the character grid aligned.</summary>
        public void SetFont(string fontName, int size = 0)
        {
            EnsureValid();
            _window.ApiSetFont(_doc, fontName, size);
        }

        /// <summary>Removes the font override — back to the global font.</summary>
        public void ClearFont()
        {
            EnsureValid();
            _window.ApiSetFont(_doc, null, 0);
        }

        // ---- Color overlay (1.1) ----

        /// <summary>Colors a 1-based column range [colStart, colEnd) on a line
        /// with an optional foreground and background. Colors are a RENDER
        /// overlay — never part of the document text — and are positional:
        /// they do not move with edits, so games repaint text and color
        /// together. Passing null for both clears the range.</summary>
        public void SetColor(int line, int colStart, int colEnd, Color? foreground, Color? background = null)
        {
            EnsureValid();
            _window.ApiSetColor(_doc, line - 1, colStart - 1, colEnd - 1, foreground, background);
        }

        /// <summary>Clears the color overlay of one 1-based line.</summary>
        public void ClearColors(int line)
        {
            EnsureValid();
            _window.ApiClearColors(_doc, line - 1);
        }

        /// <summary>Clears the whole color overlay.</summary>
        public void ClearColors()
        {
            EnsureValid();
            _window.ApiClearColors(_doc, -1);
        }

        /// <summary>Shows a pinned status line across the top of the game screen
        /// that does NOT scroll with the document — for a growing transcript
        /// (e.g. a text adventure) whose header must stay put. <paramref
        /// name="left"/> is left-aligned (location), <paramref name="right"/> is
        /// right-aligned (score/moves). The reserved row pushes the text down so
        /// nothing hides behind it. Only takes effect while this document is the
        /// active tab and in game mode; passing both empty hides the bar.</summary>
        public void SetStatusBar(string left, string right, Color foreground, Color background)
        {
            EnsureValid();
            _window.ApiSetStatusBar(_doc, left, right, foreground, background);
        }

        /// <summary>1-based line → its [start, end) span in Content.</summary>
        void GetLineSpan(int line, out string v, out int start, out int end)
        {
            v = _doc.Content ?? string.Empty;
            int cur = 1, i = 0;
            start = 0;
            while (cur < line)
            {
                int nl = v.IndexOf('\n', i);
                if (nl < 0) throw new ArgumentOutOfRangeException(nameof(line),
                    "Line " + line + " is past the end of the document.");
                i = nl + 1;
                cur++;
            }
            start = i;
            int e2 = v.IndexOf('\n', start);
            end = e2 < 0 ? v.Length : e2;
        }
    }
}
#endif
