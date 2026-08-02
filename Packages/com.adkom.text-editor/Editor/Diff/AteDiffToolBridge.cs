#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Makes ATE available as Unity's Revision Control Diff/Merge tool
    /// (Preferences → External Tools). Unity launches an external process
    /// for diffs and merges, so a tiny generated shim script receives the
    /// invocation, drops a request file under Library/ATE/DiffRequests, and
    /// exits; the running editor polls that folder and opens the request in
    /// an AteDiffWindow. Configuration writes Unity's custom-tool prefs via
    /// InternalEditorUtility (reflection; verified present) and remembers
    /// the previous tool for Restore.
    /// </summary>
    [InitializeOnLoad]
    internal static class AteDiffToolBridge
    {
        const string PrevToolKey = "ATE.Diff.PrevTool";
        static double _nextPoll;

        static AteDiffToolBridge()
        {
            EditorApplication.update += Poll;
        }

        static string AteDir => Path.GetFullPath(Path.Combine("Library", "ATE"));
        static string RequestDir => Path.Combine(AteDir, "DiffRequests");
        internal static string ShimPath => Path.Combine(AteDir,
            Application.platform == RuntimePlatform.WindowsEditor ? "ate-difftool.cmd" : "ate-difftool.sh");

        // ---------- Request polling ----------

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.0;
            string dir = RequestDir;
            if (!Directory.Exists(dir)) return;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.req"); }
            catch (Exception) { return; }
            foreach (var f in files)
            {
                string[] lines;
                try { lines = File.ReadAllLines(f); File.Delete(f); }
                catch (Exception) { continue; }
                if (lines.Length < 3) continue;
                string mode = lines[0].Trim();
                string left = lines.Length > 1 ? lines[1].Trim() : "";
                string right = lines.Length > 2 ? lines[2].Trim() : "";
                string ancestor = lines.Length > 3 ? lines[3].Trim() : "";
                string output = lines.Length > 4 ? lines[4].Trim() : "";
                switch (mode)
                {
                    case "two":
                        AteDiffWindow.OpenFiles(left, right);
                        break;
                    case "three":
                        AteDiffWindow.OpenMerge(left, right, ancestor, "", fromUnity: true);
                        break;
                    case "merge":
                        AteDiffWindow.OpenMerge(left, right, ancestor, output, fromUnity: true);
                        break;
                }
            }
        }

        // ---------- Configuration ----------

        /// <summary>True when Unity's diff/merge tool is currently ATE.</summary>
        internal static bool IsConfigured =>
            EditorPrefs.GetString("kDiffsDefaultApp", "") == "Custom Tool"
            && string.Equals(EditorPrefs.GetString("customDiffToolPath", ""), ShimPath, StringComparison.OrdinalIgnoreCase);

        /// <summary>Writes the shim and points Unity's External Tools
        /// preferences at it. Returns a status message.</summary>
        internal static string Configure()
        {
            try
            {
                WriteShim();
                string prev = EditorPrefs.GetString("kDiffsDefaultApp", "");
                if (prev != "Custom Tool") EditorPrefs.SetString(PrevToolKey, prev);
                string two = "two \"#LEFT\" \"#RIGHT\"";
                string three = "three \"#LEFT\" \"#RIGHT\" \"#ANCESTOR\"";
                string merge = "merge \"#LEFT\" \"#RIGHT\" \"#ANCESTOR\" \"#OUTPUT\"";
                var ieu = typeof(UnityEditorInternal.InternalEditorUtility);
                var set = ieu.GetMethod("SetCustomDiffToolPrefs",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (set != null) set.Invoke(null, new object[] { ShimPath, two, three, merge });
                else
                {
                    EditorPrefs.SetString("customDiffToolPath", ShimPath);
                    EditorPrefs.SetString("twoWayDiffArguments", two);
                    EditorPrefs.SetString("threeWayDiffArguments", three);
                    EditorPrefs.SetString("mergeArguments", merge);
                }
                var data = ieu.GetMethod("SetCustomDiffToolData",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (data != null) data.Invoke(null, new object[] { ShimPath, two, three, merge });
                EditorPrefs.SetString("kDiffsDefaultApp", "Custom Tool");
                return L10n.Tr("ATE is now Unity's Revision Control Diff/Merge tool (Preferences → External Tools).");
            }
            catch (Exception ex)
            {
                return string.Format(L10n.Tr("Could not configure the diff tool: {0}"), ex.Message);
            }
        }

        /// <summary>Restores the previously selected tool, if one was noted.</summary>
        internal static string Restore()
        {
            string prev = EditorPrefs.GetString(PrevToolKey, "");
            if (string.IsNullOrEmpty(prev))
                return L10n.Tr("No previous tool recorded — pick one in Preferences → External Tools.");
            EditorPrefs.SetString("kDiffsDefaultApp", prev);
            return string.Format(L10n.Tr("Restored Unity's diff/merge tool to \"{0}\"."), prev);
        }

        static void WriteShim()
        {
            Directory.CreateDirectory(RequestDir);
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // %~n strips Unity's quoting; echo( prints empty lines for
                // absent args. Write .tmp, then rename so the poller never
                // reads a half-written request.
                File.WriteAllText(ShimPath,
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    "set \"DIR=%~dp0DiffRequests\"\r\n" +
                    "if not exist \"%DIR%\" mkdir \"%DIR%\"\r\n" +
                    "set \"F=%DIR%\\req-%RANDOM%%RANDOM%\"\r\n" +
                    "> \"%F%.tmp\" (\r\n" +
                    "echo(%~1\r\n" +
                    "echo(%~2\r\n" +
                    "echo(%~3\r\n" +
                    "echo(%~4\r\n" +
                    "echo(%~5\r\n" +
                    ")\r\n" +
                    "move /y \"%F%.tmp\" \"%F%.req\" >nul\r\n");
            }
            else
            {
                File.WriteAllText(ShimPath,
                    "#!/bin/sh\n" +
                    "DIR=\"$(dirname \"$0\")/DiffRequests\"\n" +
                    "mkdir -p \"$DIR\"\n" +
                    "F=\"$DIR/req-$$-$(date +%s)\"\n" +
                    "printf '%s\\n' \"$1\" \"$2\" \"$3\" \"$4\" \"$5\" > \"$F.tmp\"\n" +
                    "mv \"$F.tmp\" \"$F.req\"\n");
                MakeExecutable(ShimPath);
            }
        }

        /// <summary>Sets the executable bit (rwxr-xr-x) on macOS/Linux. Uses
        /// File.SetUnixFileMode when the runtime has it (.NET 7+, resolved by
        /// reflection so this compiles on every Unity profile); otherwise
        /// spawns chmod — and a failure is surfaced, not swallowed, because
        /// a non-executable shim would make Unity's diff invocation a silent
        /// no-op.</summary>
        static void MakeExecutable(string path)
        {
            const int Mode755 = 0x1ED; // rwxr-xr-x as UnixFileMode flags
            try
            {
                var modeType = Type.GetType("System.IO.UnixFileMode, System.Runtime")
                            ?? Type.GetType("System.IO.UnixFileMode, mscorlib");
                var set = modeType != null
                    ? typeof(File).GetMethod("SetUnixFileMode", new[] { typeof(string), modeType })
                    : null;
                if (set != null)
                {
                    set.Invoke(null, new object[] { path, Enum.ToObject(modeType, Mode755) });
                    return;
                }
            }
            catch (Exception) { /* fall through to chmod */ }
            try
            {
                var chmod = new System.Diagnostics.ProcessStartInfo("chmod", "755 \"" + path + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(chmod))
                {
                    if (p != null && p.WaitForExit(2000) && p.ExitCode == 0) return;
                }
                AteConsole.Warn("[ADKOM Text Editor] Could not mark the diff shim executable (" + path + ") — Unity's diff/merge invocations will not reach ATE until it is (chmod 755 it manually).");
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Could not mark the diff shim executable (" + path + "): " + ex.Message + " — chmod 755 it manually.");
            }
        }
    }
}
#endif
