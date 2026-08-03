#if UNITY_EDITOR
// ATE Z-Machine — story file management: open a local .z3, or download the
// MIT-licensed Zork trilogy from its preservation repo (on the user's action,
// to the user's machine; ATE never redistributes a game).
using System;
using System.IO;
using System.Net;
using UnityEngine;

namespace AteZMachine
{
    public static class ZStory
    {
        public static string StoryFolder
        {
            get
            {
                string p = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ADKOM", "TextEditor", "ZMachine");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        /// <summary>The three MIT-licensed Zork games, each pinned to a
        /// specific commit so the download is reproducible and can't shift.
        /// These are the ONLY Infocom titles in historicalsource with an MIT
        /// license; every other game there is unlicensed and is not offered.
        /// Every URL is derived from Repo/Commit/File so what the download
        /// prompt shows the user can never drift from what is fetched.</summary>
        public struct Game
        {
            public string Title, File, Repo, Commit;
            /// <summary>Exact size at the pinned commit; the download is
            /// rejected if what arrives is a different length.</summary>
            public long Bytes;
            /// <summary>SHA-256 of the file at the pinned commit. Checked after
            /// every download: size alone would not notice a file of the right
            /// length with the wrong contents.</summary>
            public string Sha256;

            public string Url => "https://raw.githubusercontent.com/" + Repo + "/" + Commit + "/COMPILED/" + File;
            /// <summary>Repository page — source, history and the LICENSE.</summary>
            public string RepoUrl => "https://github.com/" + Repo;
            /// <summary>The story file itself on GitHub, at the pinned commit.</summary>
            public string FileUrl => "https://github.com/" + Repo + "/blob/" + Commit + "/COMPILED/" + File;
            public string ShortCommit => Commit.Length > 10 ? Commit.Substring(0, 10) : Commit;
        }

        public static readonly Game[] Downloadable =
        {
            new Game { Title = "Zork I", File = "zork1.z3", Repo = "historicalsource/zork1",
                Commit = "97b7b3d68c075dd9af7da499c3e9690ada3471fd", Bytes = 86838,
                Sha256 = "37084966477dff679282de42974b2077156b1bd68fad92a65d4ea94d8eb64d79" },
            new Game { Title = "Zork II", File = "zork2.z3", Repo = "historicalsource/zork2",
                Commit = "3da9661098809788a99cef00f00c865c6c204f96", Bytes = 92524,
                Sha256 = "3ae7d5558943e9721f3e4b273c8a7faec1a03a604e1ae4ee1cde472c21cb24ac" },
            new Game { Title = "Zork III", File = "zork3.z3", Repo = "historicalsource/zork3",
                Commit = "3ec9ed412b5f3cafe65d83c727d07db1fe4a86a8", Bytes = 87984,
                Sha256 = "b637a242865d059890184164ce8dec28554cc80901dcbf26c740b2d1ed0d4eb8" },
        };

        /// <summary>Lower-case hex SHA-256 of a file, or null if unreadable.</summary>
        public static string Fingerprint(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
            catch { return null; }
        }

        /// <summary>Where a game's story file lives once downloaded.</summary>
        public static string LocalPath(Game g) => Path.Combine(StoryFolder, g.File);

        /// <summary>True when the story file is already on this machine, so no
        /// download (and no download prompt) is needed. Length is compared
        /// against the pinned commit — cheap enough to run while a menu is
        /// built, unlike hashing, and enough to notice a truncated or
        /// substituted file and offer a clean re-download.</summary>
        public static bool IsDownloaded(Game g)
        {
            string p = LocalPath(g);
            if (!File.Exists(p)) return false;
            long len = new FileInfo(p).Length;
            return g.Bytes > 0 ? len == g.Bytes : len > 1000;
        }

        /// <summary>The display title for a story file — the proper game name
        /// (e.g. "Zork I") when the file matches a known game, else the file
        /// name without its extension.</summary>
        public static string TitleForFile(string path)
        {
            string file = Path.GetFileName(path);
            foreach (var g in Downloadable)
                if (string.Equals(g.File, file, System.StringComparison.OrdinalIgnoreCase)) return g.Title;
            return Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>Downloads a game to the story folder if not already there,
        /// and returns the local path (null on failure). Callers must have the
        /// user's consent first — ZStoryDownloadPrompt is the only path to
        /// here for a file that is not already on the machine.</summary>
        public static string EnsureDownloaded(Game g, out string error)
        {
            error = null;
            string dest = LocalPath(g);
            if (IsDownloaded(g)) return dest;
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ADKOM-TextEditor");
                    wc.DownloadFile(g.Url, dest);
                }
                if (!File.Exists(dest))
                { error = "download produced no file"; return null; }

                // The commit is pinned, so both the length and the hash are
                // known exactly. Length catches the common case (a proxy or
                // captive-portal page landing here instead of the story file);
                // the hash catches anything of the right size with the wrong
                // contents. A file failing either check is deleted, never run.
                long got = new FileInfo(dest).Length;
                if (g.Bytes > 0 && got != g.Bytes)
                {
                    error = string.Format("expected {0:n0} bytes, got {1:n0}", g.Bytes, got);
                    Discard(dest);
                    return null;
                }
                if (got < 1000) { error = "download produced no usable file"; Discard(dest); return null; }

                if (!string.IsNullOrEmpty(g.Sha256))
                {
                    string actual = Fingerprint(dest);
                    if (actual == null)
                    {
                        error = "could not read the downloaded file back to verify it";
                        Discard(dest);
                        return null;
                    }
                    if (!string.Equals(actual, g.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "SHA-256 mismatch — expected " + g.Sha256 + ", got " + actual;
                        Discard(dest);
                        return null;
                    }
                }
                return dest;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// <summary>Removes a file that failed verification, so a later run
        /// cannot mistake it for a good download.</summary>
        static void Discard(string path)
        {
            try { File.Delete(path); } catch { /* leave it if it is locked */ }
        }

        public static byte[] Load(string path, out string error)
        {
            error = null;
            try { return File.ReadAllBytes(path); }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }
}

#endif
