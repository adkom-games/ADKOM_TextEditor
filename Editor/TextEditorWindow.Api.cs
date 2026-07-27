#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Linq;

namespace ADKOM.TextEditor
{
    // Internal touchpoints for the stable scripting facade (Scripting/AteApi.cs).
    public partial class TextEditorWindow
    {
        internal bool ApiContains(TextDocument d) => _docs.Contains(d);

        internal IEnumerable<TextDocument> ApiDocuments()
        {
            foreach (var d in _docs)
                if (!d.IsSettings) yield return d;
        }

        internal TextDocument ApiActiveDocument() =>
            HasDocs && !Active.IsSettings ? Active : null;

        internal TextDocument ApiNewDocument(string initialText)
        {
            NewFile();
            var doc = Active;
            if (initialText.Length > 0)
            {
                doc.Content = initialText;
                doc.IsDirty = true;
                _code?.SetValueWithoutNotify(doc.Content);
                RebuildTabs();
                UpdateTitle();
            }
            return doc;
        }

        internal void ApiActivate(TextDocument d)
        {
            int i = _docs.IndexOf(d);
            if (i >= 0 && i != _active) SwitchTo(i);
        }

        internal void ApiGoTo(int line, int col) => _code?.GoToLine(line, col);

        /// <summary>Facade write path. Active document: routed through the
        /// undo system as one Programmatic step. Background document: applied
        /// directly to the model — documented as NOT undoable.</summary>
        internal void ApiReplaceRange(TextDocument d, int start, int end, string replacement)
        {
            if (HasDocs && Active == d && _code != null)
            {
                _code.ReplaceRangeInternal(start, end, replacement,
                    Mathf.Min(start, _code.value.Length) + replacement.Length,
                    CodeView.EditKind.Programmatic);
                return; // OnTextChanged handles model sync, dirty, and events
            }
            string v = d.Content ?? string.Empty;
            start = Mathf.Clamp(start, 0, v.Length);
            end = Mathf.Clamp(end, start, v.Length);
            d.Content = v.Substring(0, start) + replacement + v.Substring(end);
            d.IsDirty = true;
            RebuildTabs();
            UpdateTitle();
            Scripting.AteApi.NotifyTextChanged(this, d);
        }

        // ---- Game support (AteApi 1.1) ----

        internal void ApiSetGameMode(TextDocument d, bool on)
        {
            if (d.GameMode == on) return;
            d.GameMode = on;
            if (on && d.UndoWorld is CodeView.UndoWorld uw)
            {
                // Background doc entering game mode: parked history would
                // reference stale offsets once the game repaints — drop it.
                uw.Undo.Clear();
                uw.Redo.Clear();
            }
            if (HasDocs && Active == d && _code != null)
            {
                _code.gameMode = on;
                _code.wordWrap = on ? false : _wordWrap;
                RefreshFormatter(); // restores/drops syntax highlighting
                _code.RefreshVisiblePublic();
            }
        }

        internal bool ApiTryGetCursor(TextDocument d, out int line, out int col)
        {
            if (HasDocs && Active == d && _code != null)
            {
                line = _code.caretLine + 1;
                col = _code.caretColumn + 1;
                return true;
            }
            line = col = 0;
            return false;
        }

        /// <summary>WriteAt's line replacement: [start, end) is the line's
        /// span in the document; the caret stays where it was (a game
        /// repainting at 30 Hz must not scroll-chase the write position).</summary>
        internal void ApiWriteLine(TextDocument d, int start, int end, string newLineText)
        {
            if (HasDocs && Active == d && _code != null)
            {
                int keep = _code.cursorIndex;
                _code.ReplaceRangeInternal(start, end, newLineText,
                    Mathf.Min(keep, _code.value.Length), CodeView.EditKind.Programmatic);
                return;
            }
            ApiReplaceRange(d, start, end, newLineText);
        }

        internal void ApiSetColor(TextDocument d, int line0, int colStart0, int colEnd0,
            Color? fg, Color? bg)
        {
            var ov = d.Overlay ?? (d.Overlay = new CodeView.ColorOverlay());
            ov.Set(line0, colStart0, colEnd0, fg, bg);
            if (HasDocs && Active == d && _code != null)
            {
                _code.AttachOverlay(ov); // no-op when already attached
                ScheduleOverlayRefresh();
            }
        }

        internal void ApiClearColors(TextDocument d, int line0)
        {
            if (d.Overlay == null) return;
            if (line0 < 0) d.Overlay.Lines.Clear();
            else d.Overlay.Lines.Remove(line0);
            if (HasDocs && Active == d) ScheduleOverlayRefresh();
        }

        // Color calls arrive per-cell from game addons; one repaint per frame
        // is enough (and 30 Hz × a grid of quads is not).
        UnityEngine.UIElements.IVisualElementScheduledItem _ovRefresh;

        void ScheduleOverlayRefresh()
        {
            if (_ovRefresh == null)
                _ovRefresh = rootVisualElement.schedule.Execute(() => _code?.RefreshVisiblePublic());
            _ovRefresh.ExecuteLater(0);
        }

        internal void ApiSetFont(TextDocument d, string fontName, int size)
        {
            d.FontName = string.IsNullOrEmpty(fontName) ? null : fontName;
            d.FontSize = size > 0 ? Mathf.Clamp(size, 8, 40) : 0;
            if (HasDocs && Active == d)
                _code?.SetFontOverride(d.FontName, d.FontSize);
        }

        internal void ApiPrompt(string prompt, bool digitsOnly,
            System.Action<string> onCommit, System.Action onCancel, string initialValue)
        {
            StartStatusPrompt(prompt, digitsOnly, onCommit, initialValue, onCancel);
        }

        internal bool ApiSave(TextDocument d)
        {
            bool ok = FileService.Save(d);
            if (ok)
            {
                if (HasDocs && Active == d) RefreshFormatter();
                RebuildTabs();
                UpdateTitle();
                UpdateStatus();
            }
            return ok;
        }

        internal void ApiClose(TextDocument d, bool discardChanges)
        {
            if (discardChanges) d.IsDirty = false;
            int i = _docs.IndexOf(d);
            if (i >= 0) CloseTab(i); // dirty + !discard → non-modal banner
        }
    }
}
#endif
