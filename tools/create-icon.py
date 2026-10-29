"""Render the original PulseDeck vector mark to Windows multi-resolution ICO."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
assets = root / 'src' / 'PulseDeck' / 'Assets'
assets.mkdir(parents=True, exist_ok=True)
size = 1024
im = Image.new('RGBA', (size, size))
d = ImageDraw.Draw(im)
d.rounded_rectangle((24, 24, 1000, 1000), 230, fill='#0D1620', outline='#2C4658', width=14)
# Restrained concentric bevels read clearly in the taskbar; pulse remains recognizable at 16px.
d.ellipse((144, 144, 880, 880), outline='#284F54', width=24)
d.arc((144, 144, 880, 880), 210, 330, fill='#65E8CD', width=24)
d.arc((144, 144, 880, 880), 30, 120, fill='#B8A2F5', width=24)
points = [(210, 523), (350, 523), (426, 305), (540, 725), (636, 523), (815, 523)]
d.line([(x+9,y+18) for x,y in points], fill='#235C64', width=65, joint='curve')
d.line(points, fill='#65E8CD', width=54, joint='curve')
for x, y in points: d.ellipse((x-27,y-27,x+27,y+27), fill='#65E8CD')
im.resize((512,512),Image.Resampling.LANCZOS).save(assets/'PulseDeck.png')
im.save(assets/'PulseDeck.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
(assets/'PulseDeck.svg').write_text('''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024"><rect x="24" y="24" width="976" height="976" rx="230" fill="#0D1620" stroke="#2C4658" stroke-width="14"/><circle cx="512" cy="512" r="368" fill="none" stroke="#284F54" stroke-width="24"/><path d="M210 523H350L426 305L540 725L636 523H815" fill="none" stroke="#65E8CD" stroke-width="54" stroke-linecap="round" stroke-linejoin="round"/></svg>''',encoding='utf-8')
print(assets/'PulseDeck.ico')
