import hashlib
import json

def draft_identity(*parts):
    return hashlib.sha256(json.dumps(['draft-v1', *parts], ensure_ascii=False,
                                     separators=(',', ':')).encode('utf-8')).hexdigest()
