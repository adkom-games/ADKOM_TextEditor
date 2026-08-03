#if UNITY_EDITOR
namespace ADKOM.TextEditor
{
    /// <summary>
    /// Which distribution this copy came from. The GitHub build is the
    /// default; CI rewrites every file in this folder when it generates the
    /// Asset Store branch (upm-store), so the store artifact does not even
    /// contain the code the submission guidelines forbid — package add /
    /// remove and reflection into Editor internals.
    ///
    /// Keep this file to the flags alone: the store override replaces it
    /// wholesale (Tools/store-overrides/Editor/Distribution/).
    /// </summary>
    internal static class AteBuildFlavor
    {
        /// <summary>True only in the Asset Store distribution. Deliberately a
        /// static readonly rather than a const: a const would fold away and
        /// raise CS0162 (unreachable code) at every guarded call site, and the
        /// guidelines require a warning-free package.</summary>
        internal static readonly bool AssetStore = false;

        /// <summary>Where this build's updates come from, for user-facing
        /// messages ("update via …").</summary>
        internal static readonly string UpdateChannel = "GitHub";
    }
}
#endif
