// ATE sample addon: reads the active document and reports statistics.
using ADKOM.TextEditor.Scripting;

[AteAddon(Name = "Word Count", Category = "Text", ApiVersion = "1.0")]
public class WordCount : IAteAddon
{
    public void Run()
    {
        var doc = AteApi.ActiveDocument;
        if (doc == null)
        {
            UnityEngine.Debug.Log("[Word Count] no active document");
            return;
        }
        string text = doc.GetText();
        int words = 0, lines = 1;
        bool inWord = false;
        foreach (char c in text)
        {
            if (c == '\n') lines++;
            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord) { inWord = true; words++; }
        }
        UnityEngine.Debug.Log("[Word Count] " + doc.DisplayName + ": " +
            words + " words, " + text.Length + " chars, " + lines + " lines");
    }
}
