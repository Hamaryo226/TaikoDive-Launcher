"""Fail-closed, per-channel R2 storage policy (Python standard library only)."""

import argparse
import sys
import re
import json
import hashlib
import shutil
import os
import subprocess
import tempfile
from pathlib import Path
from datetime import datetime, timedelta, timezone
from urllib.parse import urlsplit

BUDGETS = {'assets': 6_000_000_000, 'game': 1_500_000_000, 'launcher': 500_000_000}


class PolicyError(Exception):
    pass


def parse_json(text):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise PolicyError('Duplicate JSON property')
            result[key] = value
        return result
    try:
        result = json.loads(text, object_pairs_hook=unique)
    except (ValueError, TypeError):
        raise PolicyError('Malformed JSON') from None
    if not isinstance(result, dict):
        raise PolicyError('Expected JSON object')
    return result


def manifest_key(channel, manifest):
    if not isinstance(manifest, dict):
        raise PolicyError('Invalid manifest object')
    if (type(manifest.get('size')) is not int or manifest['size'] <= 0
            or not isinstance(manifest.get('sha256'), str)
            or not re.fullmatch('[0-9a-fA-F]{64}', manifest['sha256'])):
        raise PolicyError('Invalid manifest size/hash')
    if channel == 'launcher':
        return 'launcher/TaikoDive.Launcher.exe'
    url = manifest.get('packageUrl')
    if not isinstance(url, str):
        raise PolicyError('Invalid manifest URL')
    parsed = urlsplit(url)
    if (parsed.scheme != 'https' or parsed.netloc != 'dl.hamaryo.dev'
            or parsed.query or parsed.fragment
            or not re.fullmatch(r'/' + channel + r'/[A-Za-z0-9_.-]+', parsed.path)):
        raise PolicyError('Invalid manifest URL')
    return parsed.path[1:]


def validate_inventory(channel, objects):
    if not isinstance(objects, list):
        raise PolicyError('Invalid inventory')
    seen = set()
    for item in objects:
        try:
            key, size = item['Key'], item['Size']
            date = datetime.fromisoformat(item['LastModified'].replace('Z', '+00:00'))
            if (not isinstance(key, str) or not key.startswith(channel + '/')
                    or key in seen or type(size) is not int or size < 0 or date.tzinfo is None):
                raise ValueError()
            seen.add(key)
        except (KeyError, TypeError, ValueError, AttributeError):
            raise PolicyError('Invalid or duplicate inventory object') from None


def plan(channel, objects, current, incoming_bytes, now, *, launcher_repair=False):
    validate_inventory(channel, objects)
    protected = set()
    if current is not None:
        key = manifest_key(channel, current)
        indexed = {item['Key']: item for item in objects}
        # Only validated local Launcher publication may repair an interrupted overwrite.
        # Missing payloads, invalid manifests and maintenance remain fail-closed.
        if (key not in indexed or (indexed[key]['Size'] != current['size']
                                   and not (channel == 'launcher' and launcher_repair))):
            raise PolicyError('Manifest payload missing or size mismatch')
        protected.add(key)
        previous = current.get('_r2PreviousPackageKey')
        if previous is not None:
            if not isinstance(previous, str) or not re.fullmatch(channel + r'/[A-Za-z0-9_.-]+', previous):
                raise PolicyError('Invalid previous payload key')
            if previous not in indexed:
                raise PolicyError('Previous payload missing')
            protected.add(previous)
    current_bytes = sum(item['Size'] for item in objects)
    peak = current_bytes + incoming_bytes
    if peak > BUDGETS[channel]:
        raise PolicyError(f'{channel} budget exceeded: {peak} > {BUDGETS[channel]} bytes')
    prefix = {'game': 'Update', 'assets': 'Assets'}.get(channel)
    pattern = rf'{channel}/TaikoDive_{prefix}_v[0-9]+(?:\.[0-9]+){{1,3}}_win-x64_[0-9a-f]{{7}}\.zip'
    candidates = [item for item in objects if prefix and re.fullmatch(pattern, item['Key'])]
    if current and not current.get('_r2PreviousPackageKey'):
        # Legacy history cannot distinguish a previous release from a failed upload.
        # A successful publication establishes explicit history; until then retain all.
        protected.update(item['Key'] for item in candidates)
    deletions = [item for item in candidates if current and item['Key'] not in protected
                 and now - datetime.fromisoformat(item['LastModified']) > timedelta(hours=48)]
    delete_bytes = sum(item['Size'] for item in deletions)
    return {'channel': channel, 'object_count': len(objects), 'current_bytes': current_bytes,
            'incoming_bytes': incoming_bytes, 'peak_bytes': peak,
            'budget_bytes': BUDGETS[channel], 'delete_keys': sorted(item['Key'] for item in deletions),
            'delete_bytes': delete_bytes, 'retained_bytes': current_bytes - delete_bytes}


