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
    // Tab-session persistence: save on close, 30s dirty autosave, restore on reopen (incl. unsaved buffers).
    public partial class TextEditorWindow
    {
        void OnDestroy()
        {
            // NO dialogs here — the window is being torn down, so a modal
            // would block Unity (and the MCP server). Dirty tabs persist
            // their unsaved CONTENT into the session and come back dirty on
            // reopen; a NON-MODAL floating notice offers one-click Save All.
            SaveSessionNow();
            var dirty = _docs?.FindAll(d => !d.IsSettings && d.IsDirty);
            if (dirty != null && dirty.Count > 0) AteUnsavedNotice.ShowFor(dirty);
        }

        /// <summary>Writes the current tab session (including dirty tabs'
        /// unsaved content). Called on window close AND periodically while
        /// any tab is dirty, so a hard editor crash loses at most
        /// SessionAutosaveMs of typing (defect #2).</summary>
        void SaveSessionNow()
        {
            if (_docs == null) return;
            var tabs = new List<EditorConfig.SessionTab>();
            int activeIndex = 0;
            for (int i = 0; i < _docs.Count; i++)
            {
                var d = _docs[i];
                if (d.IsSettings || !string.IsNullOrEmpty(d.VirtualName)) continue;
                if (!d.HasFile && !d.IsDirty) continue; // empty untitled
                if (i == _active) activeIndex = tabs.Count;
                tabs.Add(new EditorConfig.SessionTab
                {
                    path = d.HasFile ? Path.GetFullPath(d.FilePath) : string.Empty,
                    dirty = d.IsDirty,
                    content = d.IsDirty ? d.Content : null,
                    mdRendered = d.MdRendered
                });
            }
            EditorConfig.SaveSession(tabs, activeIndex);
        }

        void StartSessionAutosave()
        {
            rootVisualElement.schedule
                .Execute(() => { if (_docs != null && _docs.Exists(d => d.IsDirty)) SaveSessionNow(); })
                .Every(SessionAutosaveMs);
        }

        /// <summary>Reopens the tabs from the last time the window was closed.
        /// Runs only when the window starts with no documents (a fresh window;
        /// domain reloads keep their docs via serialization). Dirty tabs are
        /// restored from their persisted content, still dirty; clean tabs load
        /// from disk; files missing by now are skipped.</summary>
        void RestoreSession()
        {
            var tabs = EditorConfig.LoadSession(out int activeIndex);
            foreach (var t in tabs)
            {
                var doc = new TextDocument();
                bool hasFile = !string.IsNullOrEmpty(t.path) && File.Exists(t.path);
                if (hasFile)
                {
                    try { doc.LoadFrom(t.path); }
                    catch (System.Exception ex)
                    {
                        AteConsole.Warn("[ADKOM Text Editor] Session tab could not be restored: " + t.path + " — " + ex.Message);
                        continue;
                    }
                }
                if (t.dirty && t.content != null)
                {
                    if (!hasFile && !string.IsNullOrEmpty(t.path)) doc.FilePath = t.path;
                    doc.Content = t.content;
                    doc.IsDirty = true;
                }
                else if (!hasFile) continue;
                doc.MdRendered = t.mdRendered;
                _docs.Add(doc);
            }
            if (HasDocs) _active = Mathf.Clamp(activeIndex, 0, _docs.Count - 1);

            // First run in this project (fresh install, nothing to restore):
            // open the package README and release notes as a welcome. The
            // flag is set either way so the welcome never reappears.
            if (!EditorConfig.WelcomeShown)
            {
                EditorConfig.WelcomeShown = true;
                if (!HasDocs)
                {
                    foreach (var name in new[] { "RELEASE-NOTES.md", "README.md" })
                    {
                        try
                        {
                            string p = Path.GetFullPath("Packages/com.adkom.text-editor/" + name);
                            if (File.Exists(p))
                            {
                                var doc = new TextDocument();
                                doc.LoadFrom(p);
                                _docs.Insert(0, doc);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            AteConsole.Warn("[ADKOM Text Editor] Welcome tab failed to open: " + ex.Message);
                        }
                    }
                    if (HasDocs) _active = 0; // README in front
                }
            }

            // Safety net for the close-time notice: if dirty buffers came back
            // from the session, say so (banner is built by then — deferred).
            int restoredDirty = _docs.FindAll(d => d.IsDirty).Count;
            if (restoredDirty > 0)
                rootVisualElement.schedule.Execute(() =>
                    ShowBanner(restoredDirty == 1
                        ? L10n.Tr("1 document has unsaved changes from your last session.")
                        : string.Format(L10n.Tr("{0} documents have unsaved changes from your last session."), restoredDirty),
                        (L10n.Tr("Save All"), () => { SaveAll(); HideBanner(); }),
                        (L10n.Tr("Dismiss"), HideBanner))).ExecuteLater(0);
        }
    }
}
#endif
