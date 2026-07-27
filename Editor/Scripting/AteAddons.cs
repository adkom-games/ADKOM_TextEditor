#if UNITY_EDITOR
using System;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>
    /// Declares an ATE addon: a class in a .cs file under the shared addons
    /// folder (%APPDATA%/ADKOM/TextEditor/Addons) that ATE compiles and loads
    /// in every project. The attribute carries the menu identity and the
    /// AteApi version the addon was built against (semantic versioning: the
    /// addon loads when the MAJOR matches and its MINOR is not newer than
    /// this ATE's — old clients keep working across compatible API growth).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class AteAddonAttribute : Attribute
    {
        /// <summary>Menu display name (independent of the file name).</summary>
        public string Name { get; set; }

        /// <summary>Menu category; compared case-insensitively so "Game" and
        /// "game" land in the same submenu.</summary>
        public string Category { get; set; } = "General";

        /// <summary>The AteApi version this addon targets, e.g. "1.0".</summary>
        public string ApiVersion { get; set; } = "1.0";
    }

    /// <summary>An addon invocable from Tools → Addons → Category → Name.</summary>
    public interface IAteAddon
    {
        /// <summary>Invoked when the user picks the addon from the menu.</summary>
        void Run();
    }

    /// <summary>A resident addon: OnLoad runs when addons are (re)loaded —
    /// subscribe to <see cref="AteApi"/> events here. Run is still invocable
    /// from the menu.</summary>
    public interface IAteAddonResident : IAteAddon
    {
        void OnLoad();
    }
}
#endif
