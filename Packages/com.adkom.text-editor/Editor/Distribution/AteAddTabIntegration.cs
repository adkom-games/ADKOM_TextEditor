#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>Puts ATE in every dock's tab context / "⋮" menu under
    /// "Add Tab". The Add Tab list itself is a fixed set of built-in pane
    /// types (HostView.GetPaneTypes), so third-party windows can only get
    /// there via the internal static HostView.populateDefaultMenuItems
    /// event — Action&lt;GenericMenu, EditorWindow&gt;, raised while the
    /// menu is built. Same-named submenus merge, so our item lands inside
    /// the existing "Add Tab". Reflection-based and failure-tolerant: if
    /// a future Unity removes the event, we silently lose the entry.
    ///
    /// Reflection into Editor internals is not permitted in Asset Store
    /// submissions, so this file is one of the store overrides — CI replaces
    /// it with an empty variant on the upm-store branch, and the guarded
    /// early-out below keeps it inert even if that step is ever missed.
    /// </summary>
    [InitializeOnLoad]
    internal static class AteAddTabIntegration
    {
        static AteAddTabIntegration()
        {
            if (AteBuildFlavor.AssetStore) return;
            try
            {
                var hostT = typeof(EditorWindow).Assembly.GetType("UnityEditor.HostView");
                var ev = hostT?.GetEvent("populateDefaultMenuItems",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (ev == null) return;
                System.Action<GenericMenu, EditorWindow> handler = (menu, view) =>
                {
                    if (view is TextEditorWindow) return; // already one here
                    menu.AddItem(new GUIContent("Add Tab/ADKOM Text Editor"), false, () =>
                    {
                        var existing = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
                        if (existing.Length > 0) { existing[0].Show(); existing[0].Focus(); return; }
                        var win = ScriptableObject.CreateInstance<TextEditorWindow>();
                        try
                        {
                            // Dock as a sibling tab of the clicked pane.
                            var parentF = typeof(EditorWindow).GetField("m_Parent",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic);
                            object dock = view != null ? parentF?.GetValue(view) : null;
                            var addTab = dock?.GetType().GetMethod("AddTab",
                                new[] { typeof(EditorWindow), typeof(bool) });
                            if (addTab != null) addTab.Invoke(dock, new object[] { win, true });
                            else win.Show();
                        }
                        catch (System.Exception) { win.Show(); }
                        win.UpdateTitle();
                        win.Focus();
                    });
                };
                // The add accessor is internal — AddEventHandler refuses
                // non-public accessors, so invoke it directly.
                ev.GetAddMethod(true).Invoke(null, new object[] { handler });
            }
            catch (System.Exception) { /* menu entry is best-effort */ }
        }
    }
}
#endif
