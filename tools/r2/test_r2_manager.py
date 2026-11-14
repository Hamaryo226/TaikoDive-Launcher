import importlib.util
import hashlib
import json
import os
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

MODULE = Path(__file__).with_name('r2_manager.py')
NOW = datetime(2026, 1, 10, tzinfo=timezone.utc)


def load_manager():
    if not MODULE.exists():
        raise AssertionError('R2 safe planner is not implemented')
    spec = importlib.util.spec_from_file_location('r2_manager', MODULE)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PlannerTests(unittest.TestCase):
    def test_budget_counts_unknown_objects_and_full_replacement(self):
        manager = load_manager()
        self.assertIsNotNone(manager, 'R2 safe planner is not implemented')
        self.assertEqual(sum(manager.BUDGETS.values()), 8_000_000_000)
        objects = [{'Key': 'launcher/unknown.bin', 'Size': 499_999_990,
                    'LastModified': NOW.isoformat()}]
        result = manager.plan('launcher', objects, None, 10, NOW)
        self.assertEqual(result['peak_bytes'], 500_000_000)
        self.assertEqual(result['delete_keys'], [])
        with self.assertRaisesRegex(manager.PolicyError, 'budget'):
            manager.plan('launcher', objects, None, 11, NOW)

    def test_gc_keeps_current_previous_recent_and_unknown(self):
        m = load_manager()
        def obj(name, hours):
            return {'Key': 'game/' + name, 'Size': 5,
                    'LastModified': (NOW - timedelta(hours=hours)).isoformat()}
        names = [f'TaikoDive_Update_v1.0.0_win-x64_{i:07x}.zip' for i in range(5)]
        objects = [obj(names[0], 200), obj(names[1], 150), obj(names[2], 100),
                   obj(names[3], 47), obj(names[4], 48), obj('unknown.zip', 500)]
        current = {'packageUrl': 'https://dl.hamaryo.dev/game/' + names[0],
                   'size': 5, 'sha256': 'a' * 64,
                   '_r2PreviousPackageKey': 'game/' + names[1]}
        result = m.plan('game', objects, current, 0, NOW)
        self.assertEqual(result['delete_keys'], ['game/' + names[2]])
        self.assertEqual(result['delete_bytes'], 5)
        self.assertEqual(result['retained_bytes'], 25)

    def test_legacy_manifest_does_not_guess_previous_from_failed_uploads(self):
        m = load_manager()
        keys = [f'game/TaikoDive_Update_v1.0.0_win-x64_{i:07x}.zip' for i in range(4)]
        objects = [{'Key': key, 'Size': 1, 'LastModified': (NOW - timedelta(days=10)).isoformat()}
                   for key in keys]
        current = {'packageUrl': 'https://dl.hamaryo.dev/' + keys[0], 'size': 1, 'sha256': 'a' * 64}
        self.assertEqual(m.plan('game', objects, current, 0, NOW)['delete_keys'], [])

    def test_launcher_repair_exception_never_applies_to_game_or_assets(self):
        m = load_manager()
        for channel, prefix in (('game', 'Update'), ('assets', 'Assets')):
            with self.subTest(channel=channel):
                key = f'{channel}/TaikoDive_{prefix}_v1.0_win-x64_1234567.zip'
                current = {'packageUrl': 'https://dl.hamaryo.dev/' + key, 'size': 3, 'sha256': 'a' * 64}
                objects = [{'Key': key, 'Size': 4, 'LastModified': NOW.isoformat()}]
                with self.assertRaisesRegex(m.PolicyError, 'size mismatch'):
                    m.plan(channel, objects, current, 10, NOW, launcher_repair=True)

    def test_untrusted_remote_state_fails_closed(self):
        m = load_manager()
        name = 'game/TaikoDive_Update_v1.0.0_win-x64_1234567.zip'
        valid = {'Key': name, 'Size': 5, 'LastModified': NOW.isoformat()}
        manifest = {'packageUrl': 'https://dl.hamaryo.dev/' + name,
                    'sha256': 'a' * 64, 'size': 5}
        cases = [([dict(valid, Size=-1)], manifest),
                 ([dict(valid, Size=True)], manifest),
                 ([dict(valid, LastModified='bad')], manifest),
                 ([dict(valid, LastModified='2026-01-01')], manifest),
                 ([dict(valid, Key='assets/foreign.zip')], manifest),
                 ([valid, valid], manifest),
                 ([valid], dict(manifest, packageUrl='https://evil.test/' + name)),
                 ([valid], dict(manifest, size=6)),
                 ([valid], dict(manifest, sha256='bad')),
                 ([], manifest),
                 ([valid], dict(manifest, _r2PreviousPackageKey='assets/foreign.zip'))]
        for objects, current in cases:
            with self.subTest(objects=objects, current=current):
                with self.assertRaises(m.PolicyError):
                    m.plan('game', objects, current, 0, NOW)


