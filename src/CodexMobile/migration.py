"""One-way import of Dock's durable receipts; never dispatch during migration."""
import json
import uuid
from pathlib import Path

from bridge.app.lifecycle import private_json


def migrate_receipts(directory):
    directory = Path(directory)
    source = directory / 'operations.json'
    marker = directory / 'dock-receipts-v1.json'
    if not source.exists() or marker.exists():
        return
    rows = json.loads(source.read_text(encoding='utf-8'))
    if not isinstance(rows, dict):
        raise ValueError('旧版操作记录格式无效，请保留数据并检查备份')
    target = directory / 'submissions.json'
    submissions = json.loads(target.read_text(encoding='utf-8')) if target.exists() else {}
    if not isinstance(submissions, dict):
        raise ValueError('消息记录格式无效')
    imported = 0
    for identifier, row in rows.items():
        # Invalid records fail startup instead of silently dropping uncertain writes.
        uuid.UUID(identifier)
        if not isinstance(row, dict) or row.get('status') not in ('unknown', 'accepted'):
            raise ValueError('旧版操作记录格式无效')
        thread = row.get('threadId')
        uuid.UUID(thread)
        kind = row.get('kind')
        if kind not in ('send', 'steer', 'stop', 'respond'):
            raise ValueError('旧版操作记录类型无效')
        # Text was intentionally not stored by the old service. A placeholder
        # preserves the receipt without guessing its payload or resubmitting it.
        key = thread + ':' + identifier
        if key not in submissions:
            submissions[key] = {
                'text': '旧版操作：请核对原聊天结果后忽略此记录。',
                'mode': kind if kind in ('send', 'steer') else 'send',
                'status': row['status'], 'at': row.get('at', 0),
                'legacyKind': kind, 'legacyFingerprint': row.get('fingerprint', ''),
            }
            imported += 1
    private_json(target, submissions)
    # Write the marker last, so an interruption can resume idempotently. The
    # original file remains untouched for rollback and audit.
    private_json(marker, {'version': 1, 'imported': imported})
