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
    // Optional compiler-backed semantics: debounced classification pass, Go to Definition, metadata views.
    public partial class TextEditorWindow
    {
        void ScheduleSemanticPassCancel() => _semanticPending?.Pause();

        void ScheduleSemanticPass()
        {
            if (!EditorConfig.SemanticsEnabled) return;
            string ctxPath = SemanticContextPath;
            if (SemanticServices.Provider == null || ctxPath == null ||
                (Active.HasFile && !Active.FilePath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase)))
                return;
            if (_semanticPending == null)
                _semanticPending = rootVisualElement.schedule.Execute(StartSemanticPass);
            _semanticPending.ExecuteLater(400); // debounce typing
        }

        void StartSemanticPass()
        {
            var provider = SemanticServices.Provider;
            if (provider == null) return;
            string path = SemanticContextPath;
            if (path == null) return;
            string text = _code.value;
            int version = _code.DocVersion;
            var ctx = _mainCtx;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (provider.TryGetClassifiedSpans(path, text, out var spans))
                        ctx.Post(_ =>
                        {
                            // Window may have been destroyed while the task ran.
                            if (this == null || _code == null || _code.panel == null) return;
                            _code.ApplySemanticSpans(spans, version);
                        }, null);
                }
                catch (System.Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Semantic pass failed: " + ex.Message);
                }
            });
        }

        /// <summary>Go to Definition at (line, col) — Ctrl+Click / F12 / Ctrl+B.
        /// Requires the semantics module; resolved on a background thread.</summary>
        void NavigateToDefinition(int line, int col)
        {
            if (!EditorConfig.SemanticsEnabled)
            {
                if (EditorUtility.DisplayDialog(L10n.Tr("Go to Definition"),
                    L10n.Tr("Go to Definition needs Semantic Features, which are currently disabled.\n\n" +
                    "Enable them now? The semantics module — and, if your project has no Roslyn, " +
                    "the bundled MIT-licensed Roslyn assemblies — will be installed automatically."),
                    L10n.Tr("Enable and Install"), L10n.Tr("Cancel")))
                {
                    EditorConfig.SemanticsEnabled = true;
                    SyncSettingsControls();
                    SemanticSetup.EnsureInstalled();
                }
                return;
            }
            var provider = SemanticServices.Provider;
            if (provider == null)
            {
                // Informational only — no decision to make, so no modal.
                PostStatus(L10n.Tr("Semantic features are still installing or compiling — try again in a moment."));
                SemanticSetup.EnsureInstalled(silent: true); // nudge any stalled step
                return;
            }
            string path = SemanticContextPath; // metadata views navigate too
            if (path == null) return;
            string text = _code.value;
            int offset = _code.LineColToIndex(line, col);
            var ctx = _mainCtx;
            PostStatus(L10n.Tr("Resolving symbol…"));
            System.Threading.Tasks.Task.Run(() =>
            {
                string status = null;
                string defPath = null;
                string metaTitle = null, metaSource = null;
                int dl = 0, dc = 0, metaLine = 0;
                try
                {
                    if (provider.TryFindDefinition(path, text, offset, out defPath, out dl, out dc, out string origin))
                    {
                        if (defPath == null)
                        {
                            // Metadata symbol: open a signature-stub view.
                            if (provider.TryGetMetadataSource(path, text, offset, out metaTitle, out metaSource, out metaLine))
                                status = null;
                            else
                                status = L10n.Tr("Defined in ") + origin;
                        }
                    }
                    else status = L10n.Tr("Definition not found.");
                }
                catch (System.Exception ex) { status = L10n.Tr("Go to Definition failed: ") + ex.Message; }
                ctx.Post(_ =>
                {
                    if (this == null) return; // window destroyed mid-resolve
                    if (status != null) { PostStatus(status); return; }
                    if (metaSource != null) { OpenMetadataView(metaTitle, metaSource, metaLine, path); return; }
                    OpenExternal(defPath, dl + 1, dc + 1);
                }, null);
            });
        }

        /// <summary>The compilation-context path for the active document:
        /// its own file, or the originating file for metadata views.</summary>
        string SemanticContextPath =>
            !CanEditDoc ? null
            : Active.HasFile ? Active.FilePath
            : Active.VirtualCSharp ? Active.VirtualContextPath
            : null;

        /// <summary>Opens (or switches to) a virtual "from metadata" document
        /// and places the caret on the requested symbol's line.</summary>
        // NOTE: a metadata stub is generated from the compilation at the time
        // it was opened. If sources change afterwards, navigating FROM a stale
        // stub can land on shifted line numbers in the real file (defect #12,
        // inherent to snapshot-based stubs — reopen the stub to refresh).
        void OpenMetadataView(string title, string source, int line, string contextPath)
        {
            OpenVirtualDoc(title, source, csharp: true);
            int i = _docs.FindIndex(d => d.VirtualName == title);
            if (i >= 0) _docs[i].VirtualContextPath = contextPath;
            ScheduleSemanticPass();
            _code.GoToLine(line + 1, 1);
            PostStatus(title);
        }
    }
}
#endif
