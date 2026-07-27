// ATE sample addon: the smallest useful resident addon.
// Lives in the shared addons folder; loaded by every ATE instance.
using ADKOM.TextEditor.Scripting;

[AteAddon(Name = "Hello Addon", Category = "Samples", ApiVersion = "1.0")]
public class HelloAddon : IAteAddonResident
{
    // OnLoad runs whenever addons are (re)loaded — subscribe to events here.
    public void OnLoad()
    {
        AteApi.documentSaved += d =>
            UnityEngine.Debug.Log("[Hello Addon] saved: " + d.DisplayName);
    }

    // Run is invoked from ATE's Tools > Addons > Samples > Hello Addon.
    public void Run()
    {
        var doc = AteApi.ActiveDocument;
        UnityEngine.Debug.Log("[Hello Addon] active document: " +
            (doc != null ? doc.DisplayName : "(none)"));
    }
}
