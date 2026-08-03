// ATE sample addon: modifies the active document through the AteApi.
// Appends a timestamp comment line to the end of the active document.
using System;
using ADKOM.TextEditor.Scripting;

[AteAddon(Name = "Insert Timestamp", Category = "Text", ApiVersion = "1.3")]
public class InsertTimestamp : IAteAddon
{
    public void Run()
    {
        var doc = AteApi.ActiveDocument;
        if (doc == null)
        {
            // ATE's console pane (View > Console), not Unity's.
            AteApi.DebugLog("[Insert Timestamp] no active document");
            return;
        }
        string text = doc.GetText();
        string stamp = "// " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        // ReplaceRange at the end == append; edits flow through ATE's normal
        // undo pipeline, so this is one undoable step.
        doc.ReplaceRange(text.Length, text.Length,
            (text.EndsWith("\n") || text.Length == 0 ? "" : "\n") + stamp + "\n");
    }
}
