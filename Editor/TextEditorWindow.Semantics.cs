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

        /// <summary>IntelliSense back end for the completion popup: resolves
        /// the candidates at <paramref name="offset"/> on a background thread
        /// and posts them back to the main thread. Deliberately silent when
        /// semantics are unavailable — the popup then runs word-based only
        /// (the callback is simply never invoked).</summary>
        void RequestSemanticCompletions(int offset, System.Action<List<CompletionItem>> onDone)
        {
            if (!EditorConfig.SemanticsEnabled) return;
            var provider = SemanticServices.Provider as ISemanticCompletion;
            string path = SemanticContextPath;
            if (provider == null || path == null ||
                (Active.HasFile && !Active.FilePath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase)))
                return;
            string text = _code.value;
            var ctx = _mainCtx;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<CompletionItem> items = null;
                try { provider.TryGetCompletions(path, text, offset, out items); }
                catch (System.Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Completion query failed: " + ex.Message);
                }
                ctx.Post(_ =>
                {
                    if (this == null || _code == null || _code.panel == null) return;
                    onDone(items);
                }, null);
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
            PushNavLocation();
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
        // --- Refactoring clients (Batch 6 must-haves) ---

        /// <summary>Lists every reference to the symbol at the caret in the
        /// console pane (path:line: text) and reports the count.</summary>
        internal void FindAllReferences()
        {
            var provider = SemanticServices.Provider as ISemanticRefactorings;
            if (!EditorConfig.SemanticsEnabled || provider == null)
            {
                PostStatus(L10n.Tr("Find All References needs Semantic Features (enable in Settings)."));
                return;
            }
            string path = SemanticContextPath;
            if (path == null) return;
            string text = _code.value;
            int offset = _code.cursorIndex;
            var ctx = _mainCtx;
            PostStatus(L10n.Tr("Resolving symbol…"));
            System.Threading.Tasks.Task.Run(() =>
            {
                List<SymbolReference> refs = null;
                try { provider.TryFindReferences(path, text, offset, out refs); }
                catch (System.Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Find All References failed: " + ex.Message);
                }
                ctx.Post(_ =>
                {
                    if (this == null) return;
                    if (refs == null || refs.Count == 0)
                    {
                        PostStatus(L10n.Tr("No references found."));
                        return;
                    }
                    // Search Results tab (Cary, 2026-07-27): click a row to jump
                    // there, opening the file if needed.
                    var items = new List<PickLocation>(refs.Count);
                    foreach (var r in refs)
                        items.Add(new PickLocation
                        { Path = r.Path, Line = r.Line, Col = r.Column, Preview = r.LineText });
                    ShowSearchResults(refs.Count == 1 ? L10n.Tr("1 reference:")
                        : string.Format(L10n.Tr("{0} references:"), refs.Count), items);
                }, null);
            });
        }

        /// <summary>Renames every in-document occurrence of the symbol at the
        /// caret via the status-bar prompt; applied as ONE undo step through
        /// the multi-caret machinery.</summary>
        internal void RenameSymbolAtCaret()
        {
            var provider = SemanticServices.Provider as ISemanticRefactorings;
            if (!EditorConfig.SemanticsEnabled || provider == null)
            {
                PostStatus(L10n.Tr("Rename needs Semantic Features (enable in Settings)."));
                return;
            }
            string path = SemanticContextPath;
            if (path == null) return;
            if (!provider.TryGetRenameSpans(path, _code.value, _code.cursorIndex,
                    out var spans, out string oldName))
            {
                PostStatus(L10n.Tr("This symbol cannot be renamed here."));
                return;
            }
            StartStatusPrompt(string.Format(L10n.Tr("Rename '{0}' to:"), oldName), digitsOnly: false, newName =>
            {
                if (string.IsNullOrEmpty(newName) || newName == oldName) return;
                if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                {
                    PostStatus(L10n.Tr("Not a valid identifier."));
                    return;
                }
                var regions = new List<(int s, int e)>();
                foreach (var (start, length) in spans) regions.Add((start, start + length));
                _code.MultiReplace(regions, new List<string> { newName });
                _code.CollapseExtraCarets();
                PostStatus(regions.Count == 1 ? L10n.Tr("Renamed 1 occurrence in this document.")
                    : string.Format(L10n.Tr("Renamed {0} occurrences in this document."), regions.Count));
            }, initialValue: oldName);
        }

        /// <summary>Format Document: re-indents every line from brace depth
        /// (string/comment-aware), preserving content and blank lines. One
        /// undo step. C# documents only.</summary>
        internal void FormatDocument()
        {
            if (!CanEditDoc) return;
            string text = _code.value;
            var lines = text.Split('\n');
            var outLines = new string[lines.Length];
            int depth = 0;
            bool inBlockComment = false, inVerbatim = false;
            int tab = EditorConfig.TabSize;
            for (int i = 0; i < lines.Length; i++)
            {
                string body = lines[i].TrimStart();
                bool lineStartsInCode = !inBlockComment && !inVerbatim;
                // Closing brace (or case-ish dedent) on the line start dedents it.
                int lineDepth = depth;
                if (lineStartsInCode && (body.StartsWith("}") || body.StartsWith(")")))
                    lineDepth = Mathf.Max(0, depth - 1);
                outLines[i] = body.Length == 0 || !lineStartsInCode
                    ? lines[i].TrimEnd()
                    : new string(' ', lineDepth * tab) + body.TrimEnd();
                // Scan the line to update depth and string/comment state.
                bool inString = false, inChar = false, lineComment = false;
                for (int c = 0; c < body.Length; c++)
                {
                    char ch = body[c];
                    char next = c + 1 < body.Length ? body[c + 1] : '\0';
                    if (lineComment) break;
                    if (inBlockComment)
                    {
                        if (ch == '*' && next == '/') { inBlockComment = false; c++; }
                        continue;
                    }
                    if (inVerbatim)
                    {
                        if (ch == '"') { if (next == '"') c++; else inVerbatim = false; }
                        continue;
                    }
                    if (inString)
                    {
                        if (ch == '\\') c++;
                        else if (ch == '"') inString = false;
                        continue;
                    }
                    if (inChar)
                    {
                        if (ch == '\\') c++;
                        else if (ch == '\'') inChar = false;
                        continue;
                    }
                    switch (ch)
                    {
                        case '/': if (next == '/') lineComment = true; else if (next == '*') { inBlockComment = true; c++; } break;
                        case '@': if (next == '"') { inVerbatim = true; c++; } break;
                        case '"': inString = true; break;
                        case '\'': inChar = true; break;
                        case '{': depth++; break;
                        case '}': depth = Mathf.Max(0, depth - 1); break;
                    }
                }
            }
            string formatted = string.Join("\n", outLines);
            if (formatted == text)
            {
                PostStatus(L10n.Tr("Document already formatted."));
                return;
            }
            _code.ReplaceRangeInternal(0, text.Length, formatted,
                Mathf.Min(_code.cursorIndex, formatted.Length), CodeView.EditKind.LineOp);
            PostStatus(L10n.Tr("Document formatted."));
        }

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
