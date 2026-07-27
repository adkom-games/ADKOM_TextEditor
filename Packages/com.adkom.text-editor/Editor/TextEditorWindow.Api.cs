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
