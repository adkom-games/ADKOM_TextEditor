#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ADKOM.TextEditor
{
    // Snippet insertion + the live tab-stop session. A snippet body inserts
    // re-indented to the caret line, its $name$ placeholders become selected
    // stops the Tab key cycles through (Shift+Tab goes back), and $END$ marks
    // where the caret lands when the session ends. Stops track edits in real
    // time (typing inside a stop grows it; edits crossing a stop end the
    // session), so the whole flow is ordinary editing — one undo step for the
    // insertion, normal undo for everything typed after.
    public partial class CodeView
    {
        List<(int start, int length)> _snipStops;
        int _snipIndex = -1;
        int _snipEnd = -1;         // offset for $END$ (or end of insertion)
        bool _snipAdjusting;       // guards the session's own insertion edit

        internal bool SnippetSessionActive => _snipStops != null;

        /// <summary>Expands <paramref name="body"/> at the caret. When
        /// <paramref name="replaceFrom"/> >= 0, [replaceFrom, replaceTo) is
        /// replaced instead (the typed trigger/prefix) — still one undo step.</summary>
        internal void InsertSnippet(string body, int replaceFrom = -1, int replaceTo = -1)
        {
            EndSnippetSession();
            int at, to;
            if (replaceFrom >= 0) { at = replaceFrom; to = replaceTo; }
            else
            {
                at = Mathf.Min(cursorIndex, selectIndex);
                to = Mathf.Max(cursorIndex, selectIndex);
            }
            IndexToLineCol(at, out int line, out _);
            string lineText = _lines[line];
            int ind = 0;
            while (ind < lineText.Length && lineText[ind] == ' ') ind++;
            string indent = new string(' ', ind);

            ExpandBody(body, indent, out string text, out var stops, out int endOff);

            _snipAdjusting = true;
            try { ReplaceRangeInternal(at, to, text, at + (endOff >= 0 ? endOff : text.Length), EditKind.Paste); }
            finally { _snipAdjusting = false; }

            if (stops.Count == 0) return; // nothing to visit; caret already at END
            _snipStops = new List<(int, int)>(stops.Count);
            foreach (var (s, len) in stops) _snipStops.Add((at + s, len));
            _snipEnd = at + (endOff >= 0 ? endOff : text.Length);
            _snipIndex = -1;
            SnippetTabJump(false); // select the first stop
        }

        /// <summary>Body → literal text: continuation lines re-indented,
        /// $name$ → stop over the name text, $END$ → final caret offset.</summary>
        static void ExpandBody(string body, string indent,
            out string text, out List<(int start, int length)> stops, out int endOffset)
        {
            var withIndent = body.Replace("\n", "\n" + indent);
            var sb = new StringBuilder(withIndent.Length);
            stops = new List<(int, int)>();
            endOffset = -1;
            int i = 0;
            while (i < withIndent.Length)
            {
                char c = withIndent[i];
                if (c == '$')
                {
                    int close = withIndent.IndexOf('$', i + 1);
                    if (close > i + 1 && close - i - 1 <= 32)
                    {
                        string name = withIndent.Substring(i + 1, close - i - 1);
                        bool token = true;
                        foreach (char nc in name)
                            if (char.IsWhiteSpace(nc)) { token = false; break; }
                        if (token)
                        {
                            if (name == "END") endOffset = sb.Length;
                            else { stops.Add((sb.Length, name.Length)); sb.Append(name); }
                            i = close + 1;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            text = sb.ToString();
        }

        /// <summary>Tab within an active session: selects the next (or with
        /// shift, previous) stop; past the last stop the caret goes to $END$
        /// and the session ends. False when no session is active.</summary>
        internal bool SnippetTabJump(bool backwards)
        {
            if (_snipStops == null) return false;
            _snipIndex += backwards ? -1 : 1;
            if (_snipIndex < 0) _snipIndex = 0;
            if (_snipIndex >= _snipStops.Count)
            {
                int end = _snipEnd;
                EndSnippetSession();
                if (end >= 0)
                {
                    int len = GetValueInternal().Length;
                    cursorIndex = Mathf.Clamp(end, 0, len);
                    selectIndex = cursorIndex;
                    AfterCaretMove();
                }
                return true;
            }
            var (start, length) = _snipStops[_snipIndex];
            int docLen = GetValueInternal().Length;
            selectIndex = Mathf.Clamp(start, 0, docLen);
            cursorIndex = Mathf.Clamp(start + length, 0, docLen);
            AfterCaretMove();
            RefreshVisible();
            return true;
        }

        /// <summary>Tab with no session: if the word before the caret is a
        /// snippet trigger, expand it. False otherwise.</summary>
        internal bool TryExpandSnippetAtCaret()
        {
            if (HasSelection || HasMultiCarets || _gameMode) return false;
            string line = _lines[_caretLine];
            int ws = _caretCol;
            while (ws > 0 && IsWordCharUndo(line[ws - 1])) ws--;
            if (ws == _caretCol) return false;
            string word = line.Substring(ws, _caretCol - ws);
            if (!SnippetStore.TryGet(word, out var snip)) return false;
            int from = LineColToIndex(_caretLine, ws);
            InsertSnippet(snip.Body, from, LineColToIndex(_caretLine, _caretCol));
            return true;
        }

        internal void EndSnippetSession()
        {
            _snipStops = null;
            _snipIndex = -1;
            _snipEnd = -1;
        }

        /// <summary>Keeps the session's stops aligned across an edit: typing
        /// inside the current stop grows it, edits before a stop shift it, and
        /// anything that crosses a stop boundary ends the session (stale stops
        /// are worse than none).</summary>
        void AdjustSnippetStopsForEdit(int start, int end, string replacement)
        {
            if (_snipStops == null || _snipAdjusting) return;
            int delta = replacement.Length - (end - start);
            for (int i = 0; i < _snipStops.Count; i++)
            {
                var (s, len) = _snipStops[i];
                if (start >= s && end <= s + len)
                    _snipStops[i] = (s, len + delta);          // inside the stop
                else if (end <= s)
                    _snipStops[i] = (s + delta, len);          // before the stop
                else if (start >= s + len)
                    { /* after the stop: unaffected */ }
                else
                    { EndSnippetSession(); return; }           // crosses a boundary
            }
            if (_snipEnd >= 0)
            {
                if (end <= _snipEnd) _snipEnd += delta;
                else if (start < _snipEnd) { EndSnippetSession(); return; }
            }
        }
    }
}
#endif