MANIFEST_NAMES = {'game': 'game-update-manifest.json', 'assets': 'assets-update-manifest.json',
                  'launcher': 'update-manifest.json'}


class Aws:
    def __init__(self, bucket, command=None, env=None):
        self.bucket = bucket
        self.command = command or ['aws']
        self.env = dict(os.environ if env is None else env)
        for name in ('R2_ACCOUNT_ID', 'R2_ACCESS_KEY_ID', 'R2_SECRET_ACCESS_KEY'):
            if not self.env.get(name, '').strip():
                raise PolicyError(f'{name} is required')
        self.endpoint = f"https://{self.env['R2_ACCOUNT_ID']}.r2.cloudflarestorage.com"
        self.env.update(AWS_ACCESS_KEY_ID=self.env['R2_ACCESS_KEY_ID'],
                        AWS_SECRET_ACCESS_KEY=self.env['R2_SECRET_ACCESS_KEY'],
                        AWS_DEFAULT_REGION='auto', AWS_PAGER='', AWS_CLI_AUTO_PROMPT='off',
                        AWS_REQUEST_CHECKSUM_CALCULATION='when_required',
                        AWS_RESPONSE_CHECKSUM_VALIDATION='when_required')
        self.env.pop('AWS_SESSION_TOKEN', None)

    def call(self, *args):
        result = subprocess.run([*self.command, *args, '--endpoint-url', self.endpoint,
                                 '--output', 'json'], env=self.env, capture_output=True,
                                text=True, encoding='utf-8', timeout=1800)
        if result.returncode:
            # Never include CLI stdout/stderr, endpoint, or credential values in diagnostics.
            raise PolicyError(f'AWS {args[0]} {args[1]} failed (exit {result.returncode})')
        return result.stdout

    def inventory(self, channel, *, maintenance=False):
        # AWS CLI auto-pagination aggregates ALL Contents. Never --max-items/--no-paginate.
        result = parse_json(self.call('s3api', 'list-objects-v2', '--bucket', self.bucket,
                                      '--prefix', channel + '/'))
        if result.get('IsTruncated') or result.get('NextToken') or result.get('NextContinuationToken'):
            raise PolicyError('Incomplete inventory pagination')
        objects = result.get('Contents', [])
        validate_inventory(channel, objects)
        uploads = self.multipart(channel)
        if maintenance:
            return objects, uploads
        if uploads:
            raise PolicyError('Pending multipart storage; run maintenance before retry')
        return objects

    def multipart(self, channel):
        # CLI auto-pagination drops IsTruncated and Uploads for an empty R2 result.
        # Read explicit pages instead so completeness is proven, not guessed.
        uploads = []
        markers = None
        visited = set()
        while True:
            arguments = ['s3api', 'list-multipart-uploads', '--bucket', self.bucket,
                         '--prefix', channel + '/', '--no-paginate']
            if markers is not None:
                arguments += ['--key-marker', markers[0], '--upload-id-marker', markers[1]]
            pending = parse_json(self.call(*arguments))
            if (type(pending.get('IsTruncated')) is not bool
                    or not isinstance(pending.get('Uploads', []), list)
                    or pending.get('NextToken')):
                raise PolicyError('Incomplete or invalid multipart inventory')
            uploads.extend(pending.get('Uploads', []))
            next_markers = (pending.get('NextKeyMarker'), pending.get('NextUploadIdMarker'))
            if not pending['IsTruncated']:
                if any(value not in (None, '') for value in next_markers):
                    raise PolicyError('Unexpected multipart continuation markers')
                break
            key_marker, upload_marker = next_markers
            if (not isinstance(key_marker, str) or not key_marker.startswith(channel + '/')
                    or not isinstance(upload_marker, str) or not upload_marker.strip()
                    or next_markers in visited):
                raise PolicyError('Invalid or repeated multipart continuation markers')
            visited.add(next_markers)
            markers = next_markers
        seen = set()
        for item in uploads:
            try:
                key, upload_id = item['Key'], item['UploadId']
                initiated = datetime.fromisoformat(item['Initiated'].replace('Z', '+00:00'))
                if (not isinstance(key, str) or not key.startswith(channel + '/')
                        or not isinstance(upload_id, str) or not upload_id.strip()
                        or initiated.tzinfo is None or (key, upload_id) in seen):
                    raise ValueError()
                seen.add((key, upload_id))
            except (KeyError, TypeError, ValueError, AttributeError):
                raise PolicyError('Invalid or duplicate multipart upload') from None
        return uploads


    def upload(self, path, key, *, manifest=False, launcher=False, signature=False):
        cache = 'no-cache' if manifest or launcher or signature else 'public, max-age=31536000, immutable'
        content = 'application/json; charset=utf-8' if manifest else (
            'application/vnd.microsoft.portable-executable' if launcher else
            'application/octet-stream' if signature else 'application/zip')
        self.call('s3', 'cp', str(path), f's3://{self.bucket}/{key}', '--cache-control', cache,
                  '--content-type', content, '--only-show-errors')
        result = parse_json(self.call('s3api', 'head-object', '--bucket', self.bucket, '--key', key))
        if result.get('ContentLength') != path.stat().st_size:
            raise PolicyError('Uploaded object size verification failed')

    def get(self, key, destination):
        self.call('s3api', 'get-object', '--bucket', self.bucket, '--key', key, str(destination))


