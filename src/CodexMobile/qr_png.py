"""Render QR locally with a four-module quiet zone; no network image service."""
import base64
import struct
import zlib
from qrcodegen import QrCode


def pairing_image(url):
    qr = QrCode.encode_text(url, QrCode.Ecc.MEDIUM)
    scale, border = 6, 4
    width = (qr.get_size() + 2 * border) * scale
    rows = bytearray()
    for y in range(width):
        rows.append(0)
        for x in range(width):
            rows.append(0 if qr.get_module(x // scale - border, y // scale - border) else 255)
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, width, 8, 0, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b'')
    return base64.b64encode(png).decode('ascii')
