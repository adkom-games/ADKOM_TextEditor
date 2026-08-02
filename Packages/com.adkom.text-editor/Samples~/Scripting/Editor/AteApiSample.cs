#if UNITY_EDITOR
// ============================================================================
// ADKOM Text Editor — AteApi sample
// Demonstrates the CORE surface of the stable scripting API
// (ADKOM.TextEditor.Scripting): window, documents, events, and the
// lifecycle caveats. Import via Package Manager → ADKOM Text Editor →
// Samples, then explore the "Tools/ATE Samples" menu.
//
// The GAME surface (API 1.1: game mode, WriteAt, colors, input, tick) is
// demonstrated end to end by the Snake sample addon, and the STATEFUL
// lifecycle (API 1.2: SaveState/RestoreState, StateTag) by Snake and
// Rogue — Tools → Addons → Install Sample Addons.
//
// The API contract, caveats, and reference table live in the package's
// Documentation~/Scripting.md — read "Things you must know" before
// building on this.
// ============================================================================
using System.IO;
using UnityEditor;
using UnityEngine;
using ADKOM.TextEditor.Scripting;

// ----------------------------------------------------------------------------
// EVENTS. Static event subscriptions are LOST on every domain reload
// (script recompile, play mode enter). Always subscribe from a
// [InitializeOnLoad] static constructor so they re-attach automatically —
// exactly like this class does.
// ----------------------------------------------------------------------------
[InitializeOnLoad]
public static class AteApiSampleEvents
{
    static AteApiSampleEvents()
    {
        AteApi.documentOpened += d => Debug.Log($"[AteApi sample] opened: {d.DisplayName}");
        AteApi.documentClosed += d => Debug.Log($"[AteApi sample] closed: {d.DisplayName}");
        AteApi.documentSaved += d => Debug.Log($"[AteApi sample] saved: {d.Path}");
        // May be raised with null when the last tab closes.
        AteApi.activeDocumentChanged += d =>
            Debug.Log($"[AteApi sample] active: {(d != null ? d.DisplayName : "(none)")}");
        // Debounced ~400ms while typing; once per programmatic write.
        AteApi.textChanged += d =>
            Debug.Log($"[AteApi sample] changed: {d.DisplayName} ({d.GetText().Length} chars)");
    }
}

public static class AteApiSampleMenus
{
    // ---- AteApi.OpenWindow ----
    [MenuItem("Tools/ATE Samples/1 Open Window")]
    static void OpenWindow() => AteApi.OpenWindow();

    // ---- AteApi.Open (path, 1-based line/column; reuses existing tabs) ----
    [MenuItem("Tools/ATE Samples/2 Open This Sample At Line 20")]
    static void OpenSelf()
    {
        string path = Path.GetFullPath("Packages/com.adkom.text-editor/README.md");
        AteApi.Open(path, line: 20, column: 1);
    }

    // ---- AteApi.NewDocument + AteDocument.SetText / GetText ----
    [MenuItem("Tools/ATE Samples/3 New Document")]
    static void NewDoc()
    {
        AteDocument doc = AteApi.NewDocument("// Created by AteApi.NewDocument\n");
        doc.SetText(doc.GetText() + "// ...and grown by SetText.\n");
        Debug.Log($"[AteApi sample] new doc has {doc.GetText().Length} chars, " +
                  $"IsUntitled={doc.IsUntitled}, IsDirty={doc.IsDirty}");
    }

    // ---- AteApi.Documents + handle metadata ----
    [MenuItem("Tools/ATE Samples/4 List Open Documents")]
    static void ListDocs()
    {
        foreach (AteDocument d in AteApi.Documents)
            Debug.Log($"[AteApi sample] {d.DisplayName}  path={d.Path ?? "(untitled/virtual)"}  " +
                      $"dirty={d.IsDirty}  valid={d.IsValid}");
    }

    // ---- AteApi.ActiveDocument + ReplaceRange (UNDOABLE: doc is active) ----
    [MenuItem("Tools/ATE Samples/5 Stamp Header Into Active Doc")]
    static void StampActive()
    {
        AteDocument doc = AteApi.ActiveDocument;
        if (doc == null) { Debug.Log("[AteApi sample] no active document"); return; }
        // The ACTIVE document routes through the undo system: this stamp is
        // one Ctrl+Z step for the user.
        doc.ReplaceRange(0, 0, "// (c) ADKOM Games — stamped by AteApi\n");
    }

    // ---- Background edit (NOT undoable) + Activate + GoTo ----
    [MenuItem("Tools/ATE Samples/6 Append To A Background Doc")]
    static void BackgroundEdit()
    {
        AteDocument active = AteApi.ActiveDocument;
        foreach (AteDocument d in AteApi.Documents)
        {
            if (d.Equals(active)) continue; // handles compare by document
            // CAVEAT: this document is NOT active, so this edit bypasses the
            // undo system — the user cannot Ctrl+Z it. Call d.Activate()
            // first if they should be able to.
            string text = d.GetText();
            d.ReplaceRange(text.Length, text.Length, "\n// appended in background (not undoable)\n");
            d.Activate();               // bring it to the front...
            d.GoTo(int.MaxValue, 1);    // ...and jump to the end (clamped)
            return;
        }
        Debug.Log("[AteApi sample] open a second tab first");
    }

    // ---- Save / Close, handle invalidation ----
    [MenuItem("Tools/ATE Samples/7 Full Lifecycle Demo")]
    static void Lifecycle()
    {
        string path = Path.Combine(Path.GetTempPath(), "ate_api_sample.txt");
        File.WriteAllText(path, "lifecycle demo\n");

        AteApi.Open(path);                       // documentOpened fires
        AteDocument doc = AteApi.ActiveDocument;
        doc.ReplaceRange(0, 0, "// header\n");   // undoable (active)
        // CAVEAT: Save() on an UNTITLED doc opens a modal Save As dialog;
        // file-backed docs (like this one) save silently. False = cancelled.
        if (doc.Save())                          // documentSaved fires
            Debug.Log($"[AteApi sample] saved to {doc.Path}");

        // CAVEAT: Close() on a DIRTY doc (without discardChanges) only shows
        // ATE's non-modal banner — the tab is still open when Close returns,
        // and closes later if/when the user picks Save or Discard. With
        // discardChanges: true it closes immediately.
        doc.Close(discardChanges: true);         // documentClosed fires

        // CAVEAT: handles invalidate when their tab closes (and after any
        // domain reload). Check IsValid before reusing a stored handle —
        // members of an invalid handle throw InvalidOperationException.
        Debug.Log($"[AteApi sample] handle after close: IsValid={doc.IsValid}");
        File.Delete(path);
    }
}
#endif
