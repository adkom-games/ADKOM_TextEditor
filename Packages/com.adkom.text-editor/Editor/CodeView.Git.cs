#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // Git gutter markers: a 3px bar beside the line numbers — green for lines
    // added since HEAD, amber for modified, and a thin red strip under the
    // line a deletion sits below. Data arrives from the window's background
    // `git diff` (refreshed on tab switch, focus, and save); rendering rides
    // the gutter so it hides with it.
    public partial class CodeView
    {
        Dictionary<int, GitService.LineMark> _gitMarks;
        readonly List<VisualElement> _gitMarkPool = new List<VisualElement>();
        static readonly Color GitAdd = new Color(0.35f, 0.75f, 0.4f, 0.9f);
        static readonly Color GitMod = new Color(0.85f, 0.65f, 0.2f, 0.9f);
        static readonly Color GitDel = new Color(0.9f, 0.35f, 0.3f, 0.95f);

        internal void ApplyGitMarks(Dictionary<int, GitService.LineMark> marks)
        {
            _gitMarks = marks != null && marks.Count > 0 ? marks : null;
            RefreshVisible();
        }

        void RefreshGitMarks(int firstRow, int visible, float scrollY)
        {
            int quad = 0;
            if (_gitMarks != null && !_gameMode)
            {
                for (int i = 0; i < visible; i++)
                {
                    int row = firstRow + i;
                    if (row >= _totalRows) break;
                    RowToLineSub(row, out int line, out int sub);
                    if (sub != 0) continue; // one mark per logical line
                    if (!_gitMarks.TryGetValue(line, out var mark)) continue;
                    if (quad >= _gitMarkPool.Count)
                    {
                        var q = new VisualElement();
                        q.style.position = Position.Absolute;
                        q.pickingMode = PickingMode.Ignore;
                        _gutterCol.Add(q);
                        _gitMarkPool.Add(q);
                    }
                    var v = _gitMarkPool[quad++];
                    v.style.display = DisplayStyle.Flex;
                    v.style.left = 0;
                    v.style.width = 3;
                    if (mark == GitService.LineMark.DeletedBelow)
                    {
                        v.style.backgroundColor = GitDel;
                        v.style.top = (row + 1) * _lineHeight - scrollY - 2;
                        v.style.height = 3;
                        v.style.width = 8; // wider stub so a deletion reads distinctly
                    }
                    else
                    {
                        v.style.backgroundColor = mark == GitService.LineMark.Added ? GitAdd : GitMod;
                        v.style.top = row * _lineHeight - scrollY;
                        v.style.height = _lineHeight;
                    }
                }
            }
            for (int i = quad; i < _gitMarkPool.Count; i++)
                _gitMarkPool[i].style.display = DisplayStyle.None;
        }
    }
}
#endif
