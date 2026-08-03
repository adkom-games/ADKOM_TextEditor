#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// The only place in the package that changes the project's package set.
    /// Asset Store submissions must not add, update or remove packages in a
    /// user's project, so CI swaps this file for a refusing stub when it
    /// builds the store branch — callers must check <see cref="Supported"/>
    /// first and fall back to telling the user what to do by hand.
    /// </summary>
    internal static class AtePackageInstaller
    {
        /// <summary>False in the Asset Store build, where both entry points
        /// below refuse without touching the Package Manager.</summary>
        internal static bool Supported => true;

        static AddRequest _add;
        static RemoveRequest _remove;
        static Action<bool, string> _addDone, _removeDone;

        /// <summary>Installs <c>gitUrl#version</c>. A successful install ends
        /// in a domain reload, so <paramref name="completed"/> is only
        /// guaranteed to run on failure.</summary>
        internal static void Install(string gitUrl, string version, Action<bool, string> completed)
        {
            if (_add != null) { completed?.Invoke(false, "an install is already running"); return; }
            _addDone = completed;
            _add = Client.Add(gitUrl + "#" + version);
            EditorApplication.update += MonitorAdd;
        }

        /// <summary>Removes a package by name (used for the obsolete companion
        /// module that would otherwise break compilation).</summary>
        internal static void Remove(string packageName, Action<bool, string> completed)
        {
            if (_remove != null) { completed?.Invoke(false, "a removal is already running"); return; }
            _removeDone = completed;
            _remove = Client.Remove(packageName);
            EditorApplication.update += MonitorRemove;
        }

        static void MonitorAdd()
        {
            if (_add == null || !_add.IsCompleted) return;
            EditorApplication.update -= MonitorAdd;
            bool ok = _add.Status == StatusCode.Success;
            string message = ok ? _add.Result?.version : _add.Error?.message ?? "unknown error";
            var done = _addDone;
            _add = null;
            _addDone = null;
            done?.Invoke(ok, message);
        }

        static void MonitorRemove()
        {
            if (_remove == null || !_remove.IsCompleted) return;
            EditorApplication.update -= MonitorRemove;
            bool ok = _remove.Status == StatusCode.Success;
            string message = ok ? _remove.PackageIdOrName : _remove.Error?.message ?? "unknown error";
            var done = _removeDone;
            _remove = null;
            _removeDone = null;
            done?.Invoke(ok, message);
        }
    }
}
#endif
