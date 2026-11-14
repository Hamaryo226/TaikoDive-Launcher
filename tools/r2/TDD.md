# Local TDD evidence

Implementation was developed with repeated terminal executions of `python -m unittest discover -s tools/r2 -v`, tests first. These are observed results from the development session, not simulated CI output.

| Vertical behavior slice | Observed RED | Observed GREEN |
| --- | --- | --- |
| Fixed budgets, full incoming bytes, unknown objects counted | Planner-not-implemented assertion | 1 test OK |
| Current/previous/recent/unknown retention | Expected obsolete key, got no deletion | 2 tests OK |
| Invalid inventory and remote manifest fail closed | 11 invalid-state subtests did not raise | 3 tests OK |
| Read-only AWS inventory boundary, 1001 objects | Maintenance-not-implemented assertion | 4 tests OK |
| Existing manifest read failure is not first publish | Expected AWS get failure not raised | 5 tests OK |
| Truncated/malformed/duplicate JSON inventory rejected | 3 assertion failures and 2 parsing errors | 6 tests OK |
| Payload-before-manifest publishing and verification | Publish parameter absent | 7 tests OK |
| Local URL/name/size/SHA-256 validation | 4 mismatch subtests did not raise | 8 tests OK |
| Persist actual previous release; maintenance applies same planner | Previous key absent in published manifest | 9 tests OK |
| Pending multipart guard | Expected refusal not raised | 10 tests OK |
| Immutable target collision refusal | Existing target was overwritten by fake AWS | 11 tests OK |
| Python CLI and PowerShell entry points | Missing wrapper/help and old wrapper validation failure | 13 tests OK |
| JSON null origin manifest cannot imply absence | Null-manifest refusal not raised | 14 tests OK |
| Recheck before payload mutation | Concurrent inventory change was accepted | 15 tests OK |
| Do not infer legacy previous from failed-upload timestamps | Legacy history yielded deletion candidates | 16 tests OK |

Additional regression coverage exercises preflight budget refusal at the fake AWS boundary; LIST/multipart/GET/upload/HEAD failures; delete failure; fixed Launcher URL/cache behavior; and dry-run with actual deletion candidates. The completed local suite reported **21 tests, OK**, including real PowerShell 7 wrapper execution on Windows and subprocess fake-AWS boundary calls.

One test-double defect was found during implementation: Windows text newline conversion changed fake uploaded JSON sizes. The fake now preserves body bytes across get/put so readback tests model object storage rather than text-file conversion.

## Bounded recovery follow-up

Baseline rerun: **21 tests, OK**. Production changes were preceded by these observed red runs:

| Behavior slice | Exact test/filter | Observed RED | Observed GREEN |
| --- | --- | --- | --- |
| Launcher payload upload succeeds, manifest upload fails, valid retry repairs | `-k launcher_retry` / `test_launcher_retry_repairs_payload_manifest_size_split` | Retry raised `Manifest payload missing or size mismatch` after the fake confirmed new EXE and unchanged old manifest | Full suite: 22 tests OK |
| Maintenance dry-run plans stale uploads, apply aborts only those uploads | `-k stale_multipart` / `test_stale_multipart_maintenance_plans_then_aborts_only_upload` | All three channel subtests raised the old pending-multipart refusal | Full suite: 24 tests OK |
| Mixed uploads preserve fresh/unknown state and expose refusal | `-k multipart_recovery` / `test_multipart_recovery_preserves_fresh_unknown_and_reports_cli_refusal` | Missing `blocked_uploads`, then CLI returned 0 instead of 1 | Targeted test OK |
| Malformed/unknown multipart pagination cannot imply empty inventory | `-k multipart` / `test_invalid_or_incomplete_multipart_inventory_refuses_all_mutation` | Four subtests failed to refuse: `{}`, null/zero `IsTruncated`, zero `NextKeyMarker` | Full suite: 28 tests OK |

The fake AWS executable now supports a target-specific `fail_cp_target` fault, so payload success followed by manifest failure is reproduced through real subprocesses, not by constructing a split state alone. It also models abort failure/no-effect and arbitrary multipart responses. Additional guards cover invalid-hash/over-budget retries with no writes; maintenance, missing payload and unreadable/malformed origin refusal; game/assets strictness; the exact 48-hour boundary; unknown/nested/manifest filenames; publisher refusal without aborts; and abort readback failure without completed-object mutation.

Final verification command:

```text
python --version
python -m unittest discover -s tools/r2 -p 'test_*.py' -v
git diff --check -- tools/r2
```

Observed **Python 3.14.9**, **31 tests, OK**, including the PowerShell wrapper test (no skips); scoped diff check passed. CI's Python 3.12 runtime was not exercised locally. The added tests are in `test_r2_manager.py`; wrapper tests remain in `test_entrypoints.py`.

No commit, push, remote AWS call, bucket mutation, or real publish was performed. Live abort permission verification and workflow integration are separate parent-agent responsibilities. The fixed-URL Launcher mismatch window remains; retry recovery does not make the two uploads atomic.
