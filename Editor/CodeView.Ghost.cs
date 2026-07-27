#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Ghost text: a gray inline suggestion (Copilot) drawn at the caret.
    // Tab accepts, Escape dismisses, and any edit or caret move clears it.
    public partial class CodeView
    {
        Label _ghost;
        string _ghostText;
        int _ghostLine = -1, _ghostCol = -1, _ghostDocVersion = -1;

        internal bool HasGhost => _ghostText != null;

        /// <summary>Shows a suggestion anchored at the CURRENT caret; it is
        /// dropped automatically if the document or caret has moved by the
        /// time this arrives (async round-trips race with typing).</summary>
        internal void ShowGhost(string text, int forLine, int forCol, int forVersion)
        {
            if (string.IsNullOrEmpty(text) ||
                forLine != _caretLine || forCol != _caretCol || forVersion != DocVersion)
            { ClearGhost(); return; }
            _ghostText = text;
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
            _ghost.text = text;
            _ghost.style.left = MeasureRange(_ghostLine, 0, _ghostCol);
            _ghost.style.top = RowOfLine(_ghostLine) * _lineHeight;
            _ghost.style.display = DisplayStyle.Flex;
        }

        internal void ClearGhost()
        {
            _ghostText = null;
            if (_ghost != null) _ghost.style.display = DisplayStyle.None;
        }

        /// <summary>Accepts the suggestion (Tab). One undo step.</summary>
        internal bool AcceptGhost()
        {
            if (_ghostText == null) return false;
            if (_ghostLine != _caretLine || _ghostCol != _caretCol || _ghostDocVersion != DocVersion)
            { ClearGhost(); return false; }
            string t = _ghostText;
            ClearGhost();
            InsertText(t, EditKind.Paste);
            return true;
        }
    }
}
#endif
