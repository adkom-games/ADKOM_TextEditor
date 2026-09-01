// =============================================================================
//  ADKOM Text Editor — CodeView's co-editing surface (contract v1.0 §6.1, §7).
//
//  Two jobs, both of which have to avoid disturbing the local user:
//
//  1. APPLYING a remote edit. The ordinary edit path exists to serve the person
//     at the keyboard: it collapses multi-carets, moves the caret to the edit,
//     scrolls it into view and pushes an undo entry. Every one of those is
//     wrong for someone else's keystroke, so remote edits get their own path
//     that splices the text and then REBASES the local caret, selection, extra
//     carets and undo stack past the change instead of resetting them.
//
//  2. DRAWING remote carets — a coloured bar, the peer's name beside it, and
//     their selection in the same colour (contract §7 makes all three
//     mandatory). Modelled directly on RefreshExtraCarets: same pooling, same
//     wrap-aware row maths, one pool per decoration.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    public partial class CodeView
    {
        /// <summary>Raised for every local text change as (start, removedCount,
        /// insertedText) — the shape the co-editing wire uses. NOT raised while a
        /// remote edit is being applied, which is what keeps the loop closed.</summary>
        internal event System.Action<int, int, string> onTextEdit;

        /// <summary>Set while a remote edit is being spliced in. Capture drops
        /// everything observed while it is set.</summary>
        internal bool applyingRemote;

        /// <summary>Raised when the caret or selection moves, so the co-editing
        /// layer can publish presence. Throttling is ALS's job.</summary>
        internal event System.Action<int, int> onCaretMoved;

        internal sealed class RemoteCaretState
        {
            public string UserId;
            public string Name;
            public Color Color;
            public int Caret;
            public int Anchor;
        }

        readonly List<RemoteCaretState> _remote = new List<RemoteCaretState>();
        readonly List<VisualElement> _remoteCaretPool = new List<VisualElement>();
        readonly List<VisualElement> _remoteSelPool = new List<VisualElement>();
        readonly List<Label> _remoteLabelPool = new List<Label>();

        internal void RaiseTextEdit(int start, int removed, string inserted)
        {
            if (!applyingRemote)
                onTextEdit?.Invoke(start, removed, inserted);
        }

        internal void RaiseCaretMoved()
        {
            if (!applyingRemote)
                onCaretMoved?.Invoke(cursorIndex, selectIndex);
        }

        // ------------------------------------------------------- applying remote

        /// <summary>Splice a peer's edit into the buffer without disturbing this
        /// user. Deliberately bypasses ReplaceRangeInternal: no undo entry (you
        /// cannot undo someone else's work, contract §8), no caret takeover, no
        /// scroll, no multi-caret collapse.</summary>
        internal void ApplyRemoteEdit(int at, int del, string ins)
        {
            var v = GetValueInternal();
            at = Mathf.Clamp(at, 0, v.Length);
            del = Mathf.Clamp(del, 0, v.Length - at);
            ins ??= "";
            if (del == 0 && ins.Length == 0)
                return;

            // Snapshot everything that is expressed in offsets, so it can be
            // carried past the edit rather than reset by it.
            var caret = cursorIndex;
            var anchor = selectIndex;
            var extras = new List<(int a, int c)>(_extra);

            applyingRemote = true;
            var wasInternal = _internalReplace;
            _internalReplace = true;
            _inMultiEdit = true; // stops SetValueWithoutNotify clearing extra carets
            try
            {
                SetValueWithoutNotify(v.Substring(0, at) + ins + v.Substring(at + del));
            }
            finally
            {
                _inMultiEdit = false;
                _internalReplace = wasInternal;
            }

            var len = GetValueInternal().Length;
            cursorIndex = Mathf.Clamp(Rebase(caret, at, del, ins.Length), 0, len);
            selectIndex = Mathf.Clamp(Rebase(anchor, at, del, ins.Length), 0, len);
            _extra.Clear();
            foreach (var (a, c) in extras)
                _extra.Add((Mathf.Clamp(Rebase(a, at, del, ins.Length), 0, len),
                            Mathf.Clamp(Rebase(c, at, del, ins.Length), 0, len)));

            RebaseUndoStacks(at, del, ins.Length);
            RebaseRemoteCarets(at, del, ins.Length);

            // A remote insertion must never be swallowed into the local user's
            // current typing group, or their next undo would rip out a peer's text.
            BreakUndoGroup();

            applyingRemote = false;
            Notify();
            RefreshVisible();
        }

        /// <summary>Carry one offset past an edit. Offsets inside a deleted range
        /// collapse to its start — the conventional choice, and the same one the
        /// merge layer makes, so the buffer and the model never disagree.</summary>
        static int Rebase(int pos, int at, int del, int insLen)
        {
            if (pos <= at)
                return pos;
            if (pos >= at + del)
                return pos - del + insLen;
            return at;
        }

        void RebaseUndoStacks(int at, int del, int insLen)
        {
            RebaseUndoList(_undo, at, del, insLen);
            RebaseUndoList(_redo, at, del, insLen);
        }

        static void RebaseUndoList(List<UndoOp> ops, int at, int del, int insLen)
        {
            foreach (var op in ops)
            {
                op.Start = Rebase(op.Start, at, del, insLen);
                op.CursorBefore = Rebase(op.CursorBefore, at, del, insLen);
                op.SelectBefore = Rebase(op.SelectBefore, at, del, insLen);
                op.CursorAfter = Rebase(op.CursorAfter, at, del, insLen);
                op.SelectAfter = Rebase(op.SelectAfter, at, del, insLen);
                if (op.Segments == null)
                    continue;
                foreach (var seg in op.Segments)
                    seg.Start = Rebase(seg.Start, at, del, insLen);
            }
        }

        void RebaseRemoteCarets(int at, int del, int insLen)
        {
            foreach (var r in _remote)
            {
                r.Caret = Rebase(r.Caret, at, del, insLen);
                r.Anchor = Rebase(r.Anchor, at, del, insLen);
            }
        }

        // ------------------------------------------------------- remote carets

        /// <summary>Draw a small flag on the minimap for each peer, at the row
        /// they are on. The minimap is the only place you can see the WHOLE
        /// buffer at once, so it is where "who is where" belongs — a peer
        /// working four hundred lines away is invisible everywhere else.
        ///
        /// Row → y is exact rather than approximate: the paint samples row
        /// `i * step` at `y = i * ySpacing`, and ySpacing is rowH * step, so a
        /// real row lands at `row * rowH` with the sampling cancelling out.</summary>
        void PaintRemoteFlags(Painter2D p, float w, float rowH)
        {
            if (_remote.Count == 0)
                return;
            var textLen = GetValueInternal().Length;
            foreach (var r in _remote)
            {
                var caret = Mathf.Clamp(r.Caret, 0, textLen);
                IndexToLineCol(caret, out var line, out var col);
                var row = RowOfLine(line) + SubRowOfCol(line, col);
                if (row < 0 || row >= _totalRows)
                    continue;
                var y = row * rowH;
                var markH = Mathf.Max(2f, rowH);

                // A faint band across the strip locates the line…
                p.fillColor = new Color(r.Color.r, r.Color.g, r.Color.b, 0.16f);
                p.BeginPath();
                p.MoveTo(new Vector2(0, y));
                p.LineTo(new Vector2(w, y));
                p.LineTo(new Vector2(w, y + markH));
                p.LineTo(new Vector2(0, y + markH));
                p.ClosePath();
                p.Fill();

                // …and a solid pennant at the edge names the peer by colour.
                var flagH = Mathf.Max(4f, rowH * 2f);
                var flagW = Mathf.Min(7f, w * 0.35f);
                var fy = y + markH * 0.5f - flagH * 0.5f;
                p.fillColor = r.Color;
                p.BeginPath();
                p.MoveTo(new Vector2(0, fy));
                p.LineTo(new Vector2(flagW, fy + flagH * 0.5f));
                p.LineTo(new Vector2(0, fy + flagH));
                p.ClosePath();
                p.Fill();
            }
        }

        /// <summary>Peer positions changed: the minimap paints from its own
        /// callback, so it needs telling separately from the text view.</summary>
        void RepaintRemoteDecorations()
        {
            RefreshVisible();
            _minimap?.MarkDirtyRepaint();
        }

        internal void SetRemoteCaret(string userId, string name, Color color, int caret, int anchor)
        {
            var len = GetValueInternal().Length;
            caret = Mathf.Clamp(caret, 0, len);
            anchor = Mathf.Clamp(anchor, 0, len);
            foreach (var r in _remote)
                if (r.UserId == userId)
                {
                    r.Name = name;
                    r.Color = color;
                    r.Caret = caret;
                    r.Anchor = anchor;
                    RepaintRemoteDecorations();
                    return;
                }
            _remote.Add(new RemoteCaretState
            {
                UserId = userId, Name = name, Color = color, Caret = caret, Anchor = anchor,
            });
            RepaintRemoteDecorations();
        }

        internal void ClearRemoteCaret(string userId)
        {
            for (var i = 0; i < _remote.Count; i++)
                if (_remote[i].UserId == userId)
                {
                    _remote.RemoveAt(i);
                    RepaintRemoteDecorations();
                    return;
                }
        }

        internal void ClearAllRemoteCarets()
        {
            if (_remote.Count == 0)
                return;
            _remote.Clear();
            RepaintRemoteDecorations();
        }

        /// <summary>Draw every peer's caret, name and selection (contract §7).
        /// Mirrors RefreshExtraCarets' pooling and wrap handling.</summary>
        void RefreshRemoteCarets(int firstRow, int visible)
        {
            int caretQuad = 0, selQuad = 0, labelSlot = 0;
            foreach (var r in _remote)
            {
                var colour = r.Color;

                // Selection, translucent so the text stays readable underneath.
                int s = Mathf.Min(r.Anchor, r.Caret), e = Mathf.Max(r.Anchor, r.Caret);
                if (e > s)
                {
                    IndexToLineCol(s, out int sl, out int sc);
                    IndexToLineCol(e, out int el, out int ec);
                    for (int i = 0; i < visible; i++)
                    {
                        int row = firstRow + i;
                        if (row >= _totalRows) break;
                        RowToLineSub(row, out int line, out int sub);
                        if (line < sl || line > el) continue;
                        RowBounds(line, sub, out int rs, out int re);
                        int cs = line == sl ? Mathf.Max(sc, rs) : rs;
                        int ce = line == el ? Mathf.Min(ec, re) : re;
                        if (ce <= cs) continue;
                        if (selQuad >= _remoteSelPool.Count)
                        {
                            var q = new VisualElement();
                            q.style.position = Position.Absolute;
                            q.pickingMode = PickingMode.Ignore;
                            _content.Insert(0, q);
                            _remoteSelPool.Add(q);
                        }
                        var sq = _remoteSelPool[selQuad++];
                        sq.style.display = DisplayStyle.Flex;
                        sq.style.backgroundColor = new Color(colour.r, colour.g, colour.b, 0.22f);
                        float sx0 = MeasureRange(line, rs, cs);
                        sq.style.left = sx0;
                        sq.style.top = row * _lineHeight;
                        sq.style.width = Mathf.Max(2, MeasureRange(line, rs, ce) - sx0);
                        sq.style.height = _lineHeight;
                    }
                }

                // Caret bar plus the name flag above it.
                IndexToLineCol(r.Caret, out int cl, out int cc);
                int subRow = SubRowOfCol(cl, cc);
                int rowAbs = RowOfLine(cl) + subRow;
                if (rowAbs < firstRow || rowAbs >= firstRow + visible)
                    continue;

                RowBounds(cl, subRow, out int crs, out _);
                float x = MeasureRange(cl, crs, cc);

                if (caretQuad >= _remoteCaretPool.Count)
                {
                    var q = new VisualElement();
                    q.style.position = Position.Absolute;
                    q.pickingMode = PickingMode.Ignore;
                    _content.Add(q);
                    _remoteCaretPool.Add(q);
                }
                var cq = _remoteCaretPool[caretQuad++];
                cq.style.display = DisplayStyle.Flex;
                cq.style.backgroundColor = colour;
                cq.style.left = x;
                cq.style.top = rowAbs * _lineHeight;
                cq.style.width = 2;
                cq.style.height = _lineHeight;

                if (labelSlot >= _remoteLabelPool.Count)
                {
                    var l = new Label();
                    l.style.position = Position.Absolute;
                    l.pickingMode = PickingMode.Ignore;
                    l.style.fontSize = 9;
                    l.style.paddingLeft = 3;
                    l.style.paddingRight = 3;
                    l.style.color = Color.white;
                    l.style.unityFontStyleAndWeight = FontStyle.Bold;
                    _content.Add(l);
                    _remoteLabelPool.Add(l);
                }
                var lab = _remoteLabelPool[labelSlot++];
                lab.style.display = DisplayStyle.Flex;
                lab.text = r.Name ?? r.UserId;
                lab.style.backgroundColor = colour;
                lab.style.left = x;
                // Above the line, except on the first row where there is no room.
                lab.style.top = rowAbs > 0 ? (rowAbs * _lineHeight) - (_lineHeight * 0.85f)
                                           : (rowAbs * _lineHeight) + _lineHeight;
                lab.style.height = _lineHeight * 0.85f;
            }

            for (int i = caretQuad; i < _remoteCaretPool.Count; i++)
                _remoteCaretPool[i].style.display = DisplayStyle.None;
            for (int i = selQuad; i < _remoteSelPool.Count; i++)
                _remoteSelPool[i].style.display = DisplayStyle.None;
            for (int i = labelSlot; i < _remoteLabelPool.Count; i++)
                _remoteLabelPool[i].style.display = DisplayStyle.None;
        }
    }
}
