# Addon Signing & Trust

How to sign the addons you distribute, endorse addons from others, and read the trust signals ATE shows before you approve an addon. Everything lives under **Tools → Addons → Signing**.

**What a signature proves — and what it doesn't.** A signature proves the addon's content came from the holder of a specific KEY. It never proves the signer's name is truthful, and it never proves the code is safe: the security scan and the one-time consent gate apply to signed and unsigned addons alike.

## Your signing identity

- **Create Identity…** generates an RSA-2048 key pair, named with your signing name. The private key is stored per user (wrapped with Windows DPAPI where available); the public key's **fingerprint** — a short hex string like `ab12 cd34 …` — is your real identity. Names are just strings; the fingerprint is what people pin.
- **Copy My Public Identity** puts your name, fingerprint, and public key on the clipboard — publish the fingerprint somewhere people can check it (your repository, your site).
- **Back Up Identity…** exports the private key to an `.ateid` file wrapped under a passphrase (PBKDF2 → AES-256), safe to store off-machine; **Restore Identity from Backup…** imports it on any machine. The plain identity file cannot be copied between machines — it is bound to your Windows user. Losing the key means publishing a new fingerprint, so keep a backup.
- Creating a new identity **replaces** the old one: addons signed with the old key keep their old signatures, and people who pinned your old key will see an impersonation warning for the new one.

## Signing what you publish

An addon is a folder; its identity hash covers every `.cs` file (relative path + content, line endings normalized so git checkouts don't break signatures).

- **Sign as Author** (per addon, under Tools → Addons → Signing) writes `author.atesig` into the addon folder. Ship the folder with that file inside.
- **Re-sign after any change** — the signature binds to the exact content; a changed addon shows **SIGNATURE INVALID** until re-signed.

## Endorsing what others publish

Two kinds of endorsement, both written into the addon folder as sidecar files and copied to your clipboard so you can also post them anywhere (an endorsement is valid for every copy of that content):

- **Endorse This Version** — "I reviewed this exact content." Valid only for the addon's current content hash.
- **Vouch for the Author's Key** — "I trust this publisher." Bound to the author's key fingerprint, so it survives the author's future versions. It only counts when the addon carries a valid author signature from that key.

## What you see before approving

The consent banner and the security report show a one-line signing verdict:

- **UNSIGNED — author unknown.** Anyone can distribute an unsigned addon anonymously; judge it by its source.
- **Signed: name (fingerprint)** — with the key's pin state: *first sight* (verify the fingerprint out-of-band if it matters), *known since … with N approvals*, or warnings below.
- **⚠ SIGNATURE INVALID** — the content does not match the author's signature. For installed samples this usually just means your copy is older than the ATE that shipped it — reinstalling the samples is the fix; for anything else, treat it as tampering.
- **⚠ NAME KNOWN WITH A DIFFERENT KEY** — possible impersonation: you have approved this signer *name* before, but with a different key. This is the attack the whole scheme exists to catch — don't approve without checking.

**Approving pins keys** (trust on first use): the author key and every endorser key you approve are remembered, with their names. **Distrust** marks a key bad; distrusted keys warn on every future appearance.

## For package maintainers

- **Sign Shipped Samples** signs every sample folder in the package source (`Addons~`) with your identity, writing the `author.atesig` files to commit and ship.
- **Verify Shipped Samples** is the release gate: it reports any sample that is unsigned, signed with an unexpected key, or whose signature no longer matches the current content.
