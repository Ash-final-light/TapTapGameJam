"""Compose a text-free library wallpaper from existing game art, without AI."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps
import json, math, hashlib

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SOURCE = HERE / 'Sources'
parts = json.loads((SOURCE/'hero-wallpaper-mesh.json').read_text(encoding='utf-8-sig'))
atlas = Image.open(ROOT/'Assets/ArtAsset/Spine/Hero/char_hero.png').convert('RGBA')
points = [(p['Vertices'][i],p['Vertices'][i+1]) for p in parts for i in range(0,len(p['Vertices']),2)]
left, right = min(p[0] for p in points), max(p[0] for p in points)
bottom, top = min(p[1] for p in points), max(p[1] for p in points)
scale = 1.25
rw, rh = math.ceil((right-left)*scale)+12, math.ceil((top-bottom)*scale)+12
hero = Image.new('RGBA',(rw,rh))

def coefficients(d, s):
    x1,y1=d[0];x2,y2=d[1];x3,y3=d[2]
    den=x1*(y2-y3)+x2*(y3-y1)+x3*(y1-y2)
    if abs(den)<1e-8:return None
    out=[]
    for values in ([p[0] for p in s],[p[1] for p in s]):
        z1,z2,z3=values
        out.extend(((z1*(y2-y3)+z2*(y3-y1)+z3*(y1-y2))/den,
                    (z1*(x3-x2)+z2*(x1-x3)+z3*(x2-x1))/den,
                    (z1*(x2*y3-x3*y2)+z2*(x3*y1-x1*y3)+z3*(x1*y2-x2*y1))/den))
    return out

for part in parts:
    v=part['Vertices'];uv=part['UVs'];tri=part['Triangles']
    dest=[((v[i]-left)*scale+6,(top-v[i+1])*scale+6) for i in range(0,len(v),2)]
    texture=[(uv[i]*atlas.width,uv[i+1]*atlas.height) for i in range(0,len(uv),2)]
    layer=Image.new('RGBA',(rw,rh))
    for k in range(0,len(tri),3):
        d=[dest[n] for n in tri[k:k+3]];s=[texture[n] for n in tri[k:k+3]]
        x0=max(0,math.floor(min(p[0] for p in d)));y0=max(0,math.floor(min(p[1] for p in d)))
        x1=min(rw,math.ceil(max(p[0] for p in d))+1);y1=min(rh,math.ceil(max(p[1] for p in d))+1)
        d=[(x-x0,y-y0) for x,y in d]
        coeff=coefficients(d,s)
        if coeff is None or x1<=x0 or y1<=y0:continue
        patch=atlas.transform((x1-x0,y1-y0),Image.Transform.AFFINE,coeff,Image.Resampling.BICUBIC)
        mask=Image.new('L',patch.size);ImageDraw.Draw(mask).polygon(d,fill=255)
        layer.paste(patch,(x0,y0),mask)
    if part['Alpha']<1:
        layer.putalpha(layer.getchannel('A').point(lambda x:round(x*part['Alpha'])))
    hero=Image.alpha_composite(hero,layer)
hero=hero.crop(hero.getchannel('A').getbbox())
hero.save(SOURCE/'hero-wallpaper-original-spine.png',optimize=True)

size=(3840,1240)
loading=Image.open(SOURCE/'loading-background-reference.png').convert('RGB')
# All source boxes exclude the logo, loading text, tips, cursor and watermark.
wall=loading.crop((0,0,500,1050)).resize(size,Image.Resampling.LANCZOS)
motif=loading.crop((1430,0,1920,780))
motif=motif.resize((779,1240),Image.Resampling.LANCZOS)
mask=Image.new('L',motif.size)
row=bytes(round(255*min(1,x/200,(778-x)/200)) for x in range(779))
mask.frombytes(row*1240)
wall.paste(motif,(1030,0),mask)
fitted=ImageOps.contain(hero,(1040,1050),Image.Resampling.LANCZOS)
hero_pos=(2370-fitted.width//2,98+(1050-fitted.height)//2)
wall.paste(fitted,hero_pos,fitted)
output=HERE/'Upload/10_PC_Library_Wallpaper_3840x1240.png'
if output.exists():
    previous=json.loads((HERE/'Wallpaper_Validation.json').read_text(encoding='utf-8'))
    assert hashlib.sha256(output.read_bytes()).hexdigest()==previous['sha256'], 'Preserve external changes'
wall.save(output,optimize=True)
assert wall.size==size and wall.mode=='RGB' and output.stat().st_size<10*1024*1024
with Image.open(output) as check:check.verify()
record={'file':output.name,'width':3840,'height':1240,'bytes':output.stat().st_size,
        'contains_text':False,'contains_logo':False,'ai_generated':False,
        'character_source':'Original Spine setup pose, original atlas, software rasterized',
        'hero_source_render_size':hero.size,'hero_placement':hero_pos,
        'background_source':'Text-free crops of real game loading screen',
        'sha256':hashlib.sha256(output.read_bytes()).hexdigest()}
(HERE/'Wallpaper_Validation.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(record,ensure_ascii=True,indent=2))
print(output)