def read_current(channel, aws, objects, directory):
    key = channel + '/' + MANIFEST_NAMES[channel]
    item = next((item for item in objects if item['Key'] == key), None)
    if item is None:
        return None  # Only a complete, validated inventory establishes absence.
    path = directory / 'origin-manifest.json'
    aws.get(key, path)
    if path.stat().st_size != item['Size']:
        raise PolicyError('Origin manifest changed during read')
    return parse_json(path.read_text(encoding='utf-8-sig'))


def stage_local(channel, payload, manifest, directory):
    payload, manifest = Path(payload), Path(manifest)
    manifest_bytes = manifest.read_bytes()
    data = parse_json(manifest_bytes.decode('utf-8-sig'))
    key = manifest_key(channel, data)
    if key != channel + '/' + payload.name or manifest.name != MANIFEST_NAMES[channel]:
        raise PolicyError('Manifest URL/path does not match payload')
    if channel != 'launcher' and data.get('packageFileName') != payload.name:
        raise PolicyError('Manifest filename does not match payload')
    staged = directory / payload.name
    shutil.copyfile(payload, staged)
    with staged.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if data['size'] != staged.stat().st_size or data['sha256'].lower() != digest:
        raise PolicyError('Local payload size/hash mismatch')
    staged_manifest = directory / MANIFEST_NAMES[channel]
    staged_manifest.write_bytes(manifest_bytes)
    if channel == 'launcher':
        source_signature = Path(str(manifest) + '.sig')
        signature = source_signature.read_bytes()
        if not 1 <= len(signature) <= 1024:
            raise PolicyError('Invalid launcher manifest signature size')
        (directory / (MANIFEST_NAMES[channel] + '.sig')).write_bytes(signature)
    return staged, staged_manifest, data


