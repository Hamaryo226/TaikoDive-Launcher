"""Offline AWS executable double; never accesses a network or real credentials."""
import json
import os
import sys
from pathlib import Path

path = Path(os.environ['R2_TEST_STATE'])
state = json.loads(path.read_text())
args = sys.argv[1:]
operation = args[1]
state.setdefault('calls', []).append(args)
path.write_text(json.dumps(state))
if (operation == state.get('fail')
        or (operation == 'cp' and args[3] == state.get('fail_cp_target'))):
    print('simulated AWS failure; SECRET_MUST_NOT_LEAK', file=sys.stderr)
    sys.exit(7)
if operation == 'list-objects-v2':
    if state.get('bad_listing'):
        print(state['bad_listing'])
    else:
        print(json.dumps({'Contents': [{k: v for k, v in obj.items() if k != 'Body'}
                                      for obj in state['objects']]}))
elif operation == 'list-multipart-uploads':
    print(state.get('bad_multipart', json.dumps({'IsTruncated': False, 'Uploads': state.get('uploads', [])})))
elif operation == 'abort-multipart-upload':
    key = args[args.index('--key') + 1]
    upload_id = args[args.index('--upload-id') + 1]
    if not state.get('ignore_abort'):
        state['uploads'] = [u for u in state.get('uploads', [])
                            if (u['Key'], u['UploadId']) != (key, upload_id)]
        path.write_text(json.dumps(state))
    print('{}')
elif operation == 'get-object':
    key = args[args.index('--key') + 1]
    obj = next(obj for obj in state['objects'] if obj['Key'] == key)
    # Output filename directly follows --key KEY in the manager's request.
    Path(args[args.index('--key') + 2]).write_bytes(obj['Body'].encode('utf-8'))
    if state.pop('mutate_after_get', False):
        state['objects'].append({'Key': 'game/concurrent.bin', 'Size': 9, 'Body': 'concurrent',
                                 'LastModified': '2026-01-10T00:00:00+00:00'})
        path.write_text(json.dumps(state))
    print('{}')
elif operation == 'cp':
    source, target = args[2:4]
    key = target.split('/', 3)[3]
    body = Path(source).read_bytes().decode('utf-8')
    state['objects'] = [obj for obj in state['objects'] if obj['Key'] != key]
    state['objects'].append({'Key': key, 'Size': len(body.encode()),
                             'LastModified': '2026-01-10T00:00:00+00:00', 'Body': body})
    path.write_text(json.dumps(state))
elif operation == 'head-object':
    key = args[args.index('--key') + 1]
    obj = next(obj for obj in state['objects'] if obj['Key'] == key)
    print(json.dumps({'ContentLength': obj['Size']}))
elif operation == 'rm':
    key = args[2].split('/', 3)[3]
    state['objects'] = [obj for obj in state['objects'] if obj['Key'] != key]
    path.write_text(json.dumps(state))
else:
    raise SystemExit('Unsupported fake AWS operation')
