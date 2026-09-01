// =============================================================================
//  ADKOM Text Editor — window-side co-editing glue (contract v1.0).
//
//  The window is ATE's document manager, so this partial owns the mapping
//  between a co-editing path (project-relative, as ALS names it) and a
//  TextDocument, and it is where local edits are captured and remote ones are
//  applied. One CodeView serves every tab, so remote carets belong to the
//  ACTIVE document and are swapped on tab change.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    public partial class TextEditorWindow
    {
        // Which CodeView the capture hooks are attached to. Deliberately NOT a
        // bool: the view is rebuilt (domain reloads, view recreation) while the
        // window instance lives on, and a bool guard would then report "attached"
        // against a CodeView that no longer exists — capture silently dead, which
        // is exactly the failure this cost an afternoon to find.
        CodeView _coEditAttachedTo;

        /// <summary>Subscribe the capture hooks to the CURRENT CodeView. Cheap and
        /// idempotent, so it is safe to call from the poll as well as at
        /// construction — which is what makes capture self-healing.</summary>
        internal void CoEditAttach()
        {
            if (_code == null || ReferenceEquals(_coEditAttachedTo, _code))
                return;
            if (_coEditAttachedTo != null)
            {
                _coEditAttachedTo.onTextEdit -= OnCoEditLocalEdit;
                _coEditAttachedTo.onCaretMoved -= OnCoEditCaretMoved;
            }
            _coEditAttachedTo = _code;
            _code.onTextEdit += OnCoEditLocalEdit;
            _code.onCaretMoved += OnCoEditCaretMoved;
        }

        void OnCoEditLocalEdit(int start, int removed, string inserted)
        {
            if (!HasDocs || Active == null)
                return;
            CoEdit.Bridge.LocalEdit(CoEditPathOf(Active), start, removed, inserted);
        }

        void OnCoEditCaretMoved(int caret, int anchor)
        {
            if (!HasDocs || Active == null)
                return;
            CoEdit.Bridge.LocalCaret(CoEditPathOf(Active), caret, anchor);
        }

        /// <summary>Project-relative, forward-slashed — the identity ALS uses.
        /// Null for documents with no file (untitled buffers never co-edit).</summary>
        internal static string CoEditPathOf(TextDocument doc)
        {
            if (doc == null || !doc.HasFile)
                return null;
            var full = doc.FilePath.Replace('\\', '/');
            var root = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(root))
                return null;
            return full.StartsWith(root + "/", System.StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length + 1)
                : null; // outside the project — not co-editable (contract §2)
        }

        internal TextDocument CoEditFind(string path)
        {
            if (!HasDocs || string.IsNullOrEmpty(path))
                return null;
            foreach (var d in _docs)
                if (CoEditPathOf(d) == path)
                    return d;
            return null;
        }

        internal string CoEditTextOf(string path)
        {
            var doc = CoEditFind(path);
            if (doc == null)
                return null;
            return HasDocs && Active == doc && _code != null ? _code.value : doc.Content;
        }

        /// <summary>Apply a peer's edits. The active document goes through the
        /// CodeView's remote path (which preserves this user's caret); a
        /// background document is spliced straight into the model.</summary>
        internal void CoEditApply(string path, IReadOnlyList<CoEdit.Edit> edits)
        {
            var doc = CoEditFind(path);
            if (doc == null || edits == null)
                return;
            if (HasDocs && Active == doc && _code != null)
            {
                foreach (var e in edits)
                    _code.ApplyRemoteEdit(e.At, e.Del, e.Ins);
                Active.Content = _code.value;
                Active.IsDirty = true;
                RebuildTabs();
                UpdateTitle();
                return;
            }
            var v = doc.Content ?? "";
            foreach (var e in edits)
            {
                var at = Mathf.Clamp(e.At, 0, v.Length);
                var del = Mathf.Clamp(e.Del, 0, v.Length - at);
                v = v.Substring(0, at) + e.Ins + v.Substring(at + del);
            }
            doc.Content = v;
            doc.IsDirty = true;
            RebuildTabs();
            UpdateTitle();
        }

        /// <summary>Replace a document wholesale — used when a path becomes shared
        /// and the promoted base content arrives.</summary>
        internal void CoEditSetText(string path, string content, bool readOnly)
        {
            var doc = CoEditFind(path);
            if (doc == null)
                return;
            doc.Content = content ?? "";
            if (HasDocs && Active == doc && _code != null)
            {
                _code.applyingRemote = true;
                try
                {
                    _code.SetValueWithoutNotify(doc.Content);
                    _code.readOnly = readOnly;
                }
                finally
                {
                    _code.applyingRemote = false;
                }
            }
            RebuildTabs();
            UpdateTitle();
        }

        internal void CoEditSetReadOnly(string path, bool readOnly)
        {
            var doc = CoEditFind(path);
            if (doc != null && HasDocs && Active == doc && _code != null)
                _code.readOnly = readOnly;
        }

        internal CodeView CoEditActiveView => _code;

        /// <summary>Re-evaluate the tab strip after a presence change. Cheap:
        /// RebuildTabs early-returns unless its signature actually changed.</summary>
        internal void CoEditRefreshTabs() => RebuildTabs();

        internal IEnumerable<TextDocument> OpenDocuments() =>
            _docs ?? (IEnumerable<TextDocument>)System.Array.Empty<TextDocument>();

        internal TextDocument ActiveDocument() => HasDocs ? Active : null;

        /// <summary>Remote carets live on the shared CodeView, so a tab change
        /// swaps them for the newly active document's set.</summary>
        internal void CoEditOnTabChanged()
        {
            if (_code == null)
                return;
            _code.ClearAllRemoteCarets();
            if (HasDocs && Active != null)
                CoEdit.Bridge.RepublishCarets(CoEditPathOf(Active));
            _code.readOnly = CoEdit.Bridge.IsReadOnly(CoEditPathOf(Active));
        }
    }
}
