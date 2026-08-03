// ATE sample addon: the smallest useful resident addon.
// Lives in the shared addons folder; loaded by every ATE instance.
using ADKOM.TextEditor.Scripting;

[AteAddon(Name = "Hello Addon", Category = "Samples", ApiVersion = "1.3")]
public class HelloAddon : IAteAddonResident
{
    // OnLoad runs whenever addons are (re)loaded — subscribe to events here.
    public void OnLoad()
    {
        AteApi.documentSaved += d =>
            AteApi.DebugLog("[Hello Addon] saved: " + d.DisplayName);
    }

    // Run is invoked from ATE's Tools > Addons > Samples > Hello Addon.
    // AteApi.DebugLog is UnityEngine.Debug.Log's counterpart for addons:
    // same call, but it writes to ATE's console pane (View > Console).
    // Unity's console belongs to your project.
    public void Run()
    {
        var doc = AteApi.ActiveDocument;
        AteApi.DebugLog("[Hello Addon] active document: " +
            (doc != null ? doc.DisplayName : "(none)"));
    }
}
