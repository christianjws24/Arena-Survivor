"""Monta el jefe completo (núcleo + generadores en ±81 px) junto a una nave del jugador, a la misma densidad de píxel."""
import random
import sys
from PIL import Image, ImageDraw

d, ship_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
core = Image.open(f'{d}/custodius_core.png')
wl = Image.open(f'{d}/custodius_generator_left.png')
wr = Image.open(f'{d}/custodius_generator_right.png')
ship = Image.open(ship_path).convert('RGBA')

W, H = 300, 170
bg = Image.new('RGBA', (W, H), (13, 12, 30, 255))
rnd = random.Random(3)
dr = ImageDraw.Draw(bg)
for _ in range(90):
    x, y = rnd.randrange(W), rnd.randrange(H)
    dr.point((x, y), fill=rnd.choice([(60, 58, 110, 255), (120, 130, 190, 255), (230, 235, 255, 255)]))

bx, by = 120, 85  # centro del jefe
# anillo del escudo (diámetro 7.2 u de mundo = 216 px) solo como guía tenue
dr.ellipse((bx - 108, by - 108, bx + 108, by + 108), outline=(50, 200, 230, 120))


def paste_center(img, x, y):
    bg.alpha_composite(img, (x - img.width // 2, y - img.height // 2))


paste_center(wl, bx - 81, by)
paste_center(wr, bx + 81, by)
paste_center(core, bx, by)
paste_center(ship, 262, 120)
bg.resize((W * 4, H * 4), Image.NEAREST).save(out_path)
print('ok')
