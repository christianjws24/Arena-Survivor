"""Pixel-art ship concepts for Arena Survivor (Parry / Shooter / Speed).

Run:  python Art/ShipConcepts/make_ships.py
Outputs 48x48 sprites (1x), x8 previews, and a composed showcase sheet.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.dirname(os.path.abspath(__file__))
W = H = 48


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


# ---------------------------------------------------------------- palettes
HULL = [hexc(c) for c in ['#1c1a36', '#35335f', '#55598f', '#7f8bbd', '#b4c2e2', '#eef4ff']]
CYAN = [hexc(c) for c in ['#0a3346', '#0f6474', '#169ea6', '#3cd3c6', '#9bf5e4', '#f0fffb']]
CRIM = [hexc(c) for c in ['#360c22', '#6a1430', '#a5233b', '#de4a3e', '#ff8a5c', '#ffe2bb']]
AMBR = [hexc(c) for c in ['#3a1d0c', '#763b10', '#bd651b', '#efa02b', '#ffd65e', '#fff7cc']]
GLASS = [hexc(c) for c in ['#0a1238', '#14307a', '#2563c2', '#5aaaf5', '#c8ecff', '#ffffff']]
OUTLINE_DARK = hexc('#0a0717')
OUTLINE_LIT = hexc('#251c4a')
LIGHT = np.array([-0.55, -0.83])  # direction toward the light (top-left)


# ---------------------------------------------------------------- geometry
def poly(points):
    img = Image.new('L', (W, H), 0)
    ImageDraw.Draw(img).polygon(points, fill=1)
    return np.array(img) > 0


def mirror_x(m):
    m = m.copy()
    m[:, 24:] = np.fliplr(m[:, :24])
    return m


def mirror_y(m):
    m = m.copy()
    m[24:, :] = np.flipud(m[:24, :])
    return m


def shift(m, dx, dy):
    out = np.zeros_like(m)
    h, w = m.shape
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = m[ys, xs]
    return out


def erode(m):
    return m & shift(m, 1, 0) & shift(m, -1, 0) & shift(m, 0, 1) & shift(m, 0, -1)


def dilate8(m):
    out = m.copy()
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            out |= shift(m, dx, dy)
    return out


def depth(m, maxd=6):
    d = np.zeros(m.shape, int)
    cur = m.copy()
    for i in range(1, maxd + 1):
        d[cur] = i
        cur = erode(cur)
    return d


def box(x0, y0, x1, y1):
    m = np.zeros((H, W), bool)
    m[y0:y1 + 1, x0:x1 + 1] = True
    return m


# ---------------------------------------------------------------- shading
def shade(mask, regions):
    """regions: list of (submask, ramp) applied in order over the hull."""
    D = depth(mask)
    blur = np.array(Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.6))) / 255.0
    gy, gx = np.gradient(blur)
    nx, ny = -gx, -gy
    n = np.hypot(nx, ny) + 1e-6
    dot = (nx * LIGHT[0] + ny * LIGHT[1]) / n
    ys, xs = np.mgrid[0:H, 0:W]
    rows = np.where(mask.any(1))[0]
    top, bot = rows.min(), rows.max()
    vert = 0.7 - 1.4 * (ys - top) / max(1, bot - top)          # top of sprite brighter
    horiz = -0.25 * (xs - 24) / 24.0                            # slight left-lit falloff
    edge = np.select([D == 1, D == 2, D == 3], [1.7, 0.9, 0.35], 0.0) * dot
    level = np.clip(np.round(2.1 + vert + horiz + edge), 0, 4).astype(int)
    level[(D == 1) & (dot > 0.72)] = 5                          # specular rim

    ramp_idx = np.zeros((H, W), int)
    ramps = [HULL]
    for i, (sub, ramp) in enumerate(regions, start=1):
        ramp_idx[sub & mask] = i
        ramps.append(ramp)

    img = np.zeros((H, W, 4), np.uint8)
    for y in range(H):
        for x in range(W):
            if mask[y, x]:
                img[y, x] = ramps[ramp_idx[y, x]][level[y, x]]

    # seams where two materials meet: darken the lower/right side of the joint
    for i in range(1, len(ramps)):
        sub = ramp_idx == i
        seam = mask & ~sub & (shift(sub, 1, 0) | shift(sub, 0, 1)) & (D > 1)
        for y, x in zip(*np.where(seam)):
            r = ramps[ramp_idx[y, x]]
            img[y, x] = r[max(0, level[y, x] - 2)]

    # selective outline: lighter on the lit (top-left) side
    ring = dilate8(mask) & ~mask
    lit = shift(mask, -1, 0) | shift(mask, 0, -1)   # mask pixel is right/below -> outline sits top-left
    for y, x in zip(*np.where(ring)):
        img[y, x] = OUTLINE_LIT if lit[y, x] and not (shift(mask, 1, 0)[y, x] or shift(mask, 0, 1)[y, x]) else OUTLINE_DARK
    return img, level


class Canvas:
    def __init__(self, img):
        self.img = img
        self.emit = np.zeros((H, W, 4), np.uint8)  # bloom source

    def px(self, x, y, c, emit=False, mx=True):
        pts = [(x, y)] + ([(47 - x, y)] if mx else [])
        for (a, b) in pts:
            if 0 <= a < W and 0 <= b < H:
                self.img[b, a] = c
                if emit:
                    self.emit[b, a] = c

    def hline(self, x0, x1, y, c, emit=False, mx=True):
        for x in range(x0, x1 + 1):
            self.px(x, y, c, emit, mx)

    def vline(self, x, y0, y1, c, emit=False, mx=True):
        for y in range(y0, y1 + 1):
            self.px(x, y, c, emit, mx)

    def flame(self, cx, y0, length, ramp, width=1, mx=False):
        """Downward flame; cx may be fractional (x.5 = 2px core)."""
        cols = [int(np.floor(cx)), int(np.ceil(cx))] if cx % 1 else [int(cx)]
        for i in range(length):
            t = i / max(1, length - 1)
            core = ramp[5] if t < 0.25 else ramp[4] if t < 0.55 else ramp[3] if t < 0.8 else ramp[2]
            for c in cols:
                self.px(c, y0 + i, core, True, mx)
            if width > 0 and t < 0.6:
                side = ramp[3] if t < 0.3 else ramp[2]
                self.px(cols[0] - 1, y0 + i, side, True, mx)
                self.px(cols[-1] + 1, y0 + i, side, True, mx)


def glass_dome(cv, mask, ramp=GLASS):
    """Shade a canopy: bright top-left, deep bottom-right, one specular pixel."""
    ys, xs = np.where(mask)
    y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    for y, x in zip(ys, xs):
        t = ((y - y0) / max(1, y1 - y0)) * 0.65 + ((x - x0) / max(1, x1 - x0)) * 0.35
        lvl = 4 if t < 0.18 else 3 if t < 0.4 else 2 if t < 0.65 else 1
        cv.img[y, x] = ramp[lvl]
    cv.img[y0 + 1, x0 + 1] = ramp[5]


# ---------------------------------------------------------------- ships
def ship_parry():
    q = poly([(1, 24), (1, 19), (7, 12), (18, 12), (13, 20), (21, 20), (21, 17), (23, 15), (24, 15), (24, 24)])
    mask = mirror_y(mirror_x(q))
    claws = mirror_y(mirror_x(box(8, 12, 19, 19))) & mask & ~box(20, 0, 27, 47)
    img, _ = shade(mask, [(claws, CYAN)])
    cv = Canvas(img)

    # pod/center seam + vents
    cv.vline(12, 21, 26, HULL[1])
    for y in (21, 23, 25):
        cv.hline(5, 8, y, HULL[1])
        cv.hline(5, 8, y + 1, HULL[3])
    # side shield emitters
    cv.vline(2, 21, 26, CYAN[4], emit=True)
    cv.vline(3, 22, 25, CYAN[3], emit=True)
    # claw tip energy edges (inner hook faces)
    for (x, y) in [(17, 13), (16, 14), (15, 16)]:
        cv.px(x, y, CYAN[5], emit=True)
        cv.px(x, 47 - y, CYAN[4], emit=True)
    # cockpit on the front nub
    canopy = box(22, 16, 25, 19)
    canopy[16, 22] = canopy[16, 25] = False
    glass_dome(cv, canopy)
    # parry core: glowing diamond in the center bar
    core = [(23, 21, CYAN[3]), (22, 22, CYAN[3]), (23, 22, CYAN[4]), (21, 23, CYAN[2]), (22, 23, CYAN[4]),
            (23, 23, CYAN[5]), (21, 24, CYAN[2]), (22, 24, CYAN[4]), (23, 24, CYAN[5]), (22, 25, CYAN[3]),
            (23, 25, CYAN[4]), (23, 26, CYAN[3])]
    for x, y, c in core:
        cv.px(x, y, c, emit=True)
    cv.hline(19, 20, 23, HULL[1]); cv.hline(19, 20, 24, HULL[1])
    # rear nub engine
    cv.hline(22, 23, 32, HULL[0])
    cv.hline(22, 23, 33, CYAN[3], emit=True)
    cv.flame(23.5, 36, 5, CYAN, width=1)
    return cv


def ship_shooter():
    left = poly([(24, 16), (22, 16), (20, 17), (19, 19), (19, 24), (15, 24), (15, 5), (10, 5), (10, 24),
                 (8, 24), (8, 16), (3, 16), (3, 34), (19, 34), (16, 40), (24, 40)])
    mask = mirror_x(left)
    tips = mirror_x(box(10, 5, 15, 9) | box(3, 16, 8, 19)) & mask
    stripe = mirror_x(box(4, 30, 9, 31)) & mask
    img, _ = shade(mask, [(tips, CRIM), (stripe, CRIM)])
    cv = Canvas(img)

    # gun bores with a charged ember
    cv.hline(12, 13, 6, OUTLINE_DARK); cv.hline(12, 13, 7, CRIM[0])
    cv.px(12, 6, CRIM[4], emit=True); cv.px(13, 6, CRIM[3], emit=True)
    cv.hline(5, 6, 17, OUTLINE_DARK)
    cv.px(5, 17, CRIM[4], emit=True)
    # barrel ribs
    for y in (12, 15, 18, 21):
        cv.hline(11, 14, y, HULL[1])
    # canopy
    canopy = mirror_x(poly([(24, 18), (22, 18), (21, 19), (20, 21), (20, 23), (24, 23)])) & mask
    glass_dome(cv, canopy)
    # wing panel seam + vents
    cv.hline(4, 18, 27, HULL[1])
    cv.hline(4, 18, 28, HULL[3])
    for x in (12, 14, 16):
        cv.vline(x, 30, 32, HULL[1])
    # spine
    cv.vline(23, 25, 33, HULL[3]); cv.px(23, 29, CRIM[4], emit=True)
    # engines
    cv.hline(18, 21, 39, OUTLINE_DARK)
    cv.hline(18, 21, 40, CRIM[3], emit=True)
    cv.flame(19.5, 41, 6, CRIM, width=1, mx=True)
    return cv


def ship_speed():
    left = poly([(24, 3), (12, 20), (12, 37), (16, 25), (20, 32), (24, 25)])
    mask = mirror_x(left)
    nose = box(0, 0, 47, 7) & mask
    fins = box(0, 27, 47, 47) & mask
    stripe = mirror_x(box(23, 19, 23, 24)) & mask
    img, _ = shade(mask, [(nose, AMBR), (fins, AMBR), (stripe, AMBR)])
    cv = Canvas(img)

    # canopy (arched window from the sketch)
    canopy = box(21, 12, 26, 17)
    canopy[12, 21] = canopy[12, 26] = False
    glass_dome(cv, canopy)
    cv.hline(21, 26, 18, HULL[1], mx=False)
    # side intakes
    cv.vline(13, 21, 24, OUTLINE_DARK); cv.vline(14, 22, 24, HULL[1])
    # speed lines on hull
    cv.vline(18, 14, 18, HULL[4])
    # thrusters in the three notches
    cv.flame(16, 27, 6, AMBR, width=0, mx=True)
    cv.flame(23.5, 27, 12, AMBR, width=1)
    return cv


# ---------------------------------------------------------------- output
def upscale(arr, k):
    return Image.fromarray(arr).resize((arr.shape[1] * k, arr.shape[0] * k), Image.NEAREST)


def starfield(w, h, seed=7):
    rng = np.random.default_rng(seed)

    def noise(scale):
        sw, sh = w // scale + 2, h // scale + 2
        small = Image.fromarray((rng.random((sh, sw)) * 255).astype(np.uint8))
        return np.array(small.resize((sw * scale, sh * scale), Image.BICUBIC))[:h, :w] / 255.0

    n = 0.55 * noise(40) + 0.3 * noise(16) + 0.15 * noise(6)
    hue = noise(60)
    bayer = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 - 0.5
    purple = [hexc(c) for c in ['#06051a', '#0d0a2b', '#1b1242', '#2d1b5c', '#45286f']]
    teal = [hexc(c) for c in ['#06051a', '#081530', '#0d2646', '#123c5c', '#1d5a72']]
    img = np.zeros((h, w, 4), np.uint8)
    for y in range(h):
        for x in range(w):
            v = (n[y, x] - 0.35) * 7 + bayer[y % 4, x % 4]
            lvl = int(np.clip(np.round(v), 0, 4))
            img[y, x] = (teal if hue[y, x] > 0.55 else purple)[lvl]
    # planet (Sea-of-Stars-ish banded gas giant, cropped at the corner)
    px, py, r = w + 2, h + 10, 30
    bands = [hexc(c) for c in ['#1a1035', '#3a1d52', '#6a2f6a', '#a24a6e', '#e07a6a', '#ffc08a']]
    for y in range(h):
        for x in range(w):
            dx, dy = x - px, y - py
            if dx * dx + dy * dy <= r * r:
                lit = (-dx * 0.7 - dy * 0.7) / r
                band = 0.25 * np.sin(y * 0.55 + dx * 0.08)
                lvl = int(np.clip(np.round(2.2 + 2.4 * lit + band + bayer[y % 4, x % 4] * 0.8), 0, 5))
                img[y, x] = bands[lvl]
    stars = [hexc(c) for c in ['#ffffff', '#cfd8ff', '#ffe6bd', '#8a8fc4', '#5c6096']]
    for _ in range(int(w * h / 55)):
        x, y = rng.integers(0, w), rng.integers(0, h)
        if img[y, x][0] < 60:
            img[y, x] = stars[rng.choice(5, p=[0.12, 0.18, 0.1, 0.3, 0.3])]
    for _ in range(7):
        x, y = rng.integers(3, w - 3), rng.integers(3, h - 3)
        if 62 <= y <= 80:  # keep the label band clean
            continue
        img[y, x] = stars[0]
        for d in (1, 2):
            c = stars[1] if d == 1 else stars[3]
            for a, b in ((x + d, y), (x - d, y), (x, y + d), (x, y - d)):
                img[b, a] = c
    return img


def main():
    ships = [("parry", "PARRY", "Escudo / contraataque", ship_parry()),
             ("shooter", "SHOOTER", "Potencia de fuego", ship_shooter()),
             ("speed", "SPEED", "Velocidad / esquiva", ship_speed())]
    game_dir = os.path.join(OUT, "..", "..", "Assets", "Resources", "Ships")  # sprites que carga el juego
    for key, _, _, cv in ships:
        Image.fromarray(cv.img).save(os.path.join(OUT, f"ship_{key}.png"))
        if os.path.isdir(game_dir):
            Image.fromarray(cv.img).save(os.path.join(game_dir, f"ship_{key}.png"))
        upscale(cv.img, 8).save(os.path.join(OUT, f"ship_{key}_x8.png"))

    # showcase: low-res scene, nearest upscale, then HD-2D style bloom on top
    K, SW, SH = 6, 200, 96
    scene = starfield(SW, SH)
    emit = np.zeros_like(scene)
    xs = [12, 76, 140]
    for (key, _, _, cv), x0 in zip(ships, xs):
        y0 = 18
        a = cv.img[..., 3:4] / 255.0
        region = scene[y0:y0 + H, x0:x0 + W]
        region[..., :3] = (cv.img[..., :3] * a + region[..., :3] * (1 - a)).astype(np.uint8)
        emit[y0:y0 + H, x0:x0 + W] = np.maximum(emit[y0:y0 + H, x0:x0 + W], cv.emit)
    big = upscale(scene, K).convert('RGB')
    glow_src = upscale(emit, K).convert('RGB')
    glow = np.array(glow_src.filter(ImageFilter.GaussianBlur(14)), float) * 1.6 \
        + np.array(glow_src.filter(ImageFilter.GaussianBlur(4)), float) * 0.5
    out = np.clip(np.array(big, float) + glow, 0, 255).astype(np.uint8)
    sheet = Image.fromarray(out)

    d = ImageDraw.Draw(sheet)
    try:
        f1 = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 26)
        f2 = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 16)
    except OSError:
        f1 = f2 = ImageFont.load_default()
    for (_, name, sub, _), x0 in zip(ships, xs):
        cx = (x0 + W // 2) * K
        d.text((cx, 70 * K), name, font=f1, fill=(238, 244, 255), anchor="mm")
        d.text((cx, 70 * K + 26), sub, font=f2, fill=(150, 160, 210), anchor="mm")
    sheet.save(os.path.join(OUT, "ship_concepts_sheet.png"))


if __name__ == "__main__":
    main()
