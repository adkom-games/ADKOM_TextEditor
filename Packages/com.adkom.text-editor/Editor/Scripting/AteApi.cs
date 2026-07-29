#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>
    /// The STABLE scripting surface of the ADKOM Text Editor. Everything in
    /// this namespace follows semantic versioning: shape changes only on
    /// minor/major releases. Everything OUTSIDE this namespace is internal
    /// implementation and may change in any release.
    ///
    /// Usage: reference the ADKOM.TextEditor.Editor assembly from your own
    /// Editor asmdef, then call AteApi from editor scripts. See
    /// Documentation~/Scripting.md for examples.
    ///
    /// Threading: all members must be called from the main thread.
    /// Events are not raised re-entrantly: anything an event handler does to
    /// ATE will not fire nested events.
    /// </summary>
    public static partial class AteApi
    {
        /// <summary>The AteApi semantic version. Addons declare the version
        /// they target; MAJOR must match and their MINOR must not be newer.</summary>
        public const string ApiVersion = "1.1.0";

        // ---- Events (VS Code-shaped) ----

        /// <summary>A document tab was opened (file, untitled, or restored).</summary>
        public static event Action<AteDocument> documentOpened;
        /// <summary>A document tab was closed.</summary>
        public static event Action<AteDocument> documentClosed;
        /// <summary>A document was written to disk.</summary>
        public static event Action<AteDocument> documentSaved;
        /// <summary>The active tab changed. May carry null when the last tab closes.</summary>
        public static event Action<AteDocument> activeDocumentChanged;
        /// <summary>The active document's text changed. Debounced (~400ms)
        /// for typing; raised once per programmatic write.</summary>
        public static event Action<AteDocument> textChanged;

        // ---- Window / documents ----

        /// <summary>Opens (or focuses) the ATE window.</summary>
        public static void OpenWindow() => TextEditorWindow.Open();

        /// <summary>Opens a file at an optional 1-based line/column, reusing
        /// an existing tab for the same file. Creates the window if needed.</summary>
        public static void Open(string path, int line = 1, int column = 1)
            => TextEditorWindow.OpenExternal(path, line, column);

        /// <summary>Creates a new untitled document with optional initial
        /// text and returns its handle.</summary>
        public static AteDocument NewDocument(string initialText = "")
        {
            var w = Window(create: true);
            var doc = w.ApiNewDocument(initialText ?? string.Empty);
            return new AteDocument(w, doc);
        }

        /// <summary>Handles for every open document tab (Settings and other
        /// non-document tabs excluded). Empty when no window exists.</summary>
        public static IReadOnlyList<AteDocument> Documents
        {
            get
            {
                var list = new List<AteDocument>();
                var w = Window(create: false);
                if (w != null)
                    foreach (var d in w.ApiDocuments())
                        list.Add(new AteDocument(w, d));
                return list;
            }
        }

        /// <summary>The active document, or null (no window / Settings tab).</summary>
        public static AteDocument ActiveDocument
        {
            get
            {
                var w = Window(create: false);
                var d = w != null ? w.ApiActiveDocument() : null;
                return d != null ? new AteDocument(w, d) : null;
            }
        }

        // ---- Internal plumbing (not part of the contract) ----

        static TextEditorWindow Window(bool create)
        {
            var all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            if (all.Length > 0) return all[0];
            if (!create) return null;
            TextEditorWindow.Open();
            all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            return all.Length > 0 ? all[0] : null;
        }

        static bool _raising;
        static TextDocument _lastActiveRaised;

        static void Raise(Action<AteDocument> evt, TextEditorWindow w, TextDocument d)
        {
            if (evt == null || _raising) return;
            _raising = true;
            try { evt(d != null ? new AteDocument(w, d) : null); }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] A scripting event handler threw: " + ex.Message);
            }
            finally { _raising = false; }
        }

        internal static void NotifyOpened(TextEditorWindow w, TextDocument d)
            => Raise(documentOpened, w, d);

        internal static void NotifyClosed(TextEditorWindow w, TextDocument d)
            => Raise(documentClosed, w, d);

        internal static void NotifySaved(TextDocument d)
            => Raise(documentSaved, Window(create: false), d);

        internal static void NotifyActiveChanged(TextEditorWindow w, TextDocument d)
        {
            if (ReferenceEquals(d, _lastActiveRaised)) return; // dedupe SwitchTo noise
            _lastActiveRaised = d;
            Raise(activeDocumentChanged, w, d);
        }

        internal static void NotifyTextChanged(TextEditorWindow w, TextDocument d)
            => Raise(textChanged, w, d);
    }

    /// <summary>
    /// A stable handle to one open document tab. Handles become invalid when
    /// their tab closes (check <see cref="IsValid"/> after holding one across
    /// frames). Two handles to the same tab compare equal.
    /// </summary>
    public sealed partial class AteDocument
    {
        readonly TextEditorWindow _window;
        readonly TextDocument _doc;

        internal AteDocument(TextEditorWindow window, TextDocument doc)
        {
            _window = window;
            _doc = doc;
        }

        /// <summary>False once the tab has been closed (or the window died).</summary>
        public bool IsValid => _window != null && _doc != null && _window.ApiContains(_doc);

        /// <summary>Absolute file path, or null for untitled documents.</summary>
        public string Path => _doc.HasFile ? _doc.FilePath : null;
        public string DisplayName => _doc.DisplayName;
        public bool IsDirty => _doc.IsDirty;
        public bool IsUntitled => !_doc.HasFile;

        /// <summary>The document's full text (current even while typing).</summary>
        public string GetText() { EnsureValid(); return _doc.Content ?? string.Empty; }

        /// <summary>Replaces the whole text. UNDO NOTE: edits to the ACTIVE
        /// document go through the undo system (one undo step); edits to a
        /// background document are applied directly and are NOT undoable.</summary>
        public void SetText(string text)
        {
            EnsureValid();
            _window.ApiReplaceRange(_doc, 0, (_doc.Content ?? string.Empty).Length, text ?? string.Empty);
        }

        /// <summary>Replaces [start, end) with <paramref name="replacement"/>.
        /// Same undo rule as <see cref="SetText"/>: active = one undo step,
        /// background = not undoable.</summary>
        public void ReplaceRange(int start, int end, string replacement)
        {
            EnsureValid();
            _window.ApiReplaceRange(_doc, start, end, replacement ?? string.Empty);
        }

        /// <summary>Activates the tab and places the caret (1-based, clamped).</summary>
        public void GoTo(int line, int column = 1)
        {
            EnsureValid();
            _window.ApiActivate(_doc);
            _window.ApiGoTo(line, column);
        }

        /// <summary>Scrolls this document to its very end (when it is the active
        /// tab). For a growing game transcript this brings the input line into
        /// view — e.g. after restoring a saved game.</summary>
        public void ScrollToEnd() { EnsureValid(); _window.ApiScrollToEnd(_doc); }

        /// <summary>Brings this tab to the front.</summary>
        public void Activate() { EnsureValid(); _window.ApiActivate(_doc); }

        /// <summary>Saves to disk (prompts Save As for untitled documents).
        /// Returns false if the user cancelled or the write failed.</summary>
        public bool Save() { EnsureValid(); return _window.ApiSave(_doc); }

        /// <summary>Closes the tab. With <paramref name="discardChanges"/>
        /// false, a dirty document shows ATE's non-modal unsaved-changes
        /// banner and closes only after the user decides.</summary>
        public void Close(bool discardChanges = false)
        {
            EnsureValid();
            _window.ApiClose(_doc, discardChanges);
        }

        void EnsureValid()
        {
            if (!IsValid)
                throw new InvalidOperationException(
                    "This AteDocument handle is no longer valid (its tab was closed).");
        }

        public override bool Equals(object obj) =>
            obj is AteDocument other && ReferenceEquals(_doc, other._doc);
        public override int GetHashCode() => _doc != null ? _doc.GetHashCode() : 0;
        public override string ToString() => "AteDocument(" + DisplayName + ")";
    }
}
#endif
