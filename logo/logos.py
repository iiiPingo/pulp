"""Pulp logo concepts: tiles (family squircle style) + tray ring icons, rendered supersampled."""
import math, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageFont

SS = 8  # supersample


def hexc(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def gradient(size, a, b):
    a, b = np.array(hexc(a), float), np.array(hexc(b), float)
    y, x = np.mgrid[0:size, 0:size]
    t = ((x + y) / (2.0 * (size - 1)))[..., None]
    rgb = a * (1 - t) + b * t
    alpha = np.full((size, size, 1), 255.0)
    return Image.fromarray(np.concatenate([rgb, alpha], 2).astype(np.uint8), 'RGBA')


def disc(d, cx, cy, r, fill=255):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)


def smooth(mask, rad):
    # round sharp corners: blur then re-threshold
    if rad <= 0:
        return mask
    m = mask.filter(ImageFilter.GaussianBlur(rad))
    return m.point(lambda v: 255 if v >= 128 else 0)


# ---------------------------------------------------------------- glyph masks (size = full tile px)
def glyph_slice(n, scale=1.0, spokes=8, sw=0.034, hole=True):
    """citrus wheel: rind ring + segments separated by spokes + center pith hole"""
    m = Image.new('L', (n, n), 0)
    d = ImageDraw.Draw(m)
    c = n / 2
    R1, R2, R3 = 0.33 * n * scale, 0.283 * n * scale, 0.252 * n * scale
    disc(d, c, c, R1)
    disc(d, c, c, R2, 0)
    seg = Image.new('L', (n, n), 0)
    ds = ImageDraw.Draw(seg)
    disc(ds, c, c, R3)
    w = sw * n * scale
    for k in range(spokes):
        a = math.radians(22.5 + k * 360 / spokes)
        ds.line([c, c, c + math.cos(a) * R3 * 1.2, c + math.sin(a) * R3 * 1.2], fill=0, width=max(1, int(w)))
    if hole:
        disc(ds, c, c, 0.045 * n * scale, 0)
    seg = smooth(seg, 0.012 * n * scale)
    return ImageChops.lighter(m, seg)


def glyph_passion(n, scale=1.0, small=False):
    """passion fruit half: rind ring + pulp disc with seed holes"""
    m = Image.new('L', (n, n), 0)
    d = ImageDraw.Draw(m)
    c = n / 2
    disc(d, c, c, 0.33 * n * scale)
    disc(d, c, c, 0.283 * n * scale, 0)
    disc(d, c, c, 0.25 * n * scale)
    sr = (0.042 if small else 0.032) * n * scale
    if small:
        seeds = [(0.135 * math.cos(math.radians(a)), 0.135 * math.sin(math.radians(a))) for a in range(-90, 270, 60)]
        for sx, sy in seeds:
            disc(d, c + sx * n * scale, c + sy * n * scale, sr, 0)
        return m
    seeds = [(0, 0)]
    seeds += [(0.115 * math.cos(math.radians(a)), 0.115 * math.sin(math.radians(a))) for a in range(-90, 270, 60)]
    seeds += [(0.19 * math.cos(math.radians(a)), 0.19 * math.sin(math.radians(a))) for a in range(-60, 300, 36)]
    for sx, sy in seeds:
        disc(d, c + sx * n * scale, c + sy * n * scale, sr, 0)
    return m


def drop_poly(cx, top, h):
    w = h * 0.72
    pts = []
    for i in range(96):
        t = 2 * math.pi * i / 96
        x = math.sin(t) * math.sin(t / 2)
        y = -math.cos(t)
        pts.append((cx + x * w * 0.62, top + (y + 1) / 2 * h))
    return pts


def glyph_clip(n, scale=1.0):
    """clipboard with a juice drop cut out"""
    m = Image.new('L', (n, n), 0)
    d = ImageDraw.Draw(m)
    s = n * scale
    o = (n - s) / 2
    P = lambda u, v: (o + u * s, o + v * s)
    d.rounded_rectangle([*P(0.255, 0.235), *P(0.745, 0.80)], radius=0.075 * s, fill=255)
    # gap around the clip, then the clip itself
    d.rounded_rectangle([*P(0.355, 0.165), *P(0.645, 0.335)], radius=0.06 * s, fill=0)
    d.rounded_rectangle([*P(0.39, 0.19), *P(0.61, 0.305)], radius=0.04 * s, fill=255)
    d.polygon(drop_poly(o + 0.5 * s, o + 0.40 * s, 0.31 * s), fill=0)
    return m


# ---------------------------------------------------------------- tile
def tile(size, a, b, glyph):
    n = size * SS
    g = gradient(n, a, b)
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=0.24 * n, fill=255)
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    out.paste(g, (0, 0), mask)
    gm = glyph(n)
    white = Image.new('RGBA', (n, n), (255, 255, 255, 255))
    out.paste(white, (0, 0), gm)
    return out.resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------- tray (transparent, ring-style)
