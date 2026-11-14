# Launcher update publisher authentication

Launcher updates require a detached `update-manifest.json.sig`: raw RSA-PSS /
SHA-256 signature bytes over the **exact bytes** of `update-manifest.json`,
including its final newline. JSON reformatting after signing invalidates it.
The launcher verifies the signature before parsing the manifest or offering an
update, then retains the existing executable size/SHA-256 checks (including
cached and resumed downloads). Missing keys/signatures and verification failures
fail closed; there is no legacy unsigned fallback.

## Provisioning (release owner)

1. Provision a dedicated RSA key (3072–8192 bits) using a trusted key-generation
   environment. Do not reuse `GAME_PACKAGE_KEY`, R2 credentials, or a GitHub App
   key. Do not put the private key in the repository, launcher, or R2.
2. Set repository variable `LAUNCHER_UPDATE_PUBLIC_KEY` to base64-encoded DER
   SubjectPublicKeyInfo. This is the nonsecret trust anchor. Release builds embed
   it via the `LauncherUpdatePublicKey` MSBuild property; it is never downloaded.
3. Set protected CI secret `LAUNCHER_UPDATE_PRIVATE_KEY` to base64-encoded DER
   PKCS#8 for that key. Restrict signing-workflow changes and secret access
   independently of distribution-storage access. Only the signing step receives
   it. The signing script rejects a private/public key mismatch.
4. Publish with the updated workflow. It signs the final manifest and uploads
   the EXE, detached signature, and manifest to both the R2 and legacy channels.
   Manual publishers must run `tools/r2/Sign-LauncherManifest.ps1` first and place
   the resulting `update-manifest.json.sig` beside the manifest. The shared R2
   manager stages the exact signed manifest bytes, requires the signature before
   any publication writes, includes it in the storage budget, and uploads the EXE,
   signature, then manifest. Its retention, inventory, and multipart safeguards
   remain in effect. It checks signature presence and size, not cryptographic
   validity; use the signing script with the configured key pair.

No production key is supplied by this patch. Unconfigured local builds run but
cannot accept updates; CI publishing fails when the key configuration is absent.
An ephemeral test key is not a production trust anchor.

## Rollout and limits

- Bootstrap the first signature-enforcing launcher through an independently
  trusted distribution path. Already-installed unsigned clients remain vulnerable;
  publishing a signature alone cannot protect them.
- Do not rotate the public key in isolation: existing clients trust the previously
  embedded key. Plan a trusted migration before retiring that key.
- Independently uploaded files can briefly mismatch during publication. Clients
  reject the mismatch and can retry after publication completes.
- This change authenticates the publisher, not freshness. Replay of an old valid
  signed release and explicit local rollback are not prevented. Rollback to a
  pre-fix launcher restores the old vulnerability; retain only trusted backups.
- Local tampering with the installed launcher or its staging directory, and
  compromise of the signing/build authority itself, are outside this storage-only
  attacker boundary.