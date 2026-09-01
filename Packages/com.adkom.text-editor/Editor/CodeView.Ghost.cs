#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Ghost text: gray inline Copilot suggestions drawn at the caret, with a
    // small "◂ 1/3 ▸" cycler when there are alternatives. Each suggestion
    // REPLACES a document range (Copilot rewrites text around the caret);
    // the ghost shows only the part beyond the caret. Tab OR Enter accepts,
    // Alt+[ / Alt+] (or the buttons) cycle, Escape dismisses, and any edit
    // or caret move clears.
    public partial class CodeView
    {
        internal struct GhostItem
        {
            public string Text;
            public int StartIdx, EndIdx;
        }

        Label _ghost;      // first line, anchored at the caret x
        Label _ghostBlock; // lines 2+, anchored at column 0 of the next row
        VisualElement _ghostBar;
        Label _ghostCount;
        List<GhostItem> _ghostItems;
        int _ghostIndex;
        internal int _ghostExtraRows; // rows the ghost adds past the doc end
        int _ghostLine = -1, _ghostCol = -1, _ghostDocVersion = -1;

        internal bool HasGhost => _ghostItems != null && _ghostItems.Count > 0;

        internal void ShowGhost(List<GhostItem> items, int forLine, int forCol, int forVersion)
        {
            if (items == null || items.Count == 0 ||
                forLine != _caretLine || forCol != _caretCol || forVersion != DocVersion)
            { ClearGhost(); return; }
            _ghostItems = items;
            _ghostIndex = 0;
            _ghostLine = forLine;
            _ghostCol = forCol;
            _ghostDocVersion = forVersion;
            // Copilot wins the screen: the word-autocomplete popup fights the
            // ghost for the same space, so it yields when a suggestion lands.
            HideCompletion();
            PaintGhost();
        }

        internal void CycleGhost(int dir)
        {
            if (!HasGhost || _ghostItems.Count < 2) return;
            _ghostIndex = (_ghostIndex + dir + _ghostItems.Count) % _ghostItems.Count;
            PaintGhost();
        }

        void PaintGhost()
        {
            if (_ghostDocVersion != DocVersion) { ClearGhost(); return; }
            var it = _ghostItems[_ghostIndex];
            string doc = GetValueInternal();
            int caretIdx = LineColToIndex(_ghostLine, _ghostCol);
            int s = Mathf.Clamp(it.StartIdx, 0, doc.Length);
            int e = Mathf.Clamp(it.EndIdx, s, doc.Length);
            if (caretIdx < s || caretIdx > e) { ClearGhost(); return; }
            string typedPrefix = doc.Substring(s, caretIdx - s);
            string display = it.Text.StartsWith(typedPrefix)
                ? it.Text.Substring(typedPrefix.Length) : it.Text;
            if (display.Length == 0) { ClearGhost(); return; }

            if (_ghost == null)
            {
                _ghost = new Label();
                _ghost.AddToClassList("code-line");
                _ghost.style.position = Position.Absolute;
                _ghost.pickingMode = PickingMode.Ignore;
                _ghost.style.whiteSpace = WhiteSpace.Pre;
                _content.Add(_ghost);
                _ghostBlock = new Label();
                _ghostBlock.AddToClassList("code-line");
                _ghostBlock.style.position = Position.Absolute;
                _ghostBlock.pickingMode = PickingMode.Ignore;
                _ghostBlock.style.whiteSpace = WhiteSpace.Pre;
                _content.Add(_ghostBlock);

                _ghostBar = new VisualElement();
                _ghostBar.style.position = Position.Absolute;
                _ghostBar.style.flexDirection = FlexDirection.Row;
                _ghostBar.style.backgroundColor = new Color(0.14f, 0.14f, 0.15f, 0.95f);
                var bc = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                _ghostBar.style.borderLeftWidth = _ghostBar.style.borderRightWidth = 1;
                _ghostBar.style.borderTopWidth = _ghostBar.style.borderBottomWidth = 1;
                _ghostBar.style.borderLeftColor = _ghostBar.style.borderRightColor = bc;
                _ghostBar.style.borderTopColor = _ghostBar.style.borderBottomColor = bc;
                var prev = new Button(() => CycleGhost(-1)) { text = "◂" };
                var next = new Button(() => CycleGhost(1)) { text = "▸" };
                foreach (var b in new[] { prev, next })
                {
                    b.style.marginLeft = b.style.marginRight = 0;
                    b.style.paddingLeft = b.style.paddingRight = 3;
                    b.style.backgroundColor = Color.clear;
                    b.style.borderLeftWidth = b.style.borderRightWidth = 0;
                    b.style.borderTopWidth = b.style.borderBottomWidth = 0;
                    b.style.fontSize = 9;
                }
                _ghostCount = new Label();
                _ghostCount.style.fontSize = 9;
                _ghostCount.style.unityTextAlign = TextAnchor.MiddleCenter;
                _ghostCount.style.paddingLeft = _ghostCount.style.paddingRight = 2;
                _ghostCount.tooltip = L10n.Tr("Tab or Enter accepts; Alt+[ / Alt+] cycle; Escape dismisses.");
                _ghostBar.Add(prev);
                _ghostBar.Add(_ghostCount);
                _ghostBar.Add(next);
                _content.Add(_ghostBar);
            }
            // _textColor can be an unset (black) palette value for some
            // documents — black at 45% alpha on a dark theme is INVISIBLE
            // (field report 2026-07-27). Contrast-check against the actual
            // background and fall back to a readable gray.
            var c = _textColor;
            var bg = resolvedStyle.backgroundColor;
            bool darkBg = bg.grayscale < 0.5f;
            if (darkBg == (c.grayscale < 0.5f)) // no contrast with background
                c = darkBg ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.15f, 0.15f, 0.15f);
            var ghostColor = new Color(c.r, c.g, c.b, 0.55f);
            _ghost.style.color = ghostColor;
            _ghostBlock.style.color = ghostColor;
            // The ghost lives in the VISUAL row layout, not the logical line:
            // with word wrap on, the caret's line may span several rows, so
            // anchor at the caret's row (not the line's first row) and measure
            // x from that row's start column (not column 0) — exactly as
            // RefreshCaret does.
            int sub = SubRowOfCol(_ghostLine, _ghostCol);
            RowBounds(_ghostLine, sub, out int rowStart, out _);
            float x = MeasureRange(_ghostLine, rowStart, _ghostCol);
            float y = (RowOfLine(_ghostLine) + sub) * _lineHeight;

            // First row rides the caret; every later row starts at COLUMN 0
            // (a single caret-anchored label shifted every continuation line
            // right by the caret x — field report: 'indented strangely').
            // Word wrap: each suggestion line breaks at the same width, with
            // the same greedy rule, as the document itself — the first row
            // starting from the caret x — so a long suggestion folds onto
            // the following rows instead of running off the right edge.
            var rows = new List<string>();
            string[] ghostLines = display.Split('\n');
            if (_wordWrap)
            {
                float width = _wrapWidth > 0 ? _wrapWidth : Mathf.Max(40, AvailableWrapWidth());
                for (int i = 0; i < ghostLines.Length; i++)
                    AppendWrappedRows(rows, ghostLines[i], width, i == 0 ? x : 0);
            }
            else rows.AddRange(ghostLines);
            string block = rows.Count > 1 ? string.Join("\n", rows.GetRange(1, rows.Count - 1)) : null;
            _ghost.text = rows[0];
            _ghostBlock.text = block ?? string.Empty;
            _ghostBlock.style.display = block != null ? DisplayStyle.Flex : DisplayStyle.None;
            // Multi-row suggestions extend BELOW the last document row; the
            // content canvas must grow or those rows are clipped invisible
            // (field report 2026-07-27: arrows visible, no text).
            _ghostExtraRows = rows.Count - 1;
            if (_ghostExtraRows > 0)
                _content.style.height = (_totalRows + _ghostExtraRows) * _lineHeight;
            _ghost.style.left = x;
            _ghost.style.top = y;
            _ghost.style.display = DisplayStyle.Flex;
            _ghostBlock.style.left = 0;
            _ghostBlock.style.top = y + _lineHeight;

            _ghostCount.text = (_ghostIndex + 1) + "/" + _ghostItems.Count;
            _ghostBar.style.left = x;
            _ghostBar.style.top = Mathf.Max(0, y - _lineHeight - 4);
            _ghostBar.style.display = DisplayStyle.Flex;
        }

        /// <summary>Splits one suggestion line into visual rows using the
        /// document's own wrap rule; the first row may begin mid-row (at
        /// <paramref name="startX"/>, the caret's x).</summary>
        void AppendWrappedRows(List<string> rows, string text, float width, float startX)
        {
            var br = ComputeBreaks(text, width, startX);
            if (br == null) { rows.Add(text); return; }
            int from = 0;
            foreach (int b in br) { rows.Add(text.Substring(from, b - from)); from = b; }
            rows.Add(text.Substring(from));
        }

        /// <summary>Re-lays the ghost out after the wrap geometry changed
        /// (viewport resize, word wrap toggled) — its rows and anchor depend
        /// on the current break columns.</summary>
        void RelayoutGhost()
        {
            if (HasGhost) PaintGhost();
        }

        internal void ClearGhost()
        {
            _ghostItems = null;
            _ghostExtraRows = 0;
            if (_ghost != null) _ghost.style.display = DisplayStyle.None;
            if (_ghostBlock != null) _ghostBlock.style.display = DisplayStyle.None;
            if (_ghostBar != null) _ghostBar.style.display = DisplayStyle.None;
        }

        /// <summary>Accepts the current suggestion (Tab or Enter): REPLACES
        /// its whole range with its text — one undo step.</summary>
        internal bool AcceptGhost()
        {
            if (!HasGhost) return false;
            if (_ghostLine != _caretLine || _ghostCol != _caretCol || _ghostDocVersion != DocVersion)
            { ClearGhost(); return false; }
            var it = _ghostItems[_ghostIndex];
            ClearGhost();
            ReplaceRangeInternal(it.StartIdx, it.EndIdx, it.Text,
                it.StartIdx + it.Text.Length, EditKind.Paste);
            return true;
        }
    }
}
#endif
