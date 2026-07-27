#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>
    /// Addon security: static scan of addon SOURCE against known-dangerous
    /// API patterns, plus the per-addon consent store. NOTHING from the
    /// addons folder is executed (resident OnLoad included) until the user
    /// has approved that addon once; approval is keyed to the file's content
    /// hash, so a changed addon is a new consent decision. The scan is
    /// text-based and deliberately over-warns (matches in comments/strings
    /// still flag) — it is a heads-up for the human, not a sandbox.
    /// This gate applies to ANY source ATE compiles to run: folder addons
    /// today, and any future buffer/scratch execution must call it too.
    /// </summary>
    internal static class AddonSecurity
    {
        internal sealed class Finding
        {
            public string Api;       // the matched pattern, as written
            public int Line;         // 1-based line of first occurrence
            public string Category;
            public string Risk;      // one-sentence explanation for the report
            public bool High;        // high vs. moderate severity
        }

        sealed class Rule
        {
            public string Pattern;   // ordinal substring
            public string Category;
            public string Risk;
            public bool High;
        }

        static readonly Rule[] Rules =
        {
            // Shell / process execution
            new Rule { Pattern = "System.Diagnostics.Process", Category = "Process execution", High = true,
                Risk = "Can start arbitrary programs on this machine with your user rights." },
            new Rule { Pattern = "Process.Start", Category = "Process execution", High = true,
                Risk = "Can start arbitrary programs on this machine with your user rights." },
            // File system destruction / modification
            new Rule { Pattern = "File.Delete", Category = "File deletion", High = true,
                Risk = "Can permanently delete files anywhere your user can write." },
            new Rule { Pattern = "Directory.Delete", Category = "File deletion", High = true,
                Risk = "Can permanently delete whole folders anywhere your user can write." },
            new Rule { Pattern = "FileUtil.DeleteFileOrDirectory", Category = "File deletion", High = true,
                Risk = "Unity helper that permanently deletes files or folders." },
            new Rule { Pattern = "AssetDatabase.DeleteAsset", Category = "Project modification", High = true,
                Risk = "Deletes assets from the open Unity project." },
            new Rule { Pattern = "AssetDatabase.MoveAsset", Category = "Project modification", High = false,
                Risk = "Moves/renames assets in the open Unity project." },
            new Rule { Pattern = "File.WriteAll", Category = "File writes", High = false,
                Risk = "Writes files outside ATE's normal save path." },
            new Rule { Pattern = "File.Copy", Category = "File writes", High = false,
                Risk = "Copies files anywhere your user can write." },
            new Rule { Pattern = "File.Move", Category = "File writes", High = false,
                Risk = "Moves/renames files anywhere your user can write." },
            // Reading data (exfiltration staging)
            new Rule { Pattern = "File.ReadAll", Category = "File reads", High = false,
                Risk = "Reads file contents — combined with network access this can leak data." },
            new Rule { Pattern = "Directory.GetFiles", Category = "File reads", High = false,
                Risk = "Enumerates files on disk — often a data-harvesting first step." },
            // Network
            new Rule { Pattern = "System.Net", Category = "Network", High = true,
                Risk = "Can talk to the internet: download payloads or upload (exfiltrate) your data." },
            new Rule { Pattern = "HttpClient", Category = "Network", High = true,
                Risk = "Can talk to the internet: download payloads or upload (exfiltrate) your data." },
            new Rule { Pattern = "WebClient", Category = "Network", High = true,
                Risk = "Can talk to the internet: download payloads or upload (exfiltrate) your data." },
            new Rule { Pattern = "UnityWebRequest", Category = "Network", High = true,
                Risk = "Can talk to the internet: download payloads or upload (exfiltrate) your data." },
            new Rule { Pattern = "Sockets", Category = "Network", High = true,
                Risk = "Raw network sockets — arbitrary connections in or out." },
            // Native code / interop
            new Rule { Pattern = "DllImport", Category = "Native code", High = true,
                Risk = "Calls native OS functions directly — full escape from managed code." },
            new Rule { Pattern = "Marshal.", Category = "Native code", High = true,
                Risk = "Unmanaged memory/pointer operations — full escape from managed code." },
            // Dynamic code loading
            new Rule { Pattern = "Assembly.Load", Category = "Dynamic code", High = true,
                Risk = "Loads additional compiled code the scan cannot see." },
            new Rule { Pattern = "Reflection.Emit", Category = "Dynamic code", High = true,
                Risk = "Generates new code at runtime that the scan cannot see." },
            new Rule { Pattern = "CSharpCompilation", Category = "Dynamic code", High = true,
                Risk = "Compiles additional source at runtime that the scan cannot see." },
            // Registry / environment / secrets
            new Rule { Pattern = "Microsoft.Win32.Registry", Category = "System settings", High = true,
                Risk = "Reads or writes the Windows registry." },
            new Rule { Pattern = "Environment.GetEnvironmentVariable", Category = "Secrets", High = false,
                Risk = "Reads environment variables, which often hold tokens and keys." },
            new Rule { Pattern = "Environment.SetEnvironmentVariable", Category = "System settings", High = false,
                Risk = "Alters environment variables for this process." },
            new Rule { Pattern = "Environment.Exit", Category = "Editor control", High = false,
                Risk = "Can kill the Unity Editor process without saving." },
            new Rule { Pattern = "EditorApplication.Exit", Category = "Editor control", High = false,
                Risk = "Can kill the Unity Editor process without saving." },
            // Prefs wipes
            new Rule { Pattern = "EditorPrefs.DeleteAll", Category = "Settings wipe", High = true,
                Risk = "Erases ALL Unity editor preferences on this machine." },
            new Rule { Pattern = "PlayerPrefs.DeleteAll", Category = "Settings wipe", High = false,
                Risk = "Erases this project's player preferences." },
        };

        /// <summary>Scans source text; one finding per rule (first hit's line).</summary>
        internal static List<Finding> Scan(string source)
        {
            var findings = new List<Finding>();
            if (string.IsNullOrEmpty(source)) return findings;
            foreach (var r in Rules)
            {
                int idx = source.IndexOf(r.Pattern, StringComparison.Ordinal);
                if (idx < 0) continue;
                int line = 1;
                for (int i = 0; i < idx; i++) if (source[i] == '\n') line++;
                findings.Add(new Finding
                {
                    Api = r.Pattern, Line = line,
                    Category = r.Category, Risk = r.Risk, High = r.High
                });
            }
            findings.Sort((a, b) => a.High != b.High ? (a.High ? -1 : 1)
                : string.CompareOrdinal(a.Category, b.Category));
            return findings;
        }

        /// <summary>SHA-256 of the source — the identity consent is bound to.</summary>
        internal static string Hash(string source)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(source ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---- Consent store: machine-shared, like the addons folder ----

        static string ConsentFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "AddonConsent.json");

        [Serializable] class ConsentEntry { public string file; public string hash; }
        [Serializable] class ConsentList { public List<ConsentEntry> entries = new List<ConsentEntry>(); }

        static ConsentList _consent;

        static ConsentList Load()
        {
            if (_consent != null) return _consent;
            _consent = new ConsentList();
            try
            {
                if (File.Exists(ConsentFile))
                {
                    var loaded = UnityEngine.JsonUtility.FromJson<ConsentList>(File.ReadAllText(ConsentFile));
                    if (loaded?.entries != null) _consent = loaded;
                }
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Addon consent store unreadable (treating all addons as unapproved): " + ex.Message);
            }
            return _consent;
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConsentFile));
                File.WriteAllText(ConsentFile, UnityEngine.JsonUtility.ToJson(_consent, prettyPrint: true));
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Could not persist addon consent: " + ex.Message);
            }
        }

        /// <summary>Has this exact content of this addon been approved?</summary>
        internal static bool IsApproved(string file, string hash)
        {
            string key = NormPath(file);
            foreach (var e in Load().entries)
                if (string.Equals(e.file, key, StringComparison.OrdinalIgnoreCase) && e.hash == hash)
                    return true;
            return false;
        }

        /// <summary>Records the one-time consent for this addon content.</summary>
        internal static void Approve(string file, string hash)
        {
            string key = NormPath(file);
            var list = Load();
            list.entries.RemoveAll(e => string.Equals(e.file, key, StringComparison.OrdinalIgnoreCase));
            list.entries.Add(new ConsentEntry { file = key, hash = hash });
            Save();
        }

        static string NormPath(string p)
        {
            try { return Path.GetFullPath(p).Replace('\\', '/'); }
            catch { return p; }
        }

        // ---- Risk report ----

        /// <summary>The user-facing summary document (markdown) explaining
        /// what was found and what approval means.</summary>
        internal static string BuildReport(string addonName, string file,
            IReadOnlyList<Finding> findings, string hash)
        {
            var sb = new StringBuilder();
            sb.Append("# Addon Security Review: ").Append(addonName).Append('\n').Append('\n');
            sb.Append("- **File:** `").Append(file).Append("`\n");
            sb.Append("- **Content hash:** `").Append(hash, 0, Math.Min(16, hash.Length)).Append("…`\n");
            sb.Append("- **Scanned:** against ").Append(Rules.Length).Append(" known-dangerous API patterns\n\n");
            if (findings == null || findings.Count == 0)
            {
                sb.Append("## No dangerous APIs detected\n\n");
                sb.Append("The static scan found none of the known-dangerous patterns. ");
                sb.Append("This is a good sign, **not a guarantee** — a static scan cannot ");
                sb.Append("prove code safe. Only approve addons whose source you trust.\n");
            }
            else
            {
                int high = 0;
                foreach (var f in findings) if (f.High) high++;
                sb.Append("## ").Append(findings.Count).Append(" potentially dangerous API")
                  .Append(findings.Count == 1 ? "" : "s").Append(" found");
                if (high > 0) sb.Append(" (").Append(high).Append(" high severity)");
                sb.Append("\n\n| Severity | Category | API | Line | Risk |\n|---|---|---|---|---|\n");
                foreach (var f in findings)
                    sb.Append("| ").Append(f.High ? "**HIGH**" : "moderate")
                      .Append(" | ").Append(f.Category)
                      .Append(" | `").Append(f.Api)
                      .Append("` | ").Append(f.Line)
                      .Append(" | ").Append(f.Risk).Append(" |\n");
                sb.Append("\nMatches are textual: a hit inside a comment or string still ");
                sb.Append("flags. Open the addon file at the listed lines and judge the ");
                sb.Append("actual use before approving.\n");
            }
            sb.Append("\n## What approving means\n\n");
            sb.Append("Approved addons run with FULL Unity Editor privileges — the same ");
            sb.Append("power as any editor script: your files, your network, your machine. ");
            sb.Append("Approval is **one-time per addon content**: if the file changes in ");
            sb.Append("any way, ATE asks again. Never approve an addon whose source you ");
            sb.Append("have not read or whose origin you do not trust.\n");
            return sb.ToString();
        }
    }
}
#endif
