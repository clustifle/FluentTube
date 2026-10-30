"""Rebuild FluentTube's code-drawn vector and Windows icon assets (Pillow required)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'src/FluentTube/Assets'
BRAND = ROOT / 'branding'
ASSETS.mkdir(parents=True, exist_ok=True)
BRAND.mkdir(exist_ok=True)
svg = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
<defs><linearGradient id="blue" x2="0" y2="1"><stop stop-color="#2196f3"/><stop offset="1" stop-color="#0b58cf"/></linearGradient></defs>
<rect x="20" y="8" width="100" height="100" rx="24" fill="#78c6ff"/>
<rect x="8" y="20" width="104" height="100" rx="24" fill="url(#blue)"/>
<path d="M48 50 L80 70 L48 90 Z" fill="white" stroke="white" stroke-width="6" stroke-linejoin="round"/>
</svg>'''
(BRAND / 'FluentTube.svg').write_text(svg, encoding='utf-8')
(BRAND / 'FluentTube-monochrome.svg').write_text('''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><path fill="currentColor" fill-rule="evenodd" d="M44 8h52a24 24 0 0 1 24 24v52h-8V44a24 24 0 0 0-24-24H20A24 24 0 0 1 44 8ZM32 20h56a24 24 0 0 1 24 24v52a24 24 0 0 1-24 24H32A24 24 0 0 1 8 96V44a24 24 0 0 1 24-24Zm16 30v40l32-20Z"/></svg>''', encoding='utf-8')

def icon(size):
    scale = max(4, (size + 127) // 128)
    n = 128 * scale
    im = Image.new('RGBA', (n, n))
    d = ImageDraw.Draw(im)
    box = lambda x: tuple(round(v * scale) for v in x)
    d.rounded_rectangle(box((20, 8, 120, 108)), radius=24*scale, fill='#78c6ff')
    mask = Image.new('L', (n, n))
    ImageDraw.Draw(mask).rounded_rectangle(box((8, 20, 112, 120)), radius=24*scale, fill=255)
    gradient = Image.new('RGBA', (n, n))
    gd = ImageDraw.Draw(gradient)
    for y in range(n):
        t = min(1, max(0, (y/scale-20)/100))
        color = tuple(round(a+(b-a)*t) for a,b in zip((33,150,243),(11,88,207))) + (255,)
        gd.line((0,y,n,y), fill=color)
    im.paste(gradient, (0,0), mask)
    points = [(48*scale,50*scale),(80*scale,70*scale),(48*scale,90*scale)]
    d = ImageDraw.Draw(im)
    d.polygon(points, fill='white')
    d.line(points+[points[0]], fill='white', width=6*scale, joint='curve')
    for x,y in points: d.ellipse((x-3*scale,y-3*scale,x+3*scale,y+3*scale), fill='white')
    return im.resize((size,size), Image.Resampling.LANCZOS)

sizes = [16,20,24,32,40,44,48,64,128,150,256,310,1024]
for size in sizes: icon(size).save(ASSETS / f'Icon{size}.png')
icon(256).save(ASSETS / 'FluentTube.ico', sizes=[(s,s) for s in [16,24,32,48,64,128,256]])
for name,size in [('Square44x44Logo',44),('Square150x150Logo',150),('StoreLogo',50)]:
    icon(size).save(BRAND / f'{name}.png')

sheet = Image.new('RGB',(1200,700),'#f3f3f3')
d = ImageDraw.Draw(sheet)
font_path = Path('C:/Windows/Fonts/segoeui.ttf')
def font(size): return ImageFont.truetype(str(font_path),size)
d.text((40,20),'FluentTube · icon preview',fill='#171717',font=font(30))
d.text((40,63),'Vector artwork and Windows sizes · illustrative placements, not app screenshots',fill='#555555',font=font(16))
for x,bg,fg in [(30,'#ffffff','#202020'),(620,'#202020','#ffffff')]:
    d.rounded_rectangle((x,110,x+550,660),radius=18,fill=bg)
    sheet.paste(icon(128),(x+30,140),icon(128))
    d.text((x+180,165),'FluentTube',font=font(28),fill=fg)
    d.text((x+180,208),'Layered play tile',font=font(16),fill=fg)
    d.text((x+30,295),'Taskbar and title bar',font=font(18),fill=fg)
    for offset,size in [(35,16),(100,24),(175,32),(265,48),(380,64)]:
        sheet.paste(icon(size),(x+offset,340),icon(size))
        d.text((x+offset,415),str(size)+' px',font=font(14),fill=fg)
    d.text((x+30,470),'Start menu / desktop',font=font(18),fill=fg)
    for offset,size in [(40,44),(150,64),(300,96)]: sheet.paste(icon(size),(x+offset,515),icon(size))
sheet.save(BRAND / 'IconPreview.png')
print('Generated SVG, Windows ICO, PNG assets, and light/dark preview.')