def manage(channel, aws, *, now=None, payload=None, manifest=None, apply=False):
    now = now or datetime.now(timezone.utc)
    with tempfile.TemporaryDirectory() as directory:
        directory = Path(directory)
        if payload is not None:
            payload, manifest, local = stage_local(channel, payload, manifest, directory)
        pending = []
        if payload is None:
            objects, pending = aws.inventory(channel, maintenance=True)
        else:
            objects = aws.inventory(channel)
        current = read_current(channel, aws, objects, directory)
        incoming = 0
        if payload is not None:
            if channel != 'launcher' and any(item['Key'] == channel + '/' + payload.name for item in objects):
                raise PolicyError('Existing immutable payload key; use a new revision/package name')
            if channel != 'launcher':
                local.pop('_r2PreviousPackageKey', None)
                if current is not None:
                    local['_r2PreviousPackageKey'] = manifest_key(channel, current)
                manifest.write_text(json.dumps(local, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
            incoming = payload.stat().st_size + manifest.stat().st_size
            if channel == 'launcher':
                signature = directory / (MANIFEST_NAMES[channel] + '.sig')
                incoming += signature.stat().st_size
        report = plan(channel, objects, current, incoming, now,
                      launcher_repair=channel == 'launcher' and payload is not None)
        report.update(dry_run=not apply, published=False)
        if pending:
            # A recovery run only aborts incomplete uploads, never completed objects.
            prefix = {'game': 'Update', 'assets': 'Assets'}.get(channel)
            pattern = (r'launcher/TaikoDive\.Launcher\.exe' if channel == 'launcher' else
                       rf'{channel}/TaikoDive_{prefix}_v[0-9]+(?:\.[0-9]+){{1,3}}_win-x64_[0-9a-f]{{7}}\.zip')
            eligible = [item for item in pending if re.fullmatch(pattern, item['Key'])
                        and now - datetime.fromisoformat(item['Initiated'].replace('Z', '+00:00'))
                        > timedelta(hours=48)]
            report.update(abort_uploads=eligible, blocked_uploads=[item for item in pending if item not in eligible],
                          aborted_count=0, deleted_count=0,
                          pending_bytes_unknown=True, pending_multipart_count=len(pending),
                          publication_blocked=True)
            if apply and eligible:
                for item in eligible:
                    aws.call('s3api', 'abort-multipart-upload', '--bucket', aws.bucket,
                             '--key', item['Key'], '--upload-id', item['UploadId'])
                remaining = aws.multipart(channel)
                aborted = {(item['Key'], item['UploadId']) for item in eligible}
                if aborted & {(item['Key'], item['UploadId']) for item in remaining}:
                    raise PolicyError('Multipart abort readback failed')
                report.update(aborted_count=len(eligible), blocked_uploads=remaining,
                              pending_multipart_count=len(remaining),
                              publication_blocked=bool(remaining), pending_bytes_unknown=bool(remaining))
            return report
        if apply and payload is not None:
            fresh_objects = aws.inventory(channel)
            fresh_current = read_current(channel, aws, fresh_objects, directory)
            if (sorted(fresh_objects, key=lambda item: item['Key']) != sorted(objects, key=lambda item: item['Key'])
                    or fresh_current != current):
                raise PolicyError('Origin inventory or manifest changed before publication')
            aws.upload(payload, channel + '/' + payload.name, launcher=channel == 'launcher')
            if channel == 'launcher':
                aws.upload(signature, channel + '/' + signature.name, signature=True)
            aws.upload(manifest, channel + '/' + MANIFEST_NAMES[channel], manifest=True)
            readback = directory / 'published-manifest.json'
            aws.get(channel + '/' + MANIFEST_NAMES[channel], readback)
            if readback.read_bytes() != manifest.read_bytes():
                raise PolicyError('Published manifest readback mismatch')
            report['published'] = True
        report['deleted_count'] = 0
        if apply and report['delete_keys']:
            latest_objects = aws.inventory(channel)
            latest = read_current(channel, aws, latest_objects, directory)
            expected = local if payload is not None else current
            if latest != expected:
                raise PolicyError('Origin manifest changed before cleanup')
            safe = plan(channel, latest_objects, latest, 0, now)
            if not set(report['delete_keys']).issubset(safe['delete_keys']):
                raise PolicyError('Cleanup inventory changed')
            for key in report['delete_keys']:
                aws.call('s3', 'rm', f's3://{aws.bucket}/{key}', '--only-show-errors')
            remaining = aws.inventory(channel)
            if set(report['delete_keys']) & {item['Key'] for item in remaining}:
                raise PolicyError('Deletion readback failed')
            report['deleted_count'] = len(report['delete_keys'])
        return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--channel', required=True, choices=BUDGETS)
    parser.add_argument('--bucket', default='taikodive')
    parser.add_argument('--payload', type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--apply', action='store_true', help='Mutate origin; default is read-only dry-run')
    args = parser.parse_args(argv)
    if bool(args.payload) != bool(args.manifest):
        parser.error('--payload and --manifest must be supplied together')
    try:
        report = manage(args.channel, Aws(args.bucket), payload=args.payload, manifest=args.manifest,
                        apply=args.apply)
        print(json.dumps(report, ensure_ascii=True, indent=2))
        return 1 if report.get('publication_blocked') else 0
    except PolicyError as error:
        print(f'R2 policy refused: {error}', file=sys.stderr)
    except (OSError, ValueError, subprocess.SubprocessError):
        # Untrusted CLI/file details may contain secrets; do not echo exception contents.
        print('R2 operation failed: local I/O, invalid data, or AWS execution error', file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main())
