"""Build the original Astral Scanner icon from scalable geometric shapes (Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).parent / 'assets'
root.mkdir(exist_ok=True)
scale = 4
im = Image.new('RGBA', (256*scale, 256*scale))
d = ImageDraw.Draw(im)
def box(bounds, fill, radius=0, outline=None, width=1):
    bounds=tuple(round(v*scale) for v in bounds)
    if radius:d.rounded_rectangle(bounds, radius*scale, fill, outline, width*scale)
    else:d.rectangle(bounds, fill, outline, width*scale)
def poly(points, fill):d.polygon([(int(x*scale),int(y*scale)) for x,y in points],fill=fill)
def line(points, fill, width):d.line([(int(x*scale),int(y*scale)) for x,y in points],fill,width*scale,joint='curve')

box((8,8,248,248),'#101e30',52)
box((13,13,243,243),'#15273b',47, '#334b61',2)
poly([(28,174),(124,77),(230,166),(225,224),(185,243),(69,243),(28,223)],'#1d3347')
# The four-point star remains legible at small taskbar sizes.
poly([(128,28),(139,57),(168,68),(139,79),(128,107),(117,79),(88,68),(117,57)],'#99efd7')
poly([(128,40),(133,63),(154,68),(133,73),(128,95),(123,73),(102,68),(123,63)],'#e2fff3')
# Chest lid and front, outlined in warm gold.
box((49,112,207,202),'#e9be74',17)
box((57,119,199,193),'#6b4937',10)
poly([(57,136),(71,108),(185,108),(199,136)],'#f5d593')
poly([(69,132),(78,117),(178,117),(187,132)],'#a47947')
box((50,137,206,151),'#f5d593',3)
box((72,151,82,193),'#dba85e')
box((174,151,184,193),'#dba85e')
box((113,138,143,176),'#ffdf94',6)
poly([(128,147),(136,156),(128,166),(120,156)],'#65d8c7')
line([(60,203),(196,203)],'#091522',5)
im.resize((256,256),Image.Resampling.LANCZOS).save(root/'AstralScanner.png')
im.save(root/'AstralScanner.ico',sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
print('Created original multiresolution icon (16–256 px).')