class BoundaryTests(unittest.TestCase):
    def setUp(self):
        self.m = load_manager()
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.state = self.root / 'state.json'
        self.state.write_text(json.dumps({'objects': [], 'calls': []}))

    def aws(self):
        self.assertTrue(hasattr(self.m, 'Aws'), 'AWS boundary is not implemented')
        env = dict(os.environ, R2_TEST_STATE=str(self.state), R2_ACCOUNT_ID='test',
                   R2_ACCESS_KEY_ID='dummy', R2_SECRET_ACCESS_KEY='dummy')
        return self.m.Aws('taikodive', command=[sys.executable, str(MODULE.with_name('fake_aws.py'))], env=env)

    def local_package(self, channel='game'):
        name = 'TaikoDive_Update_v1.0.0_win-x64_7654321.zip' if channel == 'game' else 'TaikoDive.Launcher.exe'
        payload = self.root / name
        payload.write_bytes(b'new payload')
        data = {'size': payload.stat().st_size, 'sha256': hashlib.sha256(payload.read_bytes()).hexdigest()}
        if channel == 'game':
            data['packageUrl'] = 'https://dl.hamaryo.dev/game/' + name
            data['packageFileName'] = name
        manifest = self.root / self.m.MANIFEST_NAMES[channel]
        manifest.write_text(json.dumps(data))
        return payload, manifest

    def seed_history(self):
        names = [f'game/TaikoDive_Update_v1.0.0_win-x64_{i:07x}.zip' for i in range(4)]
        objects = [{'Key': name, 'Size': 3, 'Body': 'old',
                    'LastModified': (NOW - timedelta(hours=100 + i)).isoformat()}
                   for i, name in enumerate(names)]
        current = {'packageUrl': 'https://dl.hamaryo.dev/' + names[0], 'size': 3,
                   'sha256': hashlib.sha256(b'old').hexdigest(), '_r2PreviousPackageKey': names[1]}
        body = json.dumps(current)
        objects.append({'Key': 'game/game-update-manifest.json', 'Size': len(body), 'Body': body,
                        'LastModified': NOW.isoformat()})
        self.state.write_text(json.dumps({'objects': objects}))
        return names

    def test_publish_records_actual_previous_and_maintenance_preserves_it(self):
        names = self.seed_history()
        payload, manifest = self.local_package()
        self.m.manage('game', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        state = json.loads(self.state.read_text())
        published = json.loads(next(o['Body'] for o in state['objects'] if o['Key'].endswith('.json')))
        self.assertEqual(published.get('_r2PreviousPackageKey'), names[0])
        report = self.m.manage('game', self.aws(), apply=True, now=NOW + timedelta(days=3))
        remaining = {o['Key'] for o in json.loads(self.state.read_text())['objects']}
        self.assertIn(names[0], remaining)
        self.assertIn('game/' + payload.name, remaining)
        self.assertNotIn(names[2], remaining)
        self.assertFalse(report['dry_run'])

    def test_local_manifest_must_match_payload_before_any_mutation(self):
        for field, bad in [('size', 999), ('sha256', 'f' * 64),
                           ('packageUrl', 'https://evil.test/game/file.zip'),
                           ('packageFileName', 'wrong.zip')]:
            with self.subTest(field=field):
                self.state.write_text(json.dumps({'objects': []}))
                payload, manifest = self.local_package()
                data = json.loads(manifest.read_text())
                data[field] = bad
                manifest.write_text(json.dumps(data))
                with self.assertRaises(self.m.PolicyError):
                    self.m.manage('game', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
                self.assertFalse(any(c[1] in ('cp', 'rm') for c in json.loads(self.state.read_text()).get('calls', [])))

    def test_first_publish_verifies_payload_before_manifest(self):
        self.assertIn('payload', __import__('inspect').signature(self.m.manage).parameters,
                      'Publish path is not implemented')
        payload, manifest = self.local_package()
        report = self.m.manage('game', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        state = json.loads(self.state.read_text())
        writes = [c for c in state['calls'] if c[1] == 'cp']
        self.assertEqual([c[3] for c in writes], ['s3://taikodive/game/' + payload.name,
                                               's3://taikodive/game/' + manifest.name])
        actions = [c[1] for c in state['calls']]
        self.assertIn('head-object', actions[actions.index('cp') + 1:])
        self.assertEqual(report['published'], True)
        self.assertEqual(len(state['objects']), 2)
        self.assertGreaterEqual(report['incoming_bytes'], payload.stat().st_size + manifest.stat().st_size)

    def test_publish_rechecks_inventory_before_writing(self):
        self.seed_history()
        state = json.loads(self.state.read_text())
        state['mutate_after_get'] = True
        self.state.write_text(json.dumps(state))
        payload, manifest = self.local_package()
        with self.assertRaisesRegex(self.m.PolicyError, 'changed'):
            self.m.manage('game', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        self.assertFalse(any(c[1] in ('cp', 'rm') for c in json.loads(self.state.read_text())['calls']))

    def test_immutable_payload_collision_never_overwrites(self):
        payload, manifest = self.local_package()
        self.state.write_text(json.dumps({'objects': [{'Key': 'game/' + payload.name, 'Size': 3,
                              'Body': 'old', 'LastModified': NOW.isoformat()}]}))
        with self.assertRaisesRegex(self.m.PolicyError, 'immutable'):
            self.m.manage('game', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        self.assertFalse(any(c[1] == 'cp' for c in json.loads(self.state.read_text())['calls']))

    def test_unparseable_existing_manifest_cannot_be_treated_as_absent(self):
        for body in ('null', '{}', '[]', '{"size":1,"size":2}'):
            with self.subTest(body=body):
                self.state.write_text(json.dumps({'objects': [{'Key': 'game/game-update-manifest.json',
                                      'Size': len(body), 'Body': body, 'LastModified': NOW.isoformat()}]}))
                with self.assertRaises(self.m.PolicyError):
                    self.m.manage('game', self.aws(), apply=True, now=NOW)
                self.assertFalse(any(c[1] in ('cp', 'rm') for c in json.loads(self.state.read_text())['calls']))

    def test_publish_budget_failure_has_no_remote_mutation(self):
        payload, manifest = self.local_package()
        self.state.write_text(json.dumps({'objects': [{'Key': 'game/unknown.bin',
                              'Size': self.m.BUDGETS['game'] - 1, 'LastModified': NOW.isoformat()}]}))
        with self.assertRaisesRegex(self.m.PolicyError, 'budget'):
            self.m.manage('game', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        self.assertFalse(any(c[1] in ('cp', 'rm') for c in json.loads(self.state.read_text())['calls']))

    def test_aws_failure_never_publishes_manifest_or_deletes_payload(self):
        for operation in ('list-objects-v2', 'list-multipart-uploads', 'get-object', 'cp', 'head-object'):
            with self.subTest(operation=operation):
                self.seed_history()
                state = json.loads(self.state.read_text())
                original = next(o['Body'] for o in state['objects'] if o['Key'].endswith('.json'))
                state['fail'] = operation
                self.state.write_text(json.dumps(state))
                payload, manifest = self.local_package()
                with self.assertRaisesRegex(self.m.PolicyError, 'failed'):
                    self.m.manage('game', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
                state = json.loads(self.state.read_text())
                self.assertEqual(next(o['Body'] for o in state['objects'] if o['Key'].endswith('.json')), original)
                self.assertFalse(any(c[1] == 'rm' for c in state['calls']))

    def test_failed_delete_reports_failure_and_preserves_current_previous(self):
        names = self.seed_history()
        state = json.loads(self.state.read_text())
        state['fail'] = 'rm'
        self.state.write_text(json.dumps(state))
        with self.assertRaisesRegex(self.m.PolicyError, 'rm failed'):
            self.m.manage('game', self.aws(), apply=True, now=NOW)
        remaining = {o['Key'] for o in json.loads(self.state.read_text())['objects']}
        self.assertTrue(set(names).issubset(remaining))

    def interrupted_launcher(self):
        payload, manifest = self.local_package('launcher')
        old = {'size': 3, 'sha256': hashlib.sha256(b'old').hexdigest()}
        body = json.dumps(old)
        self.state.write_text(json.dumps({'objects': [
            {'Key': 'launcher/TaikoDive.Launcher.exe', 'Size': 3, 'Body': 'old',
             'LastModified': NOW.isoformat()},
            {'Key': 'launcher/update-manifest.json', 'Size': len(body), 'Body': body,
             'LastModified': NOW.isoformat()}],
            'fail_cp_target': 's3://taikodive/launcher/update-manifest.json'}))
        with self.assertRaisesRegex(self.m.PolicyError, 'cp failed'):
            self.m.manage('launcher', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        state = json.loads(self.state.read_text())
        self.assertEqual(next(o['Body'] for o in state['objects'] if o['Key'].endswith('.exe')), 'new payload')
        self.assertEqual(next(o['Body'] for o in state['objects'] if o['Key'].endswith('.json')), body)
        del state['fail_cp_target']
        state['calls'] = []
        self.state.write_text(json.dumps(state))
        return payload, manifest

    def test_launcher_retry_repairs_payload_manifest_size_split(self):
        payload, manifest = self.interrupted_launcher()
        report = self.m.manage('launcher', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        self.assertTrue(report['published'])
        state = json.loads(self.state.read_text())
        remote = json.loads(next(o['Body'] for o in state['objects'] if o['Key'].endswith('.json')))
        self.assertEqual(remote, json.loads(manifest.read_text()))
        self.assertEqual([c[1] for c in state['calls']][-5:], ['cp', 'head-object', 'cp', 'head-object', 'get-object'])
        self.assertEqual(report['peak_bytes'], report['current_bytes'] + report['incoming_bytes'])
        self.assertFalse(any(c[1] == 'rm' for c in state['calls']))

    def test_launcher_retry_guards_remain_fail_closed(self):
        for case in ('hash', 'budget', 'maintenance', 'read', 'json', 'missing'):
            with self.subTest(case=case):
                payload, manifest = self.interrupted_launcher()
                state = json.loads(self.state.read_text())
                if case == 'hash':
                    data = json.loads(manifest.read_text())
                    data['sha256'] = 'f' * 64
                    manifest.write_text(json.dumps(data))
                elif case == 'budget':
                    state['objects'].append({'Key': 'launcher/unknown', 'Size': self.m.BUDGETS['launcher'],
                                             'LastModified': NOW.isoformat()})
                elif case == 'read':
                    state['fail'] = 'get-object'
                elif case == 'json':
                    item = next(o for o in state['objects'] if o['Key'].endswith('.json'))
                    item.update(Body='{bad', Size=4)
                elif case == 'missing':
                    state['objects'] = [o for o in state['objects'] if not o['Key'].endswith('.exe')]
                self.state.write_text(json.dumps(state))
                before = state['objects']
                kwargs = {} if case == 'maintenance' else dict(payload=payload, manifest=manifest)
                with self.assertRaises(self.m.PolicyError):
                    self.m.manage('launcher', self.aws(), apply=True, now=NOW, **kwargs)
                after = json.loads(self.state.read_text())
                self.assertEqual(after['objects'], before)
                self.assertFalse(any(c[1] in ('cp', 'rm', 'abort-multipart-upload') for c in after['calls']))

    def test_stale_multipart_maintenance_plans_then_aborts_only_upload(self):
        for channel, key in (
                ('launcher', 'launcher/TaikoDive.Launcher.exe'),
                ('game', 'game/TaikoDive_Update_v1.0.0_win-x64_1234567.zip'),
                ('assets', 'assets/TaikoDive_Assets_v1.0_win-x64_abcdef0.zip')):
            with self.subTest(channel=channel):
                uploads = [{'Key': key, 'UploadId': 'stale',
                            'Initiated': (NOW - timedelta(hours=49)).isoformat()}]
                objects = [{'Key': channel + '/unknown', 'Size': 1, 'Body': 'x',
                            'LastModified': NOW.isoformat()}]
                self.state.write_text(json.dumps({'objects': objects, 'uploads': uploads}))
                report = self.m.manage(channel, self.aws(), now=NOW)
                self.assertEqual(report['abort_uploads'], uploads)
                self.assertEqual(report['aborted_count'], 0)
                self.assertTrue(report['pending_bytes_unknown'])
                state = json.loads(self.state.read_text())
                self.assertEqual(state['uploads'], uploads)
                self.assertFalse(any(c[1] in ('cp', 'rm', 'abort-multipart-upload') for c in state['calls']))
                report = self.m.manage(channel, self.aws(), apply=True, now=NOW)
                state = json.loads(self.state.read_text())
                self.assertEqual(report['aborted_count'], 1)
                self.assertFalse(report['publication_blocked'])
                self.assertEqual(state['uploads'], [])
                self.assertEqual(state['objects'], objects)
                self.assertEqual(state['calls'][-1][1], 'list-multipart-uploads')
                self.assertEqual([c[1] for c in state['calls'] if c[1] in ('cp', 'rm', 'abort-multipart-upload')],
                                 ['abort-multipart-upload'])

    def test_multipart_recovery_preserves_fresh_unknown_and_reports_cli_refusal(self):
        key = 'game/TaikoDive_Update_v1.0.0_win-x64_1234567.zip'
        stale = {'Key': key, 'UploadId': 'stale', 'Initiated': (NOW - timedelta(hours=49)).isoformat()}
        blocked = [dict(stale, UploadId='boundary', Initiated=(NOW - timedelta(hours=48)).isoformat()),
                   dict(stale, UploadId='young', Initiated=(NOW - timedelta(hours=1)).isoformat()),
                   dict(stale, UploadId='future', Initiated=(NOW + timedelta(hours=1)).isoformat())]
        blocked += [dict(stale, Key=name, UploadId=str(i)) for i, name in enumerate([
            'game/unknown.zip', 'game/game-update-manifest.json',
            'game/nested/TaikoDive_Update_v1.0.0_win-x64_1234567.zip',
            'game/TaikoDive_Update_v1.0.0_win-x64_ABCDEF0.zip'])]
        self.seed_history()
        state = json.loads(self.state.read_text())
        objects = state['objects']
        state['uploads'] = [stale] + blocked
        self.state.write_text(json.dumps(state))
        report = self.m.manage('game', self.aws(), apply=True, now=NOW)
        self.assertEqual(report['aborted_count'], 1)
        self.assertTrue(report['publication_blocked'])
        self.assertEqual(report['blocked_uploads'], blocked)
        state = json.loads(self.state.read_text())
        self.assertEqual(state['uploads'], blocked)
        self.assertEqual(state['objects'], objects)
        # Exercise CLI result mapping with the real manager and offline AWS boundary.
        from unittest.mock import patch
        from contextlib import redirect_stdout
        from io import StringIO
        aws = self.aws()
        # Make all remaining recognized uploads fresh relative to the CLI's clock.
        for item in state['uploads']:
            item['Initiated'] = datetime.now(timezone.utc).isoformat()
        state['calls'] = []
        self.state.write_text(json.dumps(state))
        with patch.object(self.m, 'Aws', return_value=aws), redirect_stdout(StringIO()) as output:
            self.assertEqual(self.m.main(['--channel', 'game', '--apply']), 1)
        self.assertTrue(json.loads(output.getvalue())['publication_blocked'])
        self.assertFalse(any(c[1] in ('cp', 'rm', 'abort-multipart-upload')
                             for c in json.loads(self.state.read_text())['calls']))

    def test_invalid_or_incomplete_multipart_inventory_refuses_all_mutation(self):
        upload = {'Key': 'launcher/TaikoDive.Launcher.exe', 'UploadId': 'stale',
                  'Initiated': (NOW - timedelta(hours=49)).isoformat()}
        listings = ['{}', 'null', 'broken', '{"Uploads":[],"Uploads":[]}',
                    '{"Uploads":null}', '{"Uploads":[],"IsTruncated":null}',
                    '{"Uploads":[],"IsTruncated":0}', '{"Uploads":[],"IsTruncated":true}',
                    '{"Uploads":[],"NextToken":"more"}', '{"Uploads":[],"NextKeyMarker":"more"}',
                    '{"Uploads":[],"NextUploadIdMarker":"more"}', '{"Uploads":[],"NextKeyMarker":0}']
        listings += [json.dumps({'Uploads': [upload, bad]}) for bad in (
            upload, dict(upload, UploadId=''), dict(upload, UploadId=7),
            dict(upload, Key='game/foreign'), dict(upload, Initiated='bad'),
            dict(upload, Initiated='2026-01-01'), {'Key': upload['Key']}, None)]
        for listing in listings:
            with self.subTest(listing=listing):
                self.state.write_text(json.dumps({'objects': [], 'bad_multipart': listing}))
                with self.assertRaises(self.m.PolicyError):
                    self.m.manage('launcher', self.aws(), apply=True, now=NOW)
                self.assertFalse(any(c[1] in ('cp', 'rm', 'abort-multipart-upload')
                                     for c in json.loads(self.state.read_text())['calls']))

    def test_publication_never_aborts_even_eligible_stale_multipart(self):
        payload, manifest = self.local_package('launcher')
        uploads = [{'Key': 'launcher/TaikoDive.Launcher.exe', 'UploadId': 'stale',
                    'Initiated': (NOW - timedelta(hours=49)).isoformat()}]
        for apply in (False, True):
            with self.subTest(apply=apply):
                self.state.write_text(json.dumps({'objects': [], 'uploads': uploads}))
                with self.assertRaisesRegex(self.m.PolicyError, 'multipart'):
                    self.m.manage('launcher', self.aws(), payload=payload, manifest=manifest, apply=apply, now=NOW)
                state = json.loads(self.state.read_text())
                self.assertEqual(state['uploads'], uploads)
                self.assertFalse(any(c[1] in ('cp', 'rm', 'abort-multipart-upload') for c in state['calls']))

    def test_multipart_abort_failure_or_failed_readback_never_touches_objects(self):
        for fault in ({'fail': 'abort-multipart-upload'}, {'ignore_abort': True}):
            with self.subTest(fault=fault):
                self.seed_history()
                state = json.loads(self.state.read_text())
                objects = state['objects']
                state.update(fault)
                state['uploads'] = [{'Key': 'game/TaikoDive_Update_v1.0.0_win-x64_1234567.zip',
                                     'UploadId': 'stale', 'Initiated': (NOW - timedelta(hours=49)).isoformat()}]
                self.state.write_text(json.dumps(state))
                with self.assertRaisesRegex(self.m.PolicyError, 'failed'):
                    self.m.manage('game', self.aws(), apply=True, now=NOW)
                state = json.loads(self.state.read_text())
                self.assertEqual(state['objects'], objects)
                self.assertFalse(any(c[1] in ('cp', 'rm') for c in state['calls']))

    def test_multipart_recovery_never_aborts_launcher_manifest_or_other_names(self):
        for key in ('launcher/update-manifest.json', 'launcher/other.exe',
                    'launcher/nested/TaikoDive.Launcher.exe', 'launcher/TaikoDiveXLauncher.exe',
                    'assets/TaikoDive_Update_v1.0_win-x64_1234567.zip',
                    'assets/TaikoDive_Assets_v1_win-x64_1234567.zip'):
            with self.subTest(key=key):
                channel = key.split('/')[0]
                uploads = [{'Key': key, 'UploadId': 'stale',
                            'Initiated': (NOW - timedelta(hours=49)).isoformat()}]
                self.state.write_text(json.dumps({'objects': [], 'uploads': uploads}))
                report = self.m.manage(channel, self.aws(), apply=True, now=NOW)
                self.assertTrue(report['publication_blocked'])
                self.assertEqual(report['abort_uploads'], [])
                self.assertEqual(report['blocked_uploads'], uploads)
                self.assertFalse(any(c[1] in ('cp', 'rm', 'abort-multipart-upload')
                                     for c in json.loads(self.state.read_text())['calls']))

    def test_explicit_empty_multipart_response_without_uploads_is_supported(self):
        self.state.write_text(json.dumps({'objects': [], 'bad_multipart': '{"IsTruncated":false}'}))
        report = self.m.manage('launcher', self.aws(), now=NOW)
        self.assertEqual(report['object_count'], 0)
        self.assertTrue(report['dry_run'])

    def test_launcher_uses_fixed_url_and_no_cache(self):
        payload, manifest = self.local_package('launcher')
        self.m.manage('launcher', self.aws(), payload=payload, manifest=manifest, apply=True, now=NOW)
        state = json.loads(self.state.read_text())
        writes = [c for c in state['calls'] if c[1] == 'cp']
        self.assertEqual(writes[0][3], 's3://taikodive/launcher/TaikoDive.Launcher.exe')
        self.assertTrue(all(c[c.index('--cache-control') + 1] == 'no-cache' for c in writes))
        self.assertFalse(any(c[1] == 'rm' for c in state['calls']))

    def test_dry_run_with_obsolete_packages_performs_no_deletions(self):
        self.seed_history()
        report = self.m.manage('game', self.aws(), now=NOW)
        self.assertEqual(len(report['delete_keys']), 2)
        self.assertEqual(report['deleted_count'], 0)
        self.assertFalse(any(c[1] in ('cp', 'rm') for c in json.loads(self.state.read_text())['calls']))

    def test_multipart_uploads_block_even_dry_run(self):
        self.state.write_text(json.dumps({'objects': [], 'uploads': [{'Key': 'game/pending.zip'}]}))
        with self.assertRaisesRegex(self.m.PolicyError, 'multipart'):
            self.m.manage('game', self.aws(), now=NOW)

    def test_incomplete_or_malformed_inventory_is_rejected(self):
        for listing in ('{"IsTruncated":true,"Contents":[]}',
                        '{"NextToken":"more","Contents":[]}', '{"Contents":null}',
                        'null', 'broken', '{"Contents":[],"Contents":[]}'):
            with self.subTest(listing=listing):
                self.state.write_text(json.dumps({'objects': [], 'bad_listing': listing}))
                with self.assertRaises(self.m.PolicyError):
                    self.m.manage('game', self.aws(), now=NOW)

    def test_origin_manifest_read_failure_is_not_first_publish(self):
        key = 'game/game-update-manifest.json'
        self.state.write_text(json.dumps({'objects': [{'Key': key, 'Size': 5,
                              'LastModified': NOW.isoformat(), 'Body': 'bad'}], 'fail': 'get-object'}))
        with self.assertRaisesRegex(self.m.PolicyError, 'get-object') as error:
            self.m.manage('game', self.aws(), now=NOW)
        self.assertNotIn('SECRET_MUST_NOT_LEAK', str(error.exception))
        self.assertEqual([c[1] for c in json.loads(self.state.read_text())['calls']],
                         ['list-objects-v2', 'list-multipart-uploads', 'get-object'])

    def test_maintenance_dry_run_reads_inventory_without_mutation(self):
        objects = [{'Key': 'game/unknown-' + str(i), 'Size': 1,
                    'LastModified': NOW.isoformat()} for i in range(1001)]
        self.state.write_text(json.dumps({'objects': objects}))
        self.assertTrue(hasattr(self.m, 'manage'), 'Maintenance is not implemented')
        report = self.m.manage('game', self.aws(), now=NOW)
        self.assertEqual(report['object_count'], 1001)
        self.assertEqual(report['current_bytes'], 1001)
        calls = json.loads(self.state.read_text())['calls']
        self.assertEqual([call[1] for call in calls], ['list-objects-v2', 'list-multipart-uploads'])
        self.assertNotIn('--no-paginate', calls[0])
        self.assertNotIn('--max-items', calls[0])
        self.assertIn('--output', calls[0])
        self.assertTrue(report['dry_run'])


if __name__ == '__main__':
    unittest.main()
