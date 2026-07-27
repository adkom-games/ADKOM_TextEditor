#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Ghost text: a gray inline suggestion (Copilot) drawn at the caret.
    // The suggestion REPLACES a document range (Copilot rewrites text around
    // the caret, e.g. a "()" the user already typed); the ghost shows only
    // the part beyond the caret. Tab accepts, Escape dismisses, and any edit
    // or caret move clears it.
    public partial class CodeView
    {
        Label _ghost;
        string _ghostFull;                 // full replacement text
        int _ghostStartIdx, _ghostEndIdx;  // document range being replaced
        int _ghostLine = -1, _ghostCol = -1, _ghostDocVersion = -1;

        internal bool HasGhost => _ghostFull != null;

        /// <summary>Shows a suggestion replacing [startIdx, endIdx); it is
        /// dropped if the document or caret moved during the round-trip.</summary>
        internal void ShowGhost(string text, int startIdx, int endIdx,
            int forLine, int forCol, int forVersion)
        {
            if (string.IsNullOrEmpty(text) ||
                forLine != _caretLine || forCol != _caretCol || forVersion != DocVersion)
            { ClearGhost(); return; }
            string doc = GetValueInternal();
            int caretIdx = LineColToIndex(_caretLine, _caretCol);
            startIdx = Mathf.Clamp(startIdx, 0, doc.Length);
            endIdx = Mathf.Clamp(endIdx, startIdx, doc.Length);
            if (caretIdx < startIdx || caretIdx > endIdx) { ClearGhost(); return; }
            // Display only what the caret does not already show: the part of
            // the replacement that follows the already-typed prefix.
            string typedPrefix = doc.Substring(startIdx, caretIdx - startIdx);
            string display = text.StartsWith(typedPrefix) ? text.Substring(typedPrefix.Length) : text;
            if (display.Length == 0) { ClearGhost(); return; }
            _ghostFull = text;
            _ghostStartIdx = startIdx;
            _ghostEndIdx = endIdx;
            _ghostLine = forLine;
            _ghostCol = forCol;
            _ghostDocVersion = forVersion;
            if (_ghost == null)
            {
                _ghost = new Label();
                _ghost.AddToClassList("code-line");
                _ghost.style.position = Position.Absolute;
                _ghost.pickingMode = PickingMode.Ignore;
                _ghost.style.whiteSpace = WhiteSpace.Pre;
                _content.Add(_ghost);
            }
            var c = _textColor;
            _ghost.style.color = new Color(c.r, c.g, c.b, 0.45f);
            _ghost.text = display;
            _ghost.style.left = MeasureRange(_ghostLine, 0, _ghostCol);
            _ghost.style.top = RowOfLine(_ghostLine) * _lineHeight;
            _ghost.style.display = DisplayStyle.Flex;
        }

        internal void ClearGhost()
        {
            _ghostFull = null;
            if (_ghost != null) _ghost.style.display = DisplayStyle.None;
        }

        /// <summary>Accepts the suggestion (Tab): REPLACES the suggestion's
        /// whole range with its text — one undo step.</summary>
        internal bool AcceptGhost()
        {
            if (_ghostFull == null) return false;
            if (_ghostLine != _caretLine || _ghostCol != _caretCol || _ghostDocVersion != DocVersion)
            { ClearGhost(); return false; }
            string t = _ghostFull;
            int s = _ghostStartIdx, e = _ghostEndIdx;
            ClearGhost();
            ReplaceRangeInternal(s, e, t, s + t.Length, EditKind.Paste);
            return true;
        }
    }
}
#endif
