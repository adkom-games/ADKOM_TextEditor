#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>
    /// Addon signing & endorsements (Addon Signing Spec, issue #27).
    /// RSA-2048 / SHA-256 (PKCS#1) author signatures over the SAME content
    /// hash the consent gate computes; endorsements as sidecar .atesig files
    /// (content-pinned or publisher-vouching); TOFU key pinning with
    /// impersonation warnings.
    /// CRYPTO NOTE: the spec proposed ECDSA P-256, but Unity's Mono throws
    /// NotImplementedException on EC key generation and RSA.Create(size)
    /// silently ignores the size (1024). RSACryptoServiceProvider(2048) is
    /// the one path that yields a real key in this runtime.
    /// Signatures prove ORIGIN, never safety — the scanner and one-time
    /// consent remain the floor for signed and unsigned addons alike.
    /// </summary>
    internal static class AddonSigning
    {
        internal const string SidecarExt = ".atesig";
        const string TypeAuthor = "author";
        const string TypeEndorseContent = "endorse-content";
        const string TypeEndorsePublisher = "endorse-publisher";

        // ---- Envelope (flat for JsonUtility) ----

        [Serializable]
        internal class Envelope
        {
            public string type;
            public string signerName;
            public string signerKey;         // base64 "<modulus>.<exponent>"
            public string date;
            public string contentHash;       // author / endorse-content
            public string authorFingerprint; // endorse-publisher
            public string statement;
            public string signature;         // base64 ECDSA-SHA256 over Canonical()

            public string Canonical() => string.Join("\n",
                type ?? "", signerName ?? "", signerKey ?? "", date ?? "",
                contentHash ?? "", authorFingerprint ?? "", statement ?? "");
        }

        internal enum SigStatus { Unsigned, Signed, Tampered, Invalid }
        internal enum PinState { None, New, Known, Impersonation, Distrusted }

        internal sealed class Endorsement
        {
            public string Name, Fingerprint, Statement, Date;
            public bool Publisher;           // vouches for the author key
            public string TargetFp;          // publisher: the vouched-for key
            public PinState Pin;
        }

        internal sealed class SigningInfo
        {
            public SigStatus Status = SigStatus.Unsigned;
            public string AuthorName, AuthorFingerprint, AuthorKey, AuthorDate;
            public PinState AuthorPin = PinState.None;
            public readonly List<Endorsement> Endorsements = new List<Endorsement>();
            public int ContentEndorsements, PublisherEndorsements;
        }

        // ---- Crypto ----

        /// <summary>Short fingerprint of the public key wire form — the
        /// authoritative identity (names are just strings).</summary>
        internal static string Fingerprint(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder();
                for (int i = 0; i < 8; i++)
                {
                    sb.Append(h[i].ToString("x2"));
                    if (i % 2 == 1 && i < 7) sb.Append(' ');
                }
                return sb.ToString();
            }
        }

        /// <summary>Public key wire form: "base64(modulus).base64(exponent)".</summary>
        static string EncodePublic(RSAParameters p) =>
            Convert.ToBase64String(p.Modulus) + "." + Convert.ToBase64String(p.Exponent);

        static RSACryptoServiceProvider ImportPublic(string key)
        {
            int dot = key.IndexOf('.');
            if (dot <= 0) return null;
            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Convert.FromBase64String(key.Substring(0, dot)),
                Exponent = Convert.FromBase64String(key.Substring(dot + 1))
            });
            return rsa;
        }

        internal static bool Verify(Envelope e)
        {
            try
            {
                using (var rsa = ImportPublic(e.signerKey))
                {
                    if (rsa == null) return false;
                    return rsa.VerifyData(Encoding.UTF8.GetBytes(e.Canonical()),
                        Convert.FromBase64String(e.signature),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch { return false; }
        }

        // ---- Local identity (per-user, DPAPI when available) ----

        static string KeysFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "Keys");
        static string IdentityFile => Path.Combine(KeysFolder, "identity.json");

        [Serializable]
        class StoredIdentity
        {
            public string name;
            public string publicKey;   // "base64(modulus).base64(exponent)"
            public string privateBlob; // base64 RSA private XML, DPAPI-wrapped when protectedStorage
            public bool protectedStorage;
        }

        internal static bool HasIdentity => File.Exists(IdentityFile);

        internal static string IdentityName
        {
            get
            {
                var id = LoadIdentity();
                return id?.name;
            }
        }

        internal static string IdentityPublicKey => LoadIdentity()?.publicKey;

        static StoredIdentity LoadIdentity()
        {
            try
            {
                if (!File.Exists(IdentityFile)) return null;
                return UnityEngine.JsonUtility.FromJson<StoredIdentity>(File.ReadAllText(IdentityFile));
            }
            catch { return null; }
        }

        /// <summary>Creates (or replaces) the local signing identity.</summary>
        internal static bool CreateIdentity(string name, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(KeysFolder);
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    var id = new StoredIdentity
                    {
                        name = name,
                        publicKey = EncodePublic(rsa.ExportParameters(false))
                    };
                    byte[] priv = Encoding.UTF8.GetBytes(rsa.ToXmlString(true));
                    byte[] blob = Dpapi(priv, protect: true);
                    id.protectedStorage = blob != null;
                    id.privateBlob = Convert.ToBase64String(blob ?? priv);
                    File.WriteAllText(IdentityFile, UnityEngine.JsonUtility.ToJson(id, true));
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Windows DPAPI via reflection — System.Security.Cryptography
        /// .ProtectedData isn't in Unity's profile. Returns null when
        /// unavailable (then the key is stored plaintext, guarded only by
        /// user-profile file permissions; CreateIdentity records which).</summary>
        static byte[] Dpapi(byte[] data, bool protect)
        {
            try
            {
                var t = Type.GetType("System.Security.Cryptography.ProtectedData, System.Security")
                    ?? Type.GetType("System.Security.Cryptography.ProtectedData, System.Security.Cryptography.ProtectedData");
                if (t == null) return null;
                var m = t.GetMethod(protect ? "Protect" : "Unprotect",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m == null) return null;
                var scopeType = Type.GetType("System.Security.Cryptography.DataProtectionScope, System.Security")
                    ?? Type.GetType("System.Security.Cryptography.DataProtectionScope, System.Security.Cryptography.ProtectedData");
                if (scopeType == null) return null;
                object scope = Enum.Parse(scopeType, "CurrentUser");
                return (byte[])m.Invoke(null, new object[] { data, null, scope });
            }
            catch { return null; }
        }

        static RSACryptoServiceProvider LoadPrivate(out StoredIdentity id, out string error)
        {
            error = null;
            id = LoadIdentity();
            if (id == null) { error = "no signing identity — create one first"; return null; }
            try
            {
                byte[] priv = Convert.FromBase64String(id.privateBlob);
                if (id.protectedStorage)
                {
                    priv = Dpapi(priv, protect: false);
                    if (priv == null) { error = "protected key could not be read on this machine"; return null; }
                }
                var rsa = new RSACryptoServiceProvider();
                rsa.FromXmlString(Encoding.UTF8.GetString(priv));
                return rsa;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        internal static Envelope Sign(string type, string contentHash, string authorFp,
            string statement, out string error)
        {
            using (var rsa = LoadPrivate(out var id, out error))
            {
                if (rsa == null) return null;
                var e = new Envelope
                {
                    type = type,
                    signerName = id.name,
                    signerKey = id.publicKey,
                    date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    contentHash = contentHash ?? "",
                    authorFingerprint = authorFp ?? "",
                    statement = statement ?? ""
                };
                e.signature = Convert.ToBase64String(
                    rsa.SignData(Encoding.UTF8.GetBytes(e.Canonical()),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                return e;
            }
        }

        internal static Envelope SignAuthor(string contentHash, out string error) =>
            Sign(TypeAuthor, contentHash, null, null, out error);
        internal static Envelope SignEndorseContent(string contentHash, out string error) =>
            Sign(TypeEndorseContent, contentHash, null, null, out error);
        internal static Envelope SignEndorsePublisher(string authorFp, out string error) =>
            Sign(TypeEndorsePublisher, null, authorFp, null, out error);

        // ---- Portable identity backup (export / import) ----
        //
        // identity.json is DPAPI-wrapped to THIS Windows user on THIS machine,
        // so copying it elsewhere is useless. An export re-wraps the private
        // key under a passphrase (PBKDF2-SHA256 → AES-256-CBC, HMAC-SHA256
        // over the ciphertext) so it restores on any machine — and is safe to
        // store off-machine. Losing the key means republishing a new
        // fingerprint, so back this up.

        internal const string ExportExt = ".ateid";
        const string ExportFormat = "ate-identity-1";
        // Mono's PBKDF2-SHA256 runs ~24us/iteration here (measured), so the
        // count is a UX/strength tradeoff on a rare, deliberate operation:
        // 100k ≈ 2.5s of editor freeze per backup/restore. The file also
        // isn't an online oracle — an attacker needs the backup itself.
        // Readers of a backup honor the file's stored iteration count, so
        // this can be raised later without breaking old backups.
        const int Pbkdf2Iterations = 100000;

        [Serializable]
        class ExportFile
        {
            public string format;
            public string name;
            public string publicKey;
            public string salt, iv, cipher, mac;
            public int iterations;
        }

        /// <summary>One PBKDF2 block (a second 32-byte block would cost the
        /// full iteration count again), split into independent enc/mac keys.</summary>
        static void DeriveKeys(string passphrase, byte[] salt, int iters,
            out byte[] encKey, out byte[] macKey)
        {
            byte[] dk;
            using (var kdf = new Rfc2898DeriveBytes(passphrase, salt, iters, HashAlgorithmName.SHA256))
                dk = kdf.GetBytes(32);
            using (var sha = SHA256.Create())
            {
                encKey = sha.ComputeHash(Concat(dk, Encoding.UTF8.GetBytes("ate-enc")));
                macKey = sha.ComputeHash(Concat(dk, Encoding.UTF8.GetBytes("ate-mac")));
            }
        }

        internal static bool ExportIdentity(string path, string passphrase, out string error)
        {
            error = null;
            var id = LoadIdentity();
            if (id == null) { error = "no signing identity to export"; return false; }
            if (string.IsNullOrEmpty(passphrase)) { error = "a passphrase is required"; return false; }
            try
            {
                byte[] priv = Convert.FromBase64String(id.privateBlob);
                if (id.protectedStorage)
                {
                    priv = Dpapi(priv, protect: false);
                    if (priv == null) { error = "the protected key could not be read on this machine"; return false; }
                }
                var salt = RandomBytes(16);
                var iv = RandomBytes(16);
                DeriveKeys(passphrase, salt, Pbkdf2Iterations, out var encKey, out var macKey);
                byte[] cipher;
                using (var aes = Aes.Create())
                {
                    aes.Key = encKey; aes.IV = iv;
                    aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var enc = aes.CreateEncryptor())
                        cipher = enc.TransformFinalBlock(priv, 0, priv.Length);
                }
                byte[] mac;
                using (var h = new HMACSHA256(macKey))
                    mac = h.ComputeHash(Concat(iv, cipher));
                var file = new ExportFile
                {
                    format = ExportFormat, name = id.name, publicKey = id.publicKey,
                    salt = Convert.ToBase64String(salt), iv = Convert.ToBase64String(iv),
                    cipher = Convert.ToBase64String(cipher), mac = Convert.ToBase64String(mac),
                    iterations = Pbkdf2Iterations
                };
                File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(file, true));
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Restores an exported identity on this machine (re-wrapping
        /// the private key with local protection). Replaces any current
        /// identity — callers confirm first.</summary>
        internal static bool ImportIdentity(string path, string passphrase,
            out string name, out string fingerprint, out string error)
        {
            error = null; name = null; fingerprint = null;
            try
            {
                var file = UnityEngine.JsonUtility.FromJson<ExportFile>(File.ReadAllText(path));
                if (file == null || file.format != ExportFormat)
                { error = "not an ATE identity backup"; return false; }
                var salt = Convert.FromBase64String(file.salt);
                var iv = Convert.FromBase64String(file.iv);
                var cipher = Convert.FromBase64String(file.cipher);
                DeriveKeys(passphrase ?? "", salt,
                    file.iterations > 0 ? file.iterations : Pbkdf2Iterations,
                    out var encKey, out var macKey);
                using (var h = new HMACSHA256(macKey))
                {
                    var mac = h.ComputeHash(Concat(iv, cipher));
                    if (Convert.ToBase64String(mac) != file.mac)
                    { error = "wrong passphrase (or the backup file is damaged)"; return false; }
                }
                byte[] priv;
                using (var aes = Aes.Create())
                {
                    aes.Key = encKey; aes.IV = iv;
                    aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var dec = aes.CreateDecryptor())
                        priv = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                }
                // Sanity: the private key must match the advertised public key.
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Encoding.UTF8.GetString(priv));
                    if (EncodePublic(rsa.ExportParameters(false)) != file.publicKey)
                    { error = "backup is inconsistent (key pair mismatch)"; return false; }
                }
                Directory.CreateDirectory(KeysFolder);
                var wrapped = Dpapi(priv, protect: true);
                var id = new StoredIdentity
                {
                    name = file.name, publicKey = file.publicKey,
                    protectedStorage = wrapped != null,
                    privateBlob = Convert.ToBase64String(wrapped ?? priv)
                };
                File.WriteAllText(IdentityFile, UnityEngine.JsonUtility.ToJson(id, true));
                name = id.name;
                fingerprint = Fingerprint(id.publicKey);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        // ---- Trusted keys (TOFU pins + distrust) ----

        static string TrustFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "TrustedKeys.json");

        [Serializable] class TrustEntry { public string name; public string fingerprint; public string firstSeen; public int approvals; public bool distrusted; }
        [Serializable] class TrustList { public List<TrustEntry> entries = new List<TrustEntry>(); }

        static TrustList _trust;

        static TrustList Trust()
        {
            if (_trust != null) return _trust;
            _trust = new TrustList();
            try
            {
                if (File.Exists(TrustFile))
                {
                    var t = UnityEngine.JsonUtility.FromJson<TrustList>(File.ReadAllText(TrustFile));
                    if (t?.entries != null) _trust = t;
                }
            }
            catch { }
            return _trust;
        }

        static void SaveTrust()
        {
            try { File.WriteAllText(TrustFile, UnityEngine.JsonUtility.ToJson(Trust(), true)); }
            catch { }
        }

        /// <summary>Pin state for a (name, fingerprint) pair. The NAME is the
        /// lookup key: a known name arriving with a different key is the
        /// impersonation case the whole scheme exists to catch.</summary>
        internal static PinState StateOf(string name, string fingerprint)
        {
            foreach (var t in Trust().entries)
            {
                if (t.fingerprint == fingerprint)
                    return t.distrusted ? PinState.Distrusted : PinState.Known;
            }
            foreach (var t in Trust().entries)
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase) && !t.distrusted)
                    return PinState.Impersonation; // name known, key isn't
            return PinState.New;
        }

        internal static void PinApproved(string name, string fingerprint)
        {
            foreach (var t in Trust().entries)
                if (t.fingerprint == fingerprint) { t.approvals++; SaveTrust(); return; }
            Trust().entries.Add(new TrustEntry
            {
                name = name, fingerprint = fingerprint,
                firstSeen = DateTime.UtcNow.ToString("yyyy-MM-dd"), approvals = 1
            });
            SaveTrust();
        }

        internal static void Distrust(string fingerprint, string name)
        {
            foreach (var t in Trust().entries)
                if (t.fingerprint == fingerprint) { t.distrusted = true; SaveTrust(); return; }
            Trust().entries.Add(new TrustEntry
            {
                name = name, fingerprint = fingerprint,
                firstSeen = DateTime.UtcNow.ToString("yyyy-MM-dd"), distrusted = true
            });
            SaveTrust();
        }

        internal static (string firstSeen, int approvals) PinDetails(string fingerprint)
        {
            foreach (var t in Trust().entries)
                if (t.fingerprint == fingerprint) return (t.firstSeen, t.approvals);
            return (null, 0);
        }

        // ---- Sidecar collection + verification for an addon ----

        /// <summary>Collects and verifies every .atesig inside the addon
        /// folder. <paramref name="key"/> is the addon folder path;
        /// <paramref name="contentHash"/> the consent hash.</summary>
        internal static SigningInfo Evaluate(string key, string contentHash)
        {
            var info = new SigningInfo();
            foreach (var path in SidecarsFor(key))
            {
                Envelope e;
                try { e = UnityEngine.JsonUtility.FromJson<Envelope>(File.ReadAllText(path)); }
                catch { continue; }
                if (e == null || string.IsNullOrEmpty(e.type)) continue;
                bool valid = Verify(e);
                string fp = null;
                try { fp = Fingerprint(e.signerKey); } catch { }
                switch (e.type)
                {
                    case TypeAuthor:
                        if (!valid || e.contentHash != contentHash)
                        {
                            info.Status = SigStatus.Tampered;
                            info.AuthorName = e.signerName;
                            info.AuthorFingerprint = fp;
                        }
                        else if (info.Status != SigStatus.Tampered)
                        {
                            info.Status = SigStatus.Signed;
                            info.AuthorName = e.signerName;
                            info.AuthorFingerprint = fp;
                            info.AuthorKey = e.signerKey;
                            info.AuthorDate = e.date;
                            info.AuthorPin = StateOf(e.signerName, fp);
                        }
                        break;
                    case TypeEndorseContent:
                        if (valid && e.contentHash == contentHash)
                        {
                            info.Endorsements.Add(new Endorsement
                            {
                                Name = e.signerName, Fingerprint = fp,
                                Statement = e.statement, Date = e.date,
                                Publisher = false, Pin = StateOf(e.signerName, fp)
                            });
                            info.ContentEndorsements++;
                        }
                        break;
                    case TypeEndorsePublisher:
                        if (valid)
                            info.Endorsements.Add(new Endorsement
                            {
                                Name = e.signerName, Fingerprint = fp,
                                Statement = e.statement, Date = e.date,
                                Publisher = true, TargetFp = e.authorFingerprint,
                                Pin = StateOf(e.signerName, fp)
                            });
                        break;
                }
            }
            // Publisher endorsements only apply against a VALID author sig.
            if (info.Status == SigStatus.Signed)
            {
                foreach (var en in info.Endorsements)
                    if (en.Publisher && MatchesAuthor(en, info)) info.PublisherEndorsements++;
            }
            info.Endorsements.RemoveAll(en => en.Publisher &&
                (info.Status != SigStatus.Signed || !MatchesAuthor(en, info)));
            return info;
        }

        static bool MatchesAuthor(Endorsement en, SigningInfo info) =>
            en != null && info.AuthorFingerprint != null &&
            en.TargetFp == info.AuthorFingerprint;

        static IEnumerable<string> SidecarsFor(string key)
        {
            if (!Directory.Exists(key)) yield break;
            foreach (var f in Directory.GetFiles(key, "*" + SidecarExt, SearchOption.AllDirectories))
                yield return f;
        }

        /// <summary>Every shipped sample in the package: the first-level
        /// subfolders of Samples~/Addons. Used by the batch signer and by
        /// the release gate that verifies them.</summary>
        internal static List<string> ShippedSampleKeys(out string error)
        {
            error = null;
            var keys = new List<string>();
            string src;
            try
            {
                if (AtePackage.DiskRoot == null) { error = "package not resolved"; return keys; }
                src = Path.Combine(AtePackage.DiskRoot, "Samples~", "Addons");
            }
            catch (Exception ex) { error = ex.Message; return keys; }
            if (!Directory.Exists(src)) { error = "sample addons folder not found: " + src; return keys; }
            foreach (var d in Directory.GetDirectories(src))
            {
                if (Path.GetFileName(d).StartsWith(".", StringComparison.Ordinal)) continue;
                if (Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories).Length > 0) keys.Add(d);
            }
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            return keys;
        }

        /// <summary>The identity hash of the addon folder at <paramref
        /// name="key"/> — the same computation the consent gate uses.</summary>
        internal static string HashOfAddonAt(string key)
        {
            var files = Directory.GetFiles(key, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return AddonSecurity.HashFiles(files, key);
        }

        /// <summary>Signs every shipped sample in the package source, in
        /// place, so the .atesig files can be committed and shipped.</summary>
        internal static bool SignShippedSamples(out int signed, out string error)
        {
            signed = 0;
            var keys = ShippedSampleKeys(out error);
            if (error != null) return false;
            foreach (var key in keys)
            {
                var env = SignAuthor(HashOfAddonAt(key), out error);
                if (env == null) return false;
                WriteSidecar(key, KindAuthor, env);
                signed++;
            }
            return true;
        }

        /// <summary>Release gate: is every shipped sample signed by
        /// <paramref name="expectFingerprint"/> (when given) with a signature
        /// matching its CURRENT content? Returns problems, empty when clean.</summary>
        internal static List<string> VerifyShippedSamples(string expectFingerprint)
        {
            var problems = new List<string>();
            var keys = ShippedSampleKeys(out string error);
            if (error != null) { problems.Add(error); return problems; }
            foreach (var key in keys)
            {
                string name = Path.GetFileName(key);
                var info = Evaluate(key, HashOfAddonAt(key));
                switch (info.Status)
                {
                    case SigStatus.Unsigned:
                        problems.Add(name + ": UNSIGNED (run Tools > Addons > Signing > Sign Shipped Samples)");
                        break;
                    case SigStatus.Tampered:
                        problems.Add(name + ": signature does not match current content — RE-SIGN before releasing");
                        break;
                    default:
                        if (!string.IsNullOrEmpty(expectFingerprint) &&
                            info.AuthorFingerprint != expectFingerprint)
                            problems.Add(name + ": signed by " + info.AuthorFingerprint +
                                ", expected " + expectFingerprint);
                        break;
                }
            }
            return problems;
        }

        internal static void WriteSidecar(string addonKey, string kind, Envelope e)
        {
            string name = kind == TypeAuthor ? "author" + SidecarExt
                : "endorse." + SafeName(e.signerName) + "." + kind + SidecarExt;
            File.WriteAllText(Path.Combine(addonKey, name), UnityEngine.JsonUtility.ToJson(e, true));
        }

        internal const string KindAuthor = TypeAuthor;
        internal const string KindEndorseContent = TypeEndorseContent;
        internal const string KindEndorsePublisher = TypeEndorsePublisher;

        static string SafeName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "x")
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            return sb.Length > 0 ? sb.ToString() : "x";
        }
    }
}
#endif
