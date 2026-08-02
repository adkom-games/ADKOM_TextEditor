#if UNITY_EDITOR
using UnityEditor;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Hands out the FUNCTIONAL main-thread SynchronizationContext after a
    /// domain reload. Early post-reload callbacks (CreateGUI, OnEnable, UI
    /// scheduler ticks) run before Unity installs its real context — a
    /// context captured then swallows Posts, so async completions (git
    /// results, searches) silently never land. delayCall is no escape
    /// either: one registered from inside a UIToolkit scheduler callback
    /// never fires. Polling EditorApplication.update until the
    /// UnitySynchronizationContext is current is the reliable path.
    /// </summary>
    internal static class AteMainCtx
    {
        /// <summary>Invokes <paramref name="apply"/> once, with the working
        /// Unity main-thread context, as soon as it is installed.</summary>
        public static void WhenReady(System.Action<System.Threading.SynchronizationContext> apply)
        {
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                var ctx = System.Threading.SynchronizationContext.Current;
                if (ctx == null || ctx.GetType().Name != "UnitySynchronizationContext") return;
                EditorApplication.update -= tick;
                apply(ctx);
            };
            EditorApplication.update += tick;
        }
    }
}
#endif
