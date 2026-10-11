"""Pixel art de las balas (jugador y enemigos), con la paleta de las naves.

Escala: 30 px por unidad de mundo, igual que las naves. Todas las balas apuntan a +X (así viajan en Unity).
Cada bala se define con una forma (gota, cápsula, círculo o rombo) y una rampa de 5 tonos:
halo semitransparente -> borde -> medio -> claro -> núcleo.
Uso: python bullets.py <carpeta_salida>
"""
import math
import sys
from PIL import Image


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


# halo, borde, medio, claro, núcleo
GOLD = ['bd651b', 'efa02b', 'ffd65e', 'fff7cc', 'ffffff']      # disparo automático y abanico (amarillo)
MINT = ['1f8a5a', '3cd38a', '7ff0b0', 'd4ffe6', 'ffffff']      # ráfaga láser (verde)
AMBER = ['a5331b', 'ff7a2e', 'ffab5c', 'ffe2bb', 'ffffff']     # escopeta láser (naranja)
HOSTILE = ['6a1430', 'c81e4a', 'ff4d6a', 'ffb3c1', 'fff1f4']   # balas enemigas (carmesí)
PARRIED = ['0f6474', '169ea6', '3cd3c6', '9bf5e4', 'f0fffb']   # bala enemiga reflejada con Parry (cian)
HOSTILE_RIM = '2a0718'
PARRIED_RIM = '0a3346'


def render(w, h, field, ramp, glow=0.45, rim=None, fade=None):
    """field(px, py) -> distancia normalizada (0 centro, 1 borde). fade(px) resta brillo (cola)."""
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    for y in range(h):
        for x in range(w):
            d = field(x + 0.5, y + 0.5)
            if d is None:
                continue
            k = 4 if d < 0.34 else 3 if d < 0.6 else 2 if d < 0.82 else 1 if d <= 1.0 else 0 if d <= 1.32 else -1
            if rim and 1.32 < d <= 1.6:
                img.putpixel((x, y), hexc(rim, 200))
                continue
            if k < 0:
                continue
            if fade:
                k = max(0, k - fade(x + 0.5))
            if k == 0:
                img.putpixel((x, y), hexc(ramp[0], int(255 * glow)))
            else:
                img.putpixel((x, y), hexc(ramp[k]))
    return img


def teardrop(w, h, r, tail):
    """Cabeza redonda a la derecha y cola que se afina hacia la izquierda."""
    cy = h / 2.0
    hx = w - r - 1.6

    def f(x, y):
        if x >= hx:
            return math.hypot(x - hx, y - cy) / r
        t = (hx - x) / tail
        if t > 1.0:
            return None
        rad = r * (1 - t) + 0.45 * t
        return abs(y - cy) / rad
    return f, (lambda x: 0 if x >= hx else int(((hx - x) / tail) * 2.2))


def capsule(w, h, r):
    cy = h / 2.0
    x0, x1 = r + 1.4, w - r - 1.4

    def f(x, y):
        cx = min(max(x, x0), x1)
        return math.hypot(x - cx, y - cy) / r
    return f


def circle(w, h, r):
    cx, cy = w / 2.0, h / 2.0
    return lambda x, y: math.hypot(x - cx, y - cy) / r


def diamond(w, h, rx, ry):
    cx, cy = w / 2.0, h / 2.0
    return lambda x, y: abs(x - cx) / rx + abs(y - cy) / ry


def main():
    out = sys.argv[1]
    sprites = {}

    # --- Jugador ---
    f, fade = teardrop(17, 9, 3.0, 10.0)
    sprites['bullet_player'] = render(17, 9, f, GOLD, fade=fade)
    sprites['bullet_laser_burst'] = render(40, 7, capsule(40, 7, 2.0), MINT)
    sprites['bullet_fan'] = render(13, 9, diamond(13, 9, 5.0, 3.4), GOLD)
    sprites['bullet_shotgun'] = render(26, 6, capsule(26, 6, 1.7), AMBER)

    # --- Enemigos (2 fotogramas: el segundo late un poco más grande y brillante) ---
    for tag, ramp, rim in (('hostile', HOSTILE, HOSTILE_RIM), ('parried', PARRIED, PARRIED_RIM)):
        sprites[f'enemy_orb_{tag}_0'] = render(17, 17, circle(17, 17, 5.0), ramp, rim=rim)
        sprites[f'enemy_orb_{tag}_1'] = render(17, 17, circle(17, 17, 5.6), ramp, glow=0.6, rim=rim)
        sprites[f'enemy_laser_{tag}_0'] = render(22, 9, capsule(22, 9, 2.1), ramp, rim=rim)
        sprites[f'enemy_laser_{tag}_1'] = render(22, 9, capsule(22, 9, 2.5), ramp, glow=0.6, rim=rim)

    for name, img in sprites.items():
        img.save(f'{out}/{name}.png')
    print(len(sprites), 'sprites')


if __name__ == '__main__':
    main()
