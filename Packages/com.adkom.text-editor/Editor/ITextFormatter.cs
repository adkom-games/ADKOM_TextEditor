namespace ADKOM.TextEditor
{
    /// <summary>
    /// Hook for future syntax highlighting. Implementations transform raw text
    /// into rich-text markup for display. The default passthrough does nothing.
    /// </summary>
    public interface ITextFormatter
    {
        /// <summary>Human-readable name, e.g. "Plain Text", "C#", "JSON".</summary>
        string Name { get; }

        /// <summary>Returns display markup for the given source text.</summary>
        string Format(string text);
    }

    public sealed class PlainTextFormatter : ITextFormatter
    {
        public string Name => "Plain Text";
        public string Format(string text) => text;
    }
}
