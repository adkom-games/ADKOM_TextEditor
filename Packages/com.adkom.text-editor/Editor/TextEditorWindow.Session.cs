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
            // would block Unity. Instead dirty tabs persist their unsaved
            // CONTENT into the session and come back dirty on reopen.
            SaveSessionNow();
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
        }
    }
}
#endif
