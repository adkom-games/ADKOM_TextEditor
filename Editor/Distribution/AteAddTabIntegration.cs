#if UNITY_EDITOR
namespace ADKOM.TextEditor
{
    /// <summary>
    /// Asset Store variant: no dock "Add Tab" entry. Reaching that menu needs
    /// the internal UnityEditor.HostView.populateDefaultMenuItems event, and
    /// submissions may not reflect into Editor internals. ATE is still opened
    /// from Tools → ADKOM → Text Editor, the Window menu, Ctrl+Alt+8, or by
    /// double-clicking a text asset.
    /// </summary>
    internal static class AteAddTabIntegration
    {
    }
}
#endif
