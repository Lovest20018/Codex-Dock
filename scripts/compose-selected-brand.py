"""Review the selected cross-platform logo with actual compiled Android renders."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont

root=Path(__file__).resolve().parents[1]
brand=root/'assets/brand'
out=root/'artifacts/brand-applied-20261117'
android=out/'android-icons-verified/renders'
out.mkdir(parents=True,exist_ok=True)
def font(size,bold=False):return ImageFont.truetype(str(Path('C:/Windows/Fonts')/('msyhbd.ttc' if bold else 'msyh.ttc')),size)
def paste(canvas,path,xy,size):
    with Image.open(path) as source:
        icon=source.convert('RGBA').resize((size,size),Image.Resampling.LANCZOS)
        canvas.paste(icon,xy,icon)
canvas=Image.new('RGB',(1360,900),'#F5F6F2')
draw=ImageDraw.Draw(canvas)
draw.text((60,38),'CODEX DOCK  /  LOVEST CODE',font=font(17,True),fill='#68776D')
draw.text((58,82),'接入 × 浅绿',font=font(48,True),fill='#23453C')
draw.text((60,159),'01 的形状 + 03 的配色 · Windows 与安卓统一',font=font(22),fill='#68776D')
for left,title in ((60,'Windows'),(700,'Android')):
    draw.rounded_rectangle((left,225,left+600,690),22,fill='white',outline='#E0E6DE',width=1)
    draw.text((left+28,245),title,font=font(21,True),fill='#23453C')
paste(canvas,brand/'logo-512.png',(232,312),256)
paste(canvas,android/'android-native-adaptive.png',(872,312),256)
draw.text((207,612),'应用、窗口与托盘',font=font(20),fill='#68776D')
draw.text((799,612),'原生自适应图标 · Android 14 实测',font=font(20),fill='#68776D')
draw.text((60,740),'小尺寸',font=font(20,True),fill='#23453C')
cursor=200
for size in (16,24,32,48):
    paste(canvas,brand/f'logo-{size}.png',(cursor+(52-size)//2,764-size//2),size)
    draw.text((cursor+12,808),str(size),font=font(15),fill='#68776D')
    cursor+=83
for index,(color,label) in enumerate((('#EEF2ED','浅绿底'),('#23453C','深墨绿'),('#498E70','柔和绿'))):
    left=700+index*194
    draw.rounded_rectangle((left,744,left+48,792),10,fill=color,outline='#D4DED3')
    draw.text((left+60,747),label,font=font(17),fill='#23453C')
    draw.text((left+60,778),color,font=font(13),fill='#68776D')
canvas.save(out/'selected-logo-board.png')
print(out/'selected-logo-board.png')
