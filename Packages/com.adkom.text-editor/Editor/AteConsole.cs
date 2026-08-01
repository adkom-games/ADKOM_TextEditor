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
    /// here, timestamped. The console pane in the editor window renders it;
    /// Info/Warn/Error also mirror to Unity's console. Thread-safe.
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
            lock (_lock)
            {
                _lines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
                Version++;
            }
        }

        public static void Info(string message) { Debug.Log(message); Log(message); }
        public static void Warn(string message) { Debug.LogWarning(message); Log("[warn] " + message); }
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
