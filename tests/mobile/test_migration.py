"""Durable legacy receipt upgrade must be lossless and must never dispatch."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src/CodexMobile'))
from vendor_bootstrap import activate
activate()
from migration import migrate_receipts


class MigrationTests(unittest.TestCase):
    def test_unknown_and_accepted_ids_survive_idempotent_upgrade(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            thread = '12345678-1234-1234-1234-123456789abc'
            identifiers = ['12345678-1234-1234-1234-' + f'{i:012x}' for i in range(4)]
            rows = {key: {'threadId': thread, 'kind': kind, 'status': 'unknown' if i % 2 == 0 else 'accepted',
                          'at': i, 'fingerprint': str(i)}
                    for i, (key, kind) in enumerate(zip(identifiers, ('send', 'steer', 'stop', 'respond')))}
            original = json.dumps(rows).encode()
            (root / 'operations.json').write_bytes(original)
            migrate_receipts(root)
            saved = json.loads((root / 'submissions.json').read_text())
            self.assertEqual(len(saved), 4)
            for identifier, row in rows.items():
                entry = saved[thread + ':' + identifier]
                self.assertEqual(entry['status'], row['status'])
                self.assertEqual(entry['legacyKind'], row['kind'])
            migrate_receipts(root)
            self.assertEqual(json.loads((root / 'submissions.json').read_text()), saved)
            self.assertEqual((root / 'operations.json').read_bytes(), original)

    def test_existing_new_receipt_is_authoritative_and_invalid_legacy_fails_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            identifier = '12345678-1234-1234-1234-123456789abc'
            row = {'threadId': identifier, 'kind': 'send', 'status': 'unknown'}
            (root / 'operations.json').write_text(json.dumps({identifier: row}))
            current = {identifier + ':' + identifier: {'text': 'current', 'mode': 'send', 'status': 'accepted'}}
            (root / 'submissions.json').write_text(json.dumps(current))
            migrate_receipts(root)
            self.assertEqual(json.loads((root / 'submissions.json').read_text()), current)
            (root / 'dock-receipts-v1.json').unlink()
            (root / 'operations.json').write_text('{"invalid": {}}')
            with self.assertRaises(ValueError):
                migrate_receipts(root)
            self.assertFalse((root / 'dock-receipts-v1.json').exists())


if __name__ == '__main__':
    unittest.main()
