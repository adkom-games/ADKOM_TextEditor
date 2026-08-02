# Addon Signing & Endorsements — Spec

Agreed with Cary 2026-07-28. Extends the addon security gate ([[Multi-File Addons Spec]], AddonSecurity) with authenticity. Design discussion recorded in the session of 2026-07-28; key decisions:
- NO curated registry / no ADKOM vetting workload (Cary).
- Signatures don't manufacture trust; **names + key continuity + endorsements** are the trust tool.
- Endorsements are **sidecar files**; both flavors ship: content endorsements (version-pinned) and **publisher endorsements** ("vouch for this author's key", survives versions).

## Goals / non-goals

- Goals: authorship authenticity (key continuity), tamper evidence, community endorsement display, loud labeling of anonymous addons.
- Non-goals: central registry, CA/PKI, network fetching (v1), proving code SAFETY (the scanner + one-time consent remain the floor for everyone, signed or not). Unsigned addons stay legal.

## Identity

- Keypair: ECDSA P-256 (available in the editor's .NET runtime).
- **Fingerprint** = SHA-256 of the public key, displayed short-form (first 8 bytes, grouped: `a3f9 02c1 77de 90ab`).
- A signer identity = display name + public key. The NAME IS JUST A STRING; only the fingerprint is authoritative. UI always shows both.
- Private keys live per-user (`%APPDATA%/ADKOM/TextEditor/Keys/`), DPAPI-protected at rest.

## Sidecar files (all excluded from the addon identity hash)

Extension `.atesig`, JSON envelope, one signature each:

```json
{
  "type": "author | endorse-content | endorse-publisher",
  "signerName": "ADKOM Games",
  "signerKey": "<base64 public key>",
  "date": "2026-07-28",
  "payload": { ... },           // see per-type below
  "signature": "<base64 ECDSA over canonical(payload)>"
}
```

- `author`: payload = { contentHash } — the addon author's signature over the SAME SHA-256 file-set hash AddonSecurity already computes. File: `author.atesig` in the addon folder (single-file addons: `<Name>.cs.author.atesig` beside the file).
- `endorse-content`: payload = { contentHash, statement? } — a third party vouches for THIS EXACT content. Files: `endorse.<anything>.atesig`, any count. Valid for any copy with the same hash; evaporates on any content change (correct: you endorsed what you reviewed).
- `endorse-publisher`: payload = { authorFingerprint, statement? } — a third party vouches for the AUTHOR KEY, not the code. Survives versions and applies to every addon whose valid author signature matches that fingerprint. Distributable anywhere; usable as a sidecar in any of that author's addons.

Sidecars are additive and user-droppable: pasting a found endorsement into the folder upgrades the consent display without re-prompting consent (sidecars are outside the identity hash by design).

## Verification (in AteAddonManager.LoadOne, after scan/hash)

1. Parse all `.atesig` files; malformed ones are reported, ignored.
2. `author`: signature must verify AND payload.contentHash must equal the computed hash. Mismatch = **TAMPERED** (worse than unsigned — red in every surface, extra confirmation to approve).
3. `endorse-content`: verify signature + hash match; list valid ones.
4. `endorse-publisher`: verify signature; applies only when a VALID author signature exists and fingerprints match.
5. **TOFU pinning** (`TrustedKeys.json` beside AddonConsent.json): on first APPROVAL of an addon signed by (name, fingerprint), pin the pair, plus each endorser pair the user has seen. Thereafter:
   - same name + same key → "✓ known since <date>, N addons".
   - same name + DIFFERENT key → **IMPERSONATION WARNING** banner; approval requires typing the addon name (deliberate friction).
   - Local distrust: the user can mark a key distrusted (context menu in the consent flow); distrusted keys render like TAMPERED.

## Consent UI additions

Banner (one line added): `Signed: <name> (a3f9 02c1…) ✓ known` / `UNSIGNED — author unknown` / `⚠ SIGNATURE INVALID — content altered` / `⚠ NAME REUSES A DIFFERENT KEY`. Endorsements line when present: `Endorsed: <n> for this version, <m> vouch for the author`. Report document gains a Signatures section listing each signer, fingerprint, pin status, endorsement type and statement. The Scanner Results tab is unchanged (scan is orthogonal to signing).

## Signing / endorsing workflow (menu, no coding here — spec only)

- Tools → Addons → Signing → **Create Identity…** (name → keypair; exports a shareable public-identity file so others can verify fingerprints out-of-band).
- **Sign Addon…** (author): pick an addon you distribute → writes `author.atesig`. Re-run after every content change.
- **Endorse Addon…**: pick an installed addon → choose flavor (this version / vouch for author) → writes an `endorse.*.atesig` the user can post anywhere (also copied to clipboard as JSON).
- ADKOM signs its shipped samples at release time (one step in the release procedure) — not vetting, just provenance: "the Rogue that shipped with ATE" is distinguishable from a tampered copy.

## Accepted risks (explicit)

- First contact is a leap of faith (SSH model); fingerprints published out-of-band are the remedy for those who care.
- Stolen author key: can sign NEW malicious content, but cannot alter an approved addon silently (content hash re-consent) and cannot beat a distrust mark once caught. No revocation propagation in v1.
- Reputable signers can ship honest bugs — scanner + consent stay.

## Status

- Spec agreed 2026-07-28; IMPLEMENTED same day (issue #27) in `Editor/Scripting/AddonSigning.cs` + `AddonSigningMenu.cs`, wired into AteAddonManager (Entry.Signing, consent banner line, TOFU pinning on approval, distrust action, typed confirmation for tampered/impersonating addons) and AddonSecurity.BuildSignatureSection.
- **Crypto deviation:** RSA-2048/SHA-256 PKCS#1 instead of ECDSA P-256. Unity's Mono throws NotImplementedException on EC key generation, and `RSA.Create(size)` silently ignores the size (yields 1024) — only `new RSACryptoServiceProvider(2048)` produces a real key here. Public key wire form is `base64(modulus).base64(exponent)`; the fingerprint is SHA-256 over that string.
- Private keys: DPAPI-wrapped via reflection when available (Windows), plaintext-at-rest otherwise, recorded per identity.
- Verified live: identity creation, sign/verify, tamper rejection, TOFU New→Known, same-name-different-key → Impersonation, and the full addon lifecycle (unsigned → signed → endorsed ×2 → tampered, endorsements dropping when content changes).
- **Portable backup added 2026-07-28** (Cary: "the identity must be able to be backed up and used on different machines"): Back Up Identity… / Restore Identity from Backup… write and read a `.ateid` file whose private key is re-wrapped under a passphrase — PBKDF2-SHA256 (100k iters, stored in the file so it can be raised later) → one derived block split into AES-256-CBC and HMAC-SHA256 keys; the MAC detects a wrong passphrase, and import re-checks that the private key matches the advertised public key before installing it. Iteration count is a measured tradeoff: Mono runs PBKDF2-SHA256 at ~24us/iteration and EACH 32-byte block costs the full count, hence one block + hash-split (2.4s per operation instead of 4.8s). Verified live: export → wipe identity → wrong passphrase rejected → correct passphrase restores the SAME public key, signs and verifies.
- NOT done (deferred): endorsement drop-box / network fetch (v1 is sidecar-only, by design); revocation propagation; masked passphrase entry (the status-bar prompt shows typing — noted in the prompt text).
