#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Optional bridge to Unity's AI Assistant (com.unity.ai.assistant).
    /// Everything goes through reflection so ATE has NO hard dependency on
    /// the (prerelease) package: when it is absent the menu items simply
    /// don't appear. Uses only the documented public API
    /// (AssistantApi.PromptThenRun + AttachedContext/VirtualAttachment) —
    /// the user types the prompt in Assistant's own anchored popup, so no
    /// AI call happens until they submit it.
    /// </summary>
    internal static class UnityAiBridge
    {
        static Type _api, _ctx, _va;
        static bool _searched;

        public static bool Available
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _api = _api ?? asm.GetType("Unity.AI.Assistant.Editor.Api.AssistantApi");
                        _va = _va ?? asm.GetType("Unity.AI.Assistant.VirtualAttachment");
                    }
                    _ctx = _api?.GetNestedType("AttachedContext");
                }
                return _api != null && _ctx != null && _va != null;
            }
        }

        /// <summary>The Unity account the Editor (and thus Unity AI) is
        /// signed into, or null when signed out.</summary>
        public static string UnityAccountName
        {
            get
            {
                string n = CloudProjectSettings.userName;
                return string.IsNullOrEmpty(n) || n == "anonymous" ? null : n;
            }
        }

        // NOTE: an editor-wide Unity-account sign-out button was built and
        // REMOVED the same day (2026-07-27): logging the whole Editor out is
        // far beyond ATE's remit. Unity's own account UI owns that lifecycle.

        /// <summary>Opens Assistant's prompt popup anchored to</summary>
        /// <paramref name="anchor"/>, with <paramref name="payload"/> attached
        /// as a virtual text document. The prompt is typed by the user; an AI
        /// call (and points) only happens when they submit it.</summary>
        public static bool Ask(VisualElement anchor, string placeholder,
            string payload, string displayName)
        {
            if (!Available) return false;
            try
            {
                object ctx = Activator.CreateInstance(_ctx);
                object va = Activator.CreateInstance(_va,
                    payload, "text/plain", displayName, null);
                _ctx.GetMethods().First(m => m.Name == "Add" &&
                        m.GetParameters()[0].ParameterType == _va)
                    .Invoke(ctx, new[] { va });
                var run = _api.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "PromptThenRun" &&
                        m.GetParameters()[0].ParameterType == typeof(VisualElement));
                var ps = run.GetParameters();
                var args = new object[ps.Length];
                args[0] = anchor;
                args[1] = placeholder;
                args[2] = ctx;
                for (int i = 3; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                        : ps[i].ParameterType.IsValueType
                            ? Activator.CreateInstance(ps[i].ParameterType) : null;
                run.Invoke(null, args); // returned Task observed by Assistant's own UI
                return true;
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Ask Unity AI failed: " + ex.Message);
                return false;
            }
        }
    }
}
#endif
