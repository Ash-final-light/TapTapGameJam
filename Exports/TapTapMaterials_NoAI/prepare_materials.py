"""Export existing game artwork and real screenshots. No generative image tools."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile
from PIL import Image, ImageOps, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
SOURCE = OUT / 'Sources'
UPLOAD = OUT / 'Upload'
UPLOAD.mkdir(exist_ok=True)
manifest = []

def save(im, name, purpose, sources, max_mb=4):
    path = UPLOAD / name
    im.save(path, optimize=True)
    assert path.stat().st_size < max_mb * 1024 * 1024, name
    with Image.open(path) as check:
        check.verify()
    manifest.append(dict(file=name, purpose=purpose, width=im.width,
                         height=im.height, mode=im.mode, bytes=path.stat().st_size,
                         sources=sources, sha256=hashlib.sha256(path.read_bytes()).hexdigest()))

hero_path = ROOT / 'Assets/Player/HeroPortrait.png'
logo_path = ROOT / 'Assets/ArtAsset/package4/霓虹几何风格_VARIATION_标志.png'
hero = Image.open(hero_path).convert('RGBA')
hero = hero.crop(hero.getchannel('A').getbbox())
logo = Image.open(logo_path).convert('RGBA')
logo = logo.crop(logo.getchannel('A').getbbox())
menu = Image.open(SOURCE / 'main-menu.png').convert('RGB')
battle = Image.open(SOURCE / 'battle-later-3.png').convert('RGB')

def paste_fit(canvas, art, box):
    x, y, w, h = box
    fitted = ImageOps.contain(art, (w, h), Image.Resampling.LANCZOS)
    canvas.paste(fitted, (x + (w-fitted.width)//2, y + (h-fitted.height)//2), fitted)

def backdrop(size):
    # Only pixels cropped from text-free regions of the real menu; no inpainting.
    w, h = size
    base = menu.crop((0, 0, 375, 875)).resize(size, Image.Resampling.LANCZOS)
    accent = menu.crop((1530, 150, 1920, 875))
    aw = round(h * accent.width / accent.height)
    accent = accent.resize((aw, h), Image.Resampling.LANCZOS)
    # Softly blend the crop edge so this remains a continuous background.
    alpha = Image.new('L', (aw, h))
    ramp = bytes(min(255, round(255*x/max(1,aw*.22))) for x in range(aw))
    alpha.frombytes(ramp * h)
    base.paste(accent, (w-aw, 0), alpha)
    return base

# A square, opaque icon using the existing blue/purple stage and original hero.
icon = battle.crop((1280, 10, 1590, 320)).resize((512, 512), Image.Resampling.LANCZOS)
paste_fit(icon, hero, (48, 24, 416, 464))
save(icon, '01_Icon_512x512.png', '游戏图标',
     ['Assets/Player/HeroPortrait.png', 'Sources/battle-later-3.png'])
assert icon.mode == 'RGB'

save(logo, '02_Logo_Transparent.png', 'PC 游戏 LOGO（透明，裁去无用边距）',
     ['Assets/ArtAsset/package4/霓虹几何风格_VARIATION_标志.png'])
assert logo.width >= 1280 or logo.height >= 720

# Screenshot pixels and the game HUD are preserved exactly; PNG compression only.
screens = [
    ('battle-later-3.png', '03_Screenshot_01_Battle_1920x1080.png', '实机截图 1：弹幕战斗，建议首张'),
    ('song-selection.png', '04_Screenshot_02_Songs_1920x1080.png', '实机截图 2：歌曲选择'),
    ('loadout.png', '05_Screenshot_03_Loadout_1920x1080.png', '实机截图 3：弹幕装备'),
    ('result.png', '06_Screenshot_04_Result_1920x1080.png', '实机截图 4：战斗结算（可选）'),
]
for source, name, purpose in screens:
    original = Image.open(SOURCE / source).convert('RGB')
    assert original.size == (1920,1080)
    save(original, name, purpose, ['Sources/' + source])
    with Image.open(UPLOAD / name) as exported:
        assert exported.convert('RGB').tobytes() == original.tobytes()

# Dedicated artwork layouts, using extracted background, original logo and hero.
# These are separate from the unmodified screenshot set above.
promo = backdrop((1920,1080))
paste_fit(promo, logo, (250,175,1420,400))
paste_fit(promo, hero, (720,555,480,490))
art_sources = ['Sources/main-menu.png (text-free background crops)',
               'Assets/Player/HeroPortrait.png',
               'Assets/ArtAsset/package4/霓虹几何风格_VARIATION_标志.png']
save(promo, '07_Promo_1920x1080.png', '16:9 宣传图：原素材排版，仅游戏名', art_sources)

cover = backdrop((920,430))
paste_fit(cover, logo, (50,30,650,240))
paste_fit(cover, hero, (585,110,265,310))
cover = cover.resize((460,215), Image.Resampling.LANCZOS)
save(cover, '08_PC_Cover_460x215.png', 'PC 横版游戏封面', art_sources, 3)

portrait = backdrop((600,900))
paste_fit(portrait, logo, (30,90,540,205))
paste_fit(portrait, hero, (120,330,360,500))
save(portrait, '09_PC_Cover_600x900.png', 'PC 竖版游戏封面', art_sources, 3)

# Contact sheet is for local review only, never upload as a game screenshot.
sheet = Image.new('RGB', (1200,1050), '#e8eaf2')
draw = ImageDraw.Draw(sheet)
for i, row in enumerate(manifest):
    x, y = (i%3)*400, (i//3)*350
    with Image.open(UPLOAD / row['file']) as src:
        preview = ImageOps.contain(src.convert('RGBA'), (380,300), Image.Resampling.LANCZOS)
        sheet.paste(preview, (x+(400-preview.width)//2,y+10+(300-preview.height)//2),preview)
    draw.text((x+12,y+318),row['file'],fill='#16203b')
sheet.save(OUT / 'Preview_ContactSheet.jpg', quality=90)

report = dict(requirements_url='https://developer.taptap.cn/docs/store/release/publish/material/',
              no_generative_image_tools=True,
              screenshot_capture='Real Windows game via @oai/sky, 1920x1080 fullscreen',
              screenshots_unretouched=True, files=manifest)
(OUT/'Validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')

readme = '''# TapTap 上传图片（未调用 AI 图像生成）

上传文件位于 Upload 文件夹。Sources 为实机截图原图，勿整批上传。

| 后台位置 | 文件 |
|---|---|
| 游戏图标 | 01_Icon_512x512.png |
| PC 游戏 LOGO | 02_Logo_Transparent.png |
| 游戏截图，按顺序 | 03、04、05；06 为可选结算图 |
| 16:9 宣传图 | 07_Promo_1920x1080.png |
| PC 横版游戏封面 | 08_PC_Cover_460x215.png |
| PC 竖版游戏封面 | 09_PC_Cover_600x900.png |

实机截图为全屏原图，只进行 PNG 无损压缩；没有抹除 UI、添加弹幕、修改成绩或美化画面。
图标及宣传/封面使用项目现有角色、Logo 和实机背景裁片排版，未调用 ImageGen。
角色原图分辨率较小，放大沿用原素材细节，未使用 AI 超分。
游戏自带的 trialversion 字样保留在实机截图内。

图标为不透明直角 RGB 方图，背景为游戏内蓝紫色；Logo 为透明 PNG，去掉无用留白。
宣传/封面只有原游戏名 VARIATION，没有另加宣传文案。内容是否通过由 TapTap 审核决定。

本次交付为图片，不含视频。官网另要求开放下载/试玩时提交实机录屏；本次未录制。
仅使用横版录屏时，不必提交 1:1 宣传图。PC 游戏库壁纸和 Windows 无 Logo 推荐图未包含。

要求来源：https://developer.taptap.cn/docs/store/release/publish/material/
尺寸、大小、素材来源和 SHA256 见 Validation.json。
'''
(OUT/'使用说明.md').write_text(readme,encoding='utf-8')
archive = OUT.parent/'TapTapMaterials_NoAI.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in sorted(UPLOAD.glob('*.png')):
        z.write(p,'Upload/'+p.name)
    for p in [OUT/'使用说明.md',OUT/'Validation.json']:
        z.write(p,p.name)
print(json.dumps(manifest,ensure_ascii=True,indent=2))
print('Archive:', archive)
