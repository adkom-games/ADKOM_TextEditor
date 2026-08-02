#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Small NON-MODAL floating notice shown right after the ATE window is
    /// closed with unsaved documents. It never blocks the editor main loop
    /// (no modal dialogs — session persistence already guarantees the buffers
    /// survive), it just makes the moment visible and offers one-click
    /// Save All. Ignoring or closing it is always safe.
    /// </summary>
    public class AteUnsavedNotice : EditorWindow
    {
        static bool _editorQuitting;

        [InitializeOnLoadMethod]
        static void TrackQuit() => EditorApplication.quitting += () => _editorQuitting = true;

        static AteUnsavedNotice _open; // singleton: repeated closes must not stack notices
        List<TextDocument> _dirtyDocs;

        /// <summary>A domain reload orphans the notice: its document
        /// references die with the domain, and the session already protects
        /// those buffers — so a stale notice simply closes itself.</summary>
        void CreateGUI()
        {
            if (_dirtyDocs == null)
                EditorApplication.delayCall += () => { if (this != null) Close(); };
        }

        /// <summary>Opens the notice for the given documents (called from the
        /// closing window's OnDestroy, deferred a tick so we never open UI
        /// during teardown). Skipped while the editor itself is quitting.
        /// Re-entrant closes (e.g. automation cycling the window) refresh the
        /// EXISTING notice instead of spawning another.</summary>
        public static void ShowFor(List<TextDocument> dirtyDocs)
        {
            if (_editorQuitting || dirtyDocs == null || dirtyDocs.Count == 0) return;
            EditorApplication.delayCall += () =>
            {
                if (_editorQuitting) return;
                if (_open != null)
                {
                    _open._dirtyDocs = dirtyDocs;
                    _open.Build();
                    _open.Repaint();
                    return;
                }
                var win = CreateInstance<AteUnsavedNotice>();
                _open = win;
                win._dirtyDocs = dirtyDocs;
                win.titleContent = new GUIContent(L10n.Tr("ATE — Unsaved Documents"));
                win.minSize = new Vector2(380, 150);
                var main = EditorGUIUtility.GetMainWindowPosition();
                win.position = new Rect(main.center.x - 210, main.center.y - 110, 420, 180);
                win.ShowUtility(); // floating + focused, NOT modal
                win.Build();
            };
        }

        void Build()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 12;
            root.style.paddingTop = 10;

            var head = new Label(_dirtyDocs.Count == 1
                ? L10n.Tr("The editor closed with 1 unsaved document.")
                : string.Format(L10n.Tr("The editor closed with {0} unsaved documents."), _dirtyDocs.Count));
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.whiteSpace = WhiteSpace.Normal;
            root.Add(head);

            var safe = new Label(L10n.Tr("Nothing is lost — they are kept in your session and will reopen with the window."));
            safe.style.whiteSpace = WhiteSpace.Normal;
            safe.style.opacity = 0.75f;
            safe.style.marginTop = 4;
            root.Add(safe);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.maxHeight = 120;
            list.style.marginTop = 6;
            foreach (var d in _dirtyDocs)
                list.Add(new Label("  • " + d.DisplayName));
            root.Add(list);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 10;
            var saveBtn = new Button(SaveAllNow) { text = L10n.Tr("Save All Now") };
            var laterBtn = new Button(Close) { text = L10n.Tr("Keep in Session") };
            row.Add(saveBtn);
            row.Add(laterBtn);
            root.Add(row);
        }

        void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        void SaveAllNow()
        {
            foreach (var d in _dirtyDocs)
                if (d.IsDirty) FileService.Save(d); // untitled docs prompt Save As
            // Refresh the persisted session so the saved docs don't come back
            // marked dirty on the next open.
            var tabs = new List<EditorConfig.SessionTab>();
            foreach (var d in _dirtyDocs)
            {
                if (!d.HasFile && !d.IsDirty) continue;
                tabs.Add(new EditorConfig.SessionTab
                {
                    path = d.HasFile ? Path.GetFullPath(d.FilePath) : string.Empty,
                    dirty = d.IsDirty,
                    content = d.IsDirty ? d.Content : null,
                    mdRendered = d.MdRendered,
                    mdUnlocked = !d.MdLocked
                });
            }
            EditorConfig.MergeSessionSaveState(tabs);
            int stillDirty = _dirtyDocs.FindAll(d => d.IsDirty).Count;
            if (stillDirty == 0) Close();
            else Build(); // a cancelled Save As keeps its doc listed
        }
    }
}
#endif
