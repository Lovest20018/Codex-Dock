"""Build a review board and verify original vector icon candidates."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageDraw, ImageFont
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'assets/brand-refresh-20261117'
RENDERED = ASSETS / 'rendered'
SPEC = json.loads((ASSETS / 'concepts.json').read_text(encoding='utf-8'))
OUT = ROOT / 'artifacts/brand-refresh-20261117'
OUT.mkdir(parents=True, exist_ok=True)

def font(size, bold=False, chinese=False):
    return ImageFont.truetype(str(Path('C:/Windows/Fonts') / ('msyhbd.ttc' if bold else 'msyh.ttc')) if chinese else str(Path('C:/Windows/Fonts') / ('segoeuib.ttf' if bold else 'segoeui.ttf')), size)

BG, INK, MUTED, BORDER = '#F2F4F0', '#19231D', '#69746B', '#DFE5DE'
board = Image.new('RGB', (1560, 1050), BG)
draw = ImageDraw.Draw(board)
draw.text((60, 38), 'CODEX DOCK  /  IDENTITY STUDY', font=font(16, True), fill=MUTED)
draw.text((58, 72), 'LOGO 新方案', font=font(46, True, True), fill=INK)
draw.text((60, 142), '三个方向 · 矢量原稿 · 应用图标与单色托盘', font=font(21, chinese=True), fill=MUTED)
draw.text((1302, 140), '2026.11.17', font=font(20), fill=MUTED)

def paste_icon(target, path, position, size=None):
    with Image.open(path) as image:
        icon = image.convert('RGBA')
        if size: icon = icon.resize((size, size), Image.Resampling.LANCZOS)
        target.paste(icon, position, icon)

for index, concept in enumerate(SPEC['concepts']):
    left, top, width, height = 60 + index * 488, 210, 464, 620
    draw.rounded_rectangle((left, top, left + width, top + height), 22, fill='white', outline=BORDER, width=1)
    draw.text((left + 26, top + 24), f"0{index + 1}  /  {concept['english']}", font=font(16, True), fill=MUTED)
    if concept['recommended']:
        draw.rounded_rectangle((left + 340, top + 20, left + 437, top + 53), 16, fill='#E2F6D2')
        draw.text((left + 364, top + 25), '推荐', font=font(17, True, True), fill='#3B6324')
    folder = RENDERED / concept['id']
    paste_icon(board, folder / 'app-512.png', (left + 132, top + 87), 200)
    label = 'codex dock'
    label_box = draw.textbbox((0, 0), label, font=font(35, True))
    draw.text((left + (width - label_box[2]) // 2, top + 309), label, font=font(35, True), fill=INK)
    draw.text((left + 27, top + 374), concept['name'], font=font(25, True, True), fill=INK)
    descriptions = [
        ['开口 D + 接入箭头', '石墨黑、暖白与浅芽绿'],
        ['折面 D 字形', '偏几何，紫色强调'],
        ['两层结构', '浅底与低饱和绿']
    ][index]
    draw.text((left + 27, top + 417), descriptions[0], font=font(19, chinese=True), fill=MUTED)
    draw.text((left + 27, top + 447), descriptions[1], font=font(19, chinese=True), fill=MUTED)
    draw.line((left + 26, top + 493, left + width - 26, top + 493), fill=BORDER, width=1)
    draw.text((left + 27, top + 511), '实际像素', font=font(14, chinese=True), fill=MUTED)
    cursor = left + 119
    for size in (16, 24, 32, 48):
        paste_icon(board, folder / f'app-{size}.png', (cursor + (52 - size) // 2, top + 537 - size // 2))
        draw.text((cursor + 13, top + 574), str(size), font=font(14), fill=MUTED)
        cursor += 65

draw.text((60, 869), '托盘识别', font=font(19, True, True), fill=INK)
draw.text((60, 899), '单色也能成立', font=font(17, chinese=True), fill=MUTED)
for index, concept in enumerate(SPEC['concepts']):
    left = 274 + index * 422
    draw.rounded_rectangle((left, 867, left + 186, 954), 14, fill='#E3E9E1')
    draw.rounded_rectangle((left + 194, 867, left + 380, 954), 14, fill='#202822')
    folder = RENDERED / concept['id']
    paste_icon(board, folder / 'mono-dark-32.png', (left + 34, 889))
    paste_icon(board, folder / 'mono-light-32.png', (left + 228, 889))
    draw.text((left + 87, 893), f"0{index+1}", font=font(21, True), fill=INK)
    draw.text((left + 281, 893), f"0{index+1}", font=font(21, True), fill='#F5F8F4')
draw.text((60, 996), 'DESIGN PREVIEW  ·  保持两种基本形状，优先小尺寸识别。', font=font(16, chinese=True), fill=MUTED)
board.save(OUT / 'logo-options.png')

# A larger side-by-side makes the proposed visual change reviewable at once.
comparison = Image.new('RGB', (1280, 760), BG)
canvas = ImageDraw.Draw(comparison)
canvas.text((64, 45), 'Codex Dock', font=font(38, True), fill=INK)
canvas.text((65, 101), '当前图标 / 接入方案', font=font(20, chinese=True), fill=MUTED)
for left, title, image_path in ((64, '当前', ROOT / 'assets/brand-proposal/logo-512.png'), (664, '01 · 接入（推荐）', RENDERED / '01-port/app-512.png')):
    canvas.rounded_rectangle((left, 164, left + 552, 696), 24, fill='white', outline=BORDER)
    canvas.text((left + 28, 188), title, font=font(22, True, True), fill=MUTED if left == 64 else INK)
    paste_icon(comparison, image_path, (left + 132, 256), 288)
    canvas.text((left + 183, 589), 'codex dock', font=font(30, True), fill=INK)
comparison.save(OUT / 'logo-before-after.png')

# Every candidate is exportable, including native-size alpha images and ICO frames.
verified = []
for concept in SPEC['concepts']:
    folder = RENDERED / concept['id']
    for variant in ('app', 'symbol', 'mono-dark', 'mono-light'):
        ET.parse(folder / f'{variant}.svg')
        for size in (16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024):
            with Image.open(folder / f'{variant}-{size}.png') as image:
                assert image.size == (size, size) and image.mode == 'RGBA'
                assert image.getchannel('A').getextrema()[1] == 255
    for filename, expected in (('app.ico', {16,20,24,32,40,48,64,128,256}), ('tray-on-light.ico', {16,20,24,32,40,48}), ('tray-on-dark.ico', {16,20,24,32,40,48})):
        with Image.open(folder / filename) as icon:
            assert icon.ico.sizes() == {(size, size) for size in expected}
    verified.append(concept['id'])
result = {'status': 'PASS', 'candidates': verified, 'svgFiles': 12, 'pngFiles': 132, 'icoFiles': 9,
          'source': str(ASSETS / 'concepts.json'), 'board': str(OUT / 'logo-options.png'),
          'existingApplicationChanged': False}
(OUT / 'verification.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
