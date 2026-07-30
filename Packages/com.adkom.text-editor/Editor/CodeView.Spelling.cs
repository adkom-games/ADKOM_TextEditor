#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Optional spell checking (off by default). In code documents only
    // comments and strings are checked (via the classifier's spans); markdown
    // and plain text check everything. Words are letters with internal
    // apostrophes, 4+ chars; camelCase splits into parts, ALL-CAPS acronyms
    // and path-/decorator-prefixed tokens are skipped. Unknown words get a
    // soft blue underline; the context menu offers add-to-dictionary.
    public partial class CodeView
    {
        internal bool spellEnabled
        {
            get => _spellOn;
            set
            {
                if (_spellOn == value) return;
                _spellOn = value;
                if (value) ScheduleSpell();
                else { _spellLines = null; RefreshVisible(); }
            }
        }

        bool _spellOn;
        Dictionary<int, List<(int start, int len)>> _spellLines;
        readonly List<VisualElement> _spellPool = new List<VisualElement>();
        IVisualElementScheduledItem _spellPending;
        static readonly Color SpellCol = new Color(0.35f, 0.6f, 0.95f, 0.75f);

        void ScheduleSpell()
        {
            if (!_spellOn || _gameMode) return;
            _spellPending ??= schedule.Execute(RunSpellPass);
            _spellPending.ExecuteLater(600);
        }

        void RunSpellPass()
        {
            if (!_spellOn || _gameMode) { _spellLines = null; RefreshVisible(); return; }
            if (!SpellChecker.Loaded)
            {
                SpellChecker.EnsureLoading();
                _spellPending.ExecuteLater(1000); // try again once it has loaded
                return;
            }
            var result = new Dictionary<int, List<(int, int)>>();
            bool wholeText = _classifier == null || _classifier is MarkdownClassifier;
            int lineCap = Mathf.Min(_lines.Count, 20000);
            for (int li = 0; li < lineCap; li++)
            {
                string line = _lines[li];
                if (line.Length == 0) continue;
                if (wholeText)
                    CheckSegment(line, li, 0, line.Length, result);
                else
                {
                    var spans = _lineSpans != null && li < _lineSpans.Length ? _lineSpans[li] : null;
                    if (spans == null) continue;
                    foreach (var s in spans)
                        if (s.Class == TokenClass.Comment || s.Class == TokenClass.String)
                            CheckSegment(line, li, s.Start, Mathf.Min(s.Start + s.Length, line.Length), result);
                }
            }
            _spellLines = result.Count > 0 ? result : null;
            RefreshVisible();
        }

        static void CheckSegment(string line, int li, int from, int to,
            Dictionary<int, List<(int, int)>> into)
        {
            int i = from;
            while (i < to)
            {
                char c = line[i];
                if (!char.IsLetter(c)) { i++; continue; }
                int s = i;
                while (i < to && (char.IsLetter(line[i]) ||
                       (line[i] == '\'' && i + 1 < to && char.IsLetter(line[i + 1]) && i > s)))
                    i++;
                int len = i - s;
                if (len < 4) continue;
                // Paths, escapes, decorators, domains: skip prefixed tokens.
                if (s > 0 && (line[s - 1] == '/' || line[s - 1] == '\\' ||
                              line[s - 1] == '.' || line[s - 1] == '@' ||
                              line[s - 1] == '#' || line[s - 1] == '&'))
                    continue;
                // A digit glued to the token (hex, ids): skip.
                if (i < to && char.IsDigit(line[i])) continue;
                string token = line.Substring(s, len);
                if (SpellChecker.IsKnown(token)) continue;
                // camelCase / PascalCase: judge each hump on its own.
                bool allUpper = true, anyInnerUpper = false;
                for (int k = 0; k < token.Length; k++)
                {
                    if (char.IsLower(token[k])) allUpper = false;
                    if (k > 0 && char.IsUpper(token[k])) anyInnerUpper = true;
                }
                if (allUpper) continue; // acronym
                if (anyInnerUpper)
                {
                    int ps = 0;
                    for (int k = 1; k <= token.Length; k++)
                    {
                        if (k == token.Length || char.IsUpper(token[k]))
                        {
                            int plen = k - ps;
                            if (plen >= 4 && !SpellChecker.IsKnown(token.Substring(ps, plen)))
                                AddFlag(into, li, s + ps, plen);
                            ps = k;
                        }
                    }
                    continue;
                }
                AddFlag(into, li, s, len);
            }
        }

        static void AddFlag(Dictionary<int, List<(int, int)>> into, int line, int start, int len)
        {
            if (!into.TryGetValue(line, out var l)) into[line] = l = new List<(int, int)>(2);
            l.Add((start, len));
        }

        void RefreshSpelling(int firstRow, int visible)
        {
            int quad = 0;
            if (_spellLines != null && _spellOn)
            {
                for (int i = 0; i < visible; i++)
                {
                    int row = firstRow + i;
                    if (row >= _totalRows) break;
                    RowToLineSub(row, out int line, out int sub);
                    if (!_spellLines.TryGetValue(line, out var flags)) continue;
                    RowBounds(line, sub, out int rs, out int re);
                    int lineLen = _lines[line].Length;
                    foreach (var (fs, flen) in flags)
                    {
                        int cs = Mathf.Max(Mathf.Min(fs, lineLen), rs);
                        int ce = Mathf.Min(Mathf.Min(fs + flen, lineLen), re);
                        if (ce <= cs) continue;
                        if (quad >= _spellPool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Add(q);
                            _spellPool.Add(q);
                        }
                        var v = _spellPool[quad++];
                        v.style.display = DisplayStyle.Flex;
                        v.style.backgroundColor = SpellCol;
                        float x0 = MeasureRange(line, rs, cs);
                        v.style.left = x0;
                        v.style.top = (row + 1) * _lineHeight - 2;
                        v.style.width = Mathf.Max(4, MeasureRange(line, rs, ce) - x0);
                        v.style.height = 1.5f;
                    }
                }
            }
            for (int i = quad; i < _spellPool.Count; i++)
                _spellPool[i].style.display = DisplayStyle.None;
        }

        /// <summary>The flagged word covering (line, col), or null — for the
        /// context menu's add-to-dictionary items.</summary>
        internal string MisspelledWordAt(int line, int col)
        {
            if (_spellLines == null || !_spellLines.TryGetValue(line, out var flags)) return null;
            foreach (var (s, len) in flags)
                if (col >= s && col <= s + len && s + len <= _lines[line].Length)
                    return _lines[line].Substring(s, len);
            return null;
        }

        /// <summary>Re-runs the pass now (after a dictionary addition).</summary>
        internal void RespellNow() { if (_spellOn) RunSpellPass(); }
    }
}
#endif
