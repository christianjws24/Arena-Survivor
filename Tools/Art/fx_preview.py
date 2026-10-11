"""Vista previa de los efectos a escala real (30 px por unidad), teñidos con los colores que usa el juego."""
import random
import sys
from PIL import Image, ImageDraw

fx, root, out = sys.argv[1], sys.argv[2], sys.argv[3]


def load(path):
    return Image.open(path).convert('RGBA')


def tint(img, rgb, alpha=1.0):
    r, g, b = rgb
    px = [(int(p[0] * r), int(p[1] * g), int(p[2] * b), int(p[3] * alpha)) for p in img.getdata()]
    t = Image.new('RGBA', img.size)
    t.putdata(px)
    return t


def scaled(img, factor):
    return img.resize((max(1, round(img.width * factor)), max(1, round(img.height * factor))), Image.NEAREST)


W, H = 1000, 300
bg = Image.new('RGBA', (W, H), (13, 12, 30, 255))
rnd = random.Random(5)
dr = ImageDraw.Draw(bg)
for _ in range(220):
    dr.point((rnd.randrange(W), rnd.randrange(H)), fill=rnd.choice([(60, 58, 110, 255), (120, 130, 190, 255), (230, 235, 255, 255)]))


def at(img, cx, cy):
    bg.alpha_composite(img, (int(cx - img.width / 2), int(cy - img.height / 2)))


CYAN = (0.3, 0.9, 1.0)
# 1) Custodius con su escudo (7.2 u = 216 px)
for name, dx in (('custodius_generator_left', -81), ('custodius_generator_right', 81), ('custodius_core', 0)):
    at(load(f'{root}/Assets/Resources/Bosses/{name}.png'), 120 + dx, 150)
at(tint(load(f'{fx}/fx_shield_boss.png'), (0.2, 0.9, 1.0), 0.7), 120, 150)

# 2) Zona de anclaje defensiva (6.4 u = 192 px) con el temporizador a media duración
yellow = (0.95, 0.8, 0.2)
at(tint(load(f'{fx}/fx_disc.png'), yellow, 0.12), 345, 150)
at(tint(scaled(load(f'{fx}/fx_disc.png'), 0.55), yellow, 0.16), 345, 150)
at(tint(load(f'{fx}/fx_ring_anchor.png'), yellow, 0.85), 345, 150)
at(tint(scaled(load(f'{fx}/fx_ring.png'), 192 / 216 * 0.55), yellow, 0.9), 345, 150)
ship = load(f'{root}/Assets/Resources/Ships/ship_parry.png')
at(ship, 345, 150)

# 3) Jugador con Energy Shield y anillo de tensión
at(load(f'{root}/Assets/Resources/Ships/ship_speed.png'), 520, 80)
at(tint(load(f'{fx}/fx_shield_player.png'), CYAN, 0.8), 520, 80)
at(load(f'{root}/Assets/Resources/Ships/ship_shooter.png'), 520, 210)
at(tint(scaled(load(f'{fx}/fx_ring_tension.png'), 0.8), (0.3, 0.95, 1.0), 0.9), 520, 210)

# 4) Anomalía gravitacional, satélites y botiquín
at(load(f'{fx}/fx_vortex.png'), 630, 80)
for i, (dx, dy) in enumerate(((0, -55), (48, 28), (-48, 28))):
    at(scaled(load(f'{fx}/fx_satellite.png'), 0.42 * 30 / 13), 630 + dx, 220 + dy)
at(load(f'{root}/Assets/Resources/Ships/ship_parry.png'), 630, 220)
at(scaled(load(f'{fx}/fx_medkit.png'), 0.95 * 30 / 28), 700, 150)

# 5) Explosión (destello + chispas) y onda expansiva en tres momentos
orange = (1.0, 0.45, 0.25)
for f in range(4):
    at(tint(scaled(load(f'{fx}/fx_flash_{f}.png'), 1.3), (1.0, 0.72, 0.62)), 760 + f * 40, 40)
sheet = load(f'{fx}/fx_sparks_sheet.png')
for f in range(3):
    s = sheet.crop((f * 7, 0, f * 7 + 7, 7))
    for k in range(5):
        at(tint(s, orange), 762 + f * 60 + (k % 3) * 12, 85 + (k // 3) * 12)
for i, f in enumerate((1, 3, 6)):
    at(tint(scaled(load(f'{fx}/fx_shockwave_{f}.png'), 0.35), (1, 1, 1), 1.0 - f / 8), 790 + i * 75, 220)

bg.resize((W * 2, H * 2), Image.NEAREST).save(out)
print('ok')
