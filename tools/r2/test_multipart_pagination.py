"""Multipart pagination contracts, including an observed R2 empty response."""
import json
import unittest
from r2_manager import Aws


class MultipartPaginationTests(unittest.TestCase):
    def pages(self, responses):
        aws = Aws('taikodive', env={'R2_ACCOUNT_ID': 'test', 'R2_ACCESS_KEY_ID': 'dummy', 'R2_SECRET_ACCESS_KEY': 'dummy'})
        calls = []
        def call(*args):
            calls.append(args)
            response = responses[len(calls) - 1]
            if isinstance(response, Exception):
                raise response
            return json.dumps(response)
        aws.call = call
        return aws, calls

    def test_multiple_pages_preserve_markers_and_aggregate(self):
        upload = {'Key': 'game/TaikoDive_Update_v1.0_win-x64_1234567.zip', 'UploadId': 'id-1', 'Initiated': '2026-01-01T00:00:00Z'}
        first = {'IsTruncated': True, 'Uploads': [upload], 'NextKeyMarker': upload['Key'], 'NextUploadIdMarker': 'id-1'}
        second = {'IsTruncated': False, 'Uploads': [dict(upload, UploadId='id-2')]}
        aws, calls = self.pages([first, second])
        self.assertEqual([u['UploadId'] for u in aws.multipart('game')], ['id-1', 'id-2'])
        self.assertEqual(len(calls), 2)
        self.assertIn('--no-paginate', calls[1])
        self.assertEqual(calls[1][calls[1].index('--key-marker') + 1], upload['Key'])
        self.assertEqual(calls[1][calls[1].index('--upload-id-marker') + 1], 'id-1')

    def test_invalid_or_cycling_pages_never_return_partial_inventory(self):
        from r2_manager import PolicyError
        first = {'IsTruncated': True, 'Uploads': [], 'NextKeyMarker': 'game/key', 'NextUploadIdMarker': 'id'}
        cases = [
            [{}], [{'IsTruncated': None}], [{'IsTruncated': 0}],
            [{'IsTruncated': True}],
            [dict(first, NextKeyMarker='assets/wrong')],
            [dict(first, NextUploadIdMarker='')],
            [dict(first, NextKeyMarker=12)],
            [first, first],
            [first, {'IsTruncated': False, 'NextKeyMarker': 'game/leftover'}],
            [first, PolicyError('Later-page AWS failure')],
        ]
        for responses in cases:
            with self.subTest(responses=responses):
                aws, _ = self.pages(responses)
                with self.assertRaises(PolicyError):
                    aws.multipart('game')

    def test_duplicates_across_pages_are_rejected(self):
        from r2_manager import PolicyError
        upload = {'Key': 'game/key', 'UploadId': 'id', 'Initiated': '2026-01-01T00:00:00Z'}
        aws, _ = self.pages([
            {'IsTruncated': True, 'Uploads': [upload], 'NextKeyMarker': 'game/key', 'NextUploadIdMarker': 'id'},
            {'IsTruncated': False, 'Uploads': [upload]},
        ])
        with self.assertRaises(PolicyError):
            aws.multipart('game')

    def test_real_empty_response_uses_single_page_mode(self):
        aws = Aws('taikodive', env={'R2_ACCOUNT_ID': 'test', 'R2_ACCESS_KEY_ID': 'dummy', 'R2_SECRET_ACCESS_KEY': 'dummy'})
        calls = []
        def call(*args):
            calls.append(args)
            # Observed in read-only Actions run 43583120714, not a guessed response.
            data = ({'Bucket': 'taikodive', 'Prefix': 'launcher/', 'MaxUploads': 1000, 'IsTruncated': False}
                    if '--no-paginate' in args else {'RequestCharged': None, 'Prefix': 'launcher/'})
            return json.dumps(data)
        aws.call = call
        self.assertEqual(aws.multipart('launcher'), [])
        self.assertEqual(len(calls), 1)
        self.assertIn('--no-paginate', calls[0])


if __name__ == '__main__':
    unittest.main()