def tray(size, accent, glyph, paused=False):
    n = size * SS
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    k = 1.0 / 0.66 * 0.98
    gm = glyph_slice(n, k, 6, 0.06, False) if glyph is glyph_slice else glyph_passion(n, k, True)  # glyph fills the icon
    col = (150, 150, 150, 255) if paused else hexc(accent) + (255,)
    # rind/ring in accent, inner parts white
    ring = Image.new('L', (n, n), 0)
    d = ImageDraw.Draw(ring)
    c = n / 2
    R1 = 0.33 * n / 0.66 * 0.98
    R2 = 0.283 * n / 0.66 * 0.98
    disc(d, c, c, R1)
    disc(d, c, c, R2, 0)
    inner = ImageChops.subtract(gm, ring)
    out.paste(Image.new('RGBA', (n, n), (255, 255, 255, 255)), (0, 0), inner)
    out.paste(Image.new('RGBA', (n, n), col), (0, 0), ImageChops.multiply(gm, ring))
    return out.resize((size, size), Image.LANCZOS)


def tray_clip(size, accent, paused=False):
    n = size * SS
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    col = (150, 150, 150, 255) if paused else hexc(accent) + (255,)
    gm = glyph_clip(n, 1.32)
    # accent board, white drop
    out.paste(Image.new('RGBA', (n, n), col), (0, 0), gm)
    dm = Image.new('L', (n, n), 0)
    s = n * 1.32
    o = (n - s) / 2
    ImageDraw.Draw(dm).polygon(drop_poly(o + 0.5 * s, o + 0.40 * s, 0.31 * s), fill=255)
    out.paste(Image.new('RGBA', (n, n), (255, 255, 255, 255)), (0, 0), dm)
    return out.resize((size, size), Image.LANCZOS)


OPTIONS = [
    dict(key='A', name='Slice', sub='citrus wheel, tangerine', a='#E0480B', b='#FFAE45', accent='#FF8A3D', glyph=glyph_slice, tray='ring'),
    dict(key='B', name='Passion', sub='passion fruit pulp, violet', a='#4C2AA8', b='#D16BEB', accent='#B37CFF', glyph=glyph_passion, tray='ring'),
    dict(key='C', name='Squeeze', sub='clipboard + juice drop, mango', a='#E06A00', b='#FFC53D', accent='#FFA826', glyph=glyph_clip, tray='clip'),
]


def font(sz, bold=False):
    p = '/usr/share/fonts/opentype/inter/Inter-SemiBold.otf' if bold else '/usr/share/fonts/opentype/inter/Inter-Regular.otf'
    try:
        return ImageFont.truetype(p, sz)
    except Exception:
        return ImageFont.load_default()


def sheet(path):
    W, colw = 1260, 420
    H = 640
    img = Image.new('RGBA', (W, H), (24, 24, 24, 255))
    d = ImageDraw.Draw(img)
    for i, o in enumerate(OPTIONS):
        x0 = i * colw
        if i:
            d.line([x0, 30, x0, H - 30], fill=(48, 48, 48), width=1)
        big = tile(200, o['a'], o['b'], o['glyph'])
        img.alpha_composite(big, (x0 + 110, 40))
        d.text((x0 + 210, 262), f"{o['key']}  ·  {o['name']}", font=font(26, True), fill=(242, 242, 242), anchor='mm')
        d.text((x0 + 210, 294), o['sub'], font=font(16), fill=(160, 160, 160), anchor='mm')
        # small tiles
        xx = x0 + 70
        for s in (64, 32, 24, 16):
            t = tile(s, o['a'], o['b'], o['glyph'])
            img.alpha_composite(t, (xx, 330 + (64 - s) // 2))
            xx += s + 26
        d.text((xx + 6, 362), 'exe icon sizes', font=font(13), fill=(112, 112, 112), anchor='lm')
        # taskbar strip with tray icons at real size
        strip = (x0 + 40, 420, x0 + colw - 40, 470)
        d.rounded_rectangle(strip, radius=8, fill=(32, 32, 32))
        xx = strip[0] + 18
        for s in (16, 20, 24):
            t = tray(s, o['accent'], o['glyph']) if o['tray'] == 'ring' else tray_clip(s, o['accent'])
            img.alpha_composite(t, (xx, 445 - s // 2))
            xx += s + 18
        t = tray(20, o['accent'], o['glyph'], True) if o['tray'] == 'ring' else tray_clip(20, o['accent'], True)
        img.alpha_composite(t, (xx + 10, 435))
        d.text((xx + 40, 445), 'tray · paused', font=font(13), fill=(112, 112, 112), anchor='lm')
        # zoomed tray
        t = tray(16, o['accent'], o['glyph']) if o['tray'] == 'ring' else tray_clip(16, o['accent'])
        z = t.resize((64, 64), Image.NEAREST)
        img.alpha_composite(z, (x0 + 60, 490))
        d.text((x0 + 60, 562), 'tray 16 px, zoomed', font=font(12), fill=(112, 112, 112))
        # accent sample: button + selected row bar
        bx = x0 + 170
        d.rounded_rectangle([bx, 494, bx + 190, 528], radius=8, fill=(44, 44, 44))
        d.rounded_rectangle([bx, 494, bx + 4, 528], radius=2, fill=hexc(o['accent']))
        d.text((bx + 16, 511), 'Selected item', font=font(14), fill=(242, 242, 242), anchor='lm')
        d.rounded_rectangle([bx, 538, bx + 90, 568], radius=7, fill=hexc(o['accent']))
        d.text((bx + 45, 553), 'Paste', font=font(14, True), fill=(255, 255, 255), anchor='mm')
        d.text((bx + 104, 553), o['accent'], font=font(13), fill=(160, 160, 160), anchor='lm')
    img.save(path)


if __name__ == '__main__':
    sheet(sys.argv[1] if len(sys.argv) > 1 else 'pulp_logo_options.png')
