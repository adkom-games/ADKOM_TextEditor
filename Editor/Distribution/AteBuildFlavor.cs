#if UNITY_EDITOR
namespace ADKOM.TextEditor
{
    /// <summary>
    /// Asset Store variant. CI copies this over the package's own
    /// Editor/Distribution/AteBuildFlavor.cs when it builds the upm-store
    /// branch; flipping AssetStore to true switches every guarded call site
    /// to the behaviour the submission guidelines require.
    /// </summary>
    internal static class AteBuildFlavor
    {
        internal static readonly bool AssetStore = true;

        internal static readonly string UpdateChannel = "the Asset Store";
    }
}
#endif
