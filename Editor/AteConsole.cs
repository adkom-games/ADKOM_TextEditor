#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Session log for the ADKOM Text Editor: every ATE message — tool
    /// output, update checks, semantic setup, status-bar messages — lands
    /// here, timestamped, and is rendered by the console pane in the editor
    /// window. Thread-safe.
    ///
    /// ATE keeps out of Unity's console. A text editor that narrates itself
    /// there buries the messages the user's own project is trying to show
    /// them, so <see cref="Info"/> and <see cref="Warn"/> stay inside ATE.
    /// Only <see cref="Error"/> — something failed and the user has to act —
    /// also reaches Unity's console, because that must be seen even with no
    /// ATE window open.
    /// </summary>
    public static class AteConsole
    {
        const int MaxLines = 2000;

        static readonly object _lock = new object();
        static readonly List<string> _lines = new List<string>();

        /// <summary>Increments on every appended line (cheap change poll).</summary>
        public static int Version { get; private set; }

        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            // A pathological single line breaks the console's row renderer
            // (a failed git command once echoed hundreds of file paths into
            // one entry, garbling the rows) — truncate, don't trust it.
            const int MaxChars = 2000;
            if (message.Length > MaxChars) message = message.Substring(0, MaxChars) + " …";
            lock (_lock)
            {
                _lines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
                Version++;
            }
        }

        /// <summary>Ordinary progress and results. ATE's console only.</summary>
        public static void Info(string message) { Log(message); }

        /// <summary>Something is off but ATE carried on. ATE's console only —
        /// promote to <see cref="Error"/> if the user must see it with ATE
        /// closed.</summary>
        public static void Warn(string message) { Log("[warn] " + message); }

        /// <summary>Something failed and needs the user. The only thing ATE
        /// writes to Unity's console.</summary>
        public static void Error(string message) { Debug.LogError(message); Log("[error] " + message); }

        public static string GetText()
        {
            lock (_lock)
            {
                var sb = new StringBuilder(_lines.Count * 48);
                foreach (var l in _lines) sb.Append(l).Append('\n');
                return sb.ToString();
            }
        }

        /// <summary>Empties the log (the console tab's right-click Clear).</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _lines.Clear();
                Version++;
            }
        }

        /// <summary>Snapshots the lines into <paramref name="into"/> (cleared
        /// first) — the console pane's per-row view binds to this list.</summary>
        public static void CopyLinesInto(List<string> into)
        {
            lock (_lock)
            {
                into.Clear();
                into.AddRange(_lines);
            }
        }
    }
}
#endif
