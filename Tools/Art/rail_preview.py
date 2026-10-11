"""Simula el efecto del Rail Gun a 60 fps (rayo repetido + destello de boca), como lo reproduce RailgunEffect.
Uso: python rail_preview.py <carpeta_fx> <raíz_proyecto> <salida.gif> [<salida_fotogramas.png>]"""
import math
import random
import sys
from PIL import Image, ImageDraw

fx, root, out_gif = sys.argv[1], sys.argv[2], sys.argv[3]
out_strip = sys.argv[4] if len(sys.argv) > 4 else None

beam = []
k = 0
while True:
    try:
        beam.append(Image.open(f'{fx}/fx_rail_beam_{k}.png').convert('RGBA'))
    except FileNotFoundError:
        break
    k += 1
caps = [Image.open(f'{fx}/fx_rail_cap_{i}.png').convert('RGBA') for i in range(len(beam))]
muzzle = [Image.open(f'{fx}/fx_rail_muzzle_{i}.png').convert('RGBA') for i in range(4)]
ship = Image.open(f'{root}/Assets/Resources/Ships/ship_shooter.png').convert('RGBA').rotate(-90)

W, H = 540, 200
rnd = random.Random(2)
stars = [(rnd.randrange(W), rnd.randrange(H), rnd.choice([(60, 58, 110), (120, 130, 190), (230, 235, 255)])) for _ in range(140)]
ox, oy = 60, H // 2
DURATION, FPS = 0.4, 60
MUZZLE_FRAME = 0.05


def alpha_mul(img, a):
    if a >= 1:
        return img
    r, g, b, al = img.split()
    al = al.point(lambda v: int(v * a))
    return Image.merge('RGBA', (r, g, b, al))


def frame_at(time):
    bg = Image.new('RGBA', (W, H), (13, 12, 30, 255))
    d = ImageDraw.Draw(bg)
    for x, y, c in stars:
        d.point((x, y), fill=c + (255,))
    bg.alpha_composite(ship, (ox - ship.width // 2 - 22, oy - ship.height // 2))
    t = time / DURATION
    if t <= 1:
        idx = min(len(beam) - 1, int(t * len(beam)))
        seg = beam[idx]
        a = 1.0 if t < 0.7 else 1 - (t - 0.7) / 0.3
        strip = Image.new('RGBA', (W - ox, seg.height))
        for x in range(0, W - ox, seg.width):
            strip.alpha_composite(seg, (x, 0))
        bg.alpha_composite(alpha_mul(strip, a), (ox, oy - seg.height // 2))
        cap = caps[idx]
        bg.alpha_composite(alpha_mul(cap, a), (ox - cap.width, oy - cap.height // 2))
    m = int(time / MUZZLE_FRAME)
    if m < len(muzzle):
        bg.alpha_composite(muzzle[m], (ox - 48, oy - 48))
    return bg


frames = [frame_at(i / FPS) for i in range(int(DURATION * FPS) + 10)]
big = [f.resize((W * 2, H * 2), Image.NEAREST).convert('P', palette=Image.ADAPTIVE) for f in frames]
big[0].save(out_gif, save_all=True, append_images=big[1:], duration=1000 // FPS, loop=0)

if out_strip:
    picks = [0.0, 0.06, 0.15, 0.25, 0.33]
    strip = Image.new('RGBA', (W, H * len(picks)))
    for i, p in enumerate(picks):
        strip.paste(frame_at(p), (0, i * H))
    strip.resize((W * 2, H * 2 * len(picks)), Image.NEAREST).save(out_strip)
print(len(frames), 'fotogramas')
