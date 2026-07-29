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
        /// license; every other game there is unlicensed and is not offered.</summary>
        public struct Game { public string Title, File, Url; }

        public static readonly Game[] Downloadable =
        {
            new Game { Title = "Zork I", File = "zork1.z3",
                Url = "https://raw.githubusercontent.com/historicalsource/zork1/97b7b3d68c075dd9af7da499c3e9690ada3471fd/COMPILED/zork1.z3" },
            new Game { Title = "Zork II", File = "zork2.z3",
                Url = "https://raw.githubusercontent.com/historicalsource/zork2/3da9661098809788a99cef00f00c865c6c204f96/COMPILED/zork2.z3" },
            new Game { Title = "Zork III", File = "zork3.z3",
                Url = "https://raw.githubusercontent.com/historicalsource/zork3/3ec9ed412b5f3cafe65d83c727d07db1fe4a86a8/COMPILED/zork3.z3" },
        };

        /// <summary>Downloads a game to the story folder if not already there,
        /// and returns the local path (null on failure).</summary>
        public static string EnsureDownloaded(Game g, out string error)
        {
            error = null;
            string dest = Path.Combine(StoryFolder, g.File);
            if (File.Exists(dest) && new FileInfo(dest).Length > 1000) return dest;
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ADKOM-TextEditor");
                    wc.DownloadFile(g.Url, dest);
                }
                if (!File.Exists(dest) || new FileInfo(dest).Length < 1000)
                { error = "download produced no usable file"; return null; }
                return dest;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static byte[] Load(string path, out string error)
        {
            error = null;
            try { return File.ReadAllBytes(path); }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }
}
