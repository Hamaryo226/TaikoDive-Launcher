# R2 publisher and maintenance

Requires Python **3.11+**, AWS CLI v2, and PowerShell 7 for the wrappers. CI must install/select Python explicitly (for example `actions/setup-python` with `python-version: '3.12'`). No Python packages are required.

```powershell
# Read-only origin audit; this still consumes LIST/GET requests.
./tools/r2/Maintain-TaikoDiveR2.ps1 -Channel assets
# Apply safe obsolete-package deletions, or scoped stale-multipart recovery.
./tools/r2/Maintain-TaikoDiveR2.ps1 -Channel assets -Apply
# Existing publication interface; -DryRun performs validation/planning without writes.
./tools/r2/Publish-TaikoDiveR2.ps1 -Channel assets -PayloadPath ./package.zip -ManifestPath ./assets-update-manifest.json -DryRun
```

The example package path must be replaced by the actual package named in its manifest. All commands accept `-Bucket` (default `taikodive`). Python equivalent: `python tools/r2/r2_manager.py --channel assets [--apply] [--payload PATH --manifest PATH]`.

Supply `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY` through environment variables only. AWS child-process credentials are derived from these variables; CLI stdout/stderr are not echoed on failure. Required object permissions include list objects, list multipart uploads, get/head, put, delete, and abort multipart uploads. No ListBuckets, lifecycle-management, or other bucket-admin API is used.

## Fixed storage policy

| Prefix | Maximum bytes |
| --- | ---: |
| `assets/` | 6,000,000,000 |
| `game/` | 1,500,000,000 |
| `launcher/` | 500,000,000 |

Total allocation: **8,000,000,000 bytes**, not GiB. Each job only reads/manages its channel. All objects in that prefix, including manifests and unrecognized objects, consume budget. Before any upload or deletion, publishing requires `current object bytes + full incoming payload bytes + full generated manifest bytes <= channel budget`. Replacement does not subtract the old body. Cleanup is not credited in this preflight. Over-budget jobs fail without changing the current manifest, even if a future cleanup could make room. Run maintenance separately while still within budget, or have an operator investigate an already-over-budget channel.

- Object inventory uses full AWS CLI JSON auto-pagination without limiting queries. Multipart inventory uses explicit `--no-paginate` pages with strict `IsTruncated` validation, continuation markers and cycle detection, aggregating every page before any mutation. This preserves the real empty R2 response that CLI auto-pagination otherwise reduces to only metadata. Malformed/duplicate inventory and AWS failures stop the job.
- **Any pending multipart upload stops publication**, including publication dry-run. Pending bytes are unknown, never credited as zero; the tool does not enumerate parts or claim an exact multipart storage total. Malformed/incomplete multipart listings, duplicate upload identities, invalid timestamps/IDs, or listing failure stop all mutation.
- Maintenance alone can recover uploads initiated **strictly more than 48 hours ago**, limited to `launcher/TaikoDive.Launcher.exe` and the exact game/assets package patterns below. No manifest, unknown filename, nested path or other channel upload is aborted. Dry-run returns `abort_uploads` without mutation. Apply aborts only those key/upload-ID pairs, then relists multipart uploads and verifies their absence. It does **not** publish or delete completed objects in that recovery run; a later maintenance run may perform ordinary object cleanup.
- Fresh, exactly-48-hour-old, future-dated and unknown-key uploads are preserved. Reports identify `blocked_uploads`, `pending_multipart_count`, `pending_bytes_unknown`, `aborted_count` and `publication_blocked`. CLI exits nonzero while any pending uploads remain (including read-only plans); abort or readback failures also fail. Eligible stale uploads can still be recovered when fresh/unknown uploads coexist. A stale initiation timestamp does not prove no parts are still being uploaded: use the same serialized channel workflow and never run competing writers.
- Recovery still requires readable/valid origin state and the existing completed-object budget preflight. Over-budget objects or a Launcher payload/manifest size mismatch need the separate remedies below; maintenance does not weaken these guards. No lifecycle or bucket-admin API is called; existing credentials must permit `AbortMultipartUpload`. Live permission support is not verified by offline tests.
- Existing manifests are fetched directly from R2 with AWS `get-object`, never through the public CDN. Only a complete inventory proving manifest absence permits first publication.
- Local size/SHA-256, fixed HTTPS origin `dl.hamaryo.dev`, channel/key, and package filename are validated before any mutation. A private temporary copy is hashed and uploaded. The generated JSON keeps application fields and adds `_r2PreviousPackageKey` for game/assets.
- Payload upload and HEAD size verification precede manifest upload. The manifest is fetched back byte-for-byte. Cleanup runs only afterward. Payload HEAD is a size check, **not** an independent remote SHA-256 rehash.
- Game/assets payload keys are immutable: any existing target key is rejected, even identical retries. After interruption between payload and manifest publication, use a new revision/package key or have an operator inspect the orphan; the tool will not overwrite it.
- Current and explicit previous payloads are retained. The previous key is recorded **in the same atomic manifest object** as the new release, so failed uploads cannot displace previous history. Legacy manifests without this field conservatively retain all packages until a successful publication establishes history; the next maintenance can then prune.
- Only exact `game/TaikoDive_Update_v<2-4 numeric version components>_win-x64_<7 lowercase hex>.zip` or corresponding `assets/TaikoDive_Assets_...zip` names may be deleted. Only files **strictly older than 48 hours** by LastModified are eligible. Unknown files are never deleted, but always count toward budget. No manifest, launcher EXE, nested path, or other channel is garbage-collected.
- Reports include inventory counts/bytes, incoming/peak/budget bytes, proposed deletion keys/bytes, retained inventory bytes, dry-run state, publication state and actual deletion count. `retained_bytes` is the original inventory minus proposed deletions, not a post-publication storage measurement.

## Concurrency and compatibility limits

All publication and maintenance workflows for a channel must share a repository-level concurrency group with `cancel-in-progress: false`. Ensure exactly one repository owns each channel. Do not concurrently run manual publishers, legacy workflows, or other writers. Inventory/manifest rechecks detect observed changes, but are **not a distributed lock or atomic conditional transaction** against uncooperative writers.

The current Launcher updater hardcodes `launcher/TaikoDive.Launcher.exe`; it does not consume a manifest-selected executable URL. That URL remains fixed with `no-cache`. Publishing the executable before its manifest still has a temporary old-manifest/new-executable hash mismatch window. A failed manifest write can prolong it. A subsequent publication with a locally validated size/SHA-256 pair may repair the existing executable/manifest size split; only this Launcher publishing path relaxes the old payload-size comparison. Maintenance, missing payloads, malformed/unreadable origin manifests, game/assets publishing and full peak-budget checks remain strict. Retry uploads and HEAD-verifies the executable before publishing and byte-for-byte reading back the manifest. Previous executable bodies cannot be retained at the same key. Immutable Launcher URLs and updater changes are out of scope; no claim is made that this race is solved.

This is a storage guard, **not a billing cutoff or a zero-billing guarantee**. Other prefixes/buckets/accounts, historical storage usage, request counts, reads from clients, and other billable services are not inventoried. No download proxy or bucket-visibility change is implemented.

## Offline verification

```text
python -m unittest discover -s tools/r2 -p 'test_*.py' -v
```

Tests execute the real planner/orchestrator against a separate local fake AWS executable with dummy environment credentials. PowerShell wrapper tests replace `python` with an argument-capturing function and forbid direct AWS invocation. No remote state is read or changed by these tests. See `TDD.md` for red/green evidence. Windows runs also exercise the PowerShell wrapper tests; hosts without `pwsh` explicitly skip those tests.
