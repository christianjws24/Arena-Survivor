"""Pixel art de los efectos visuales (explosiones, ondas, escudos, zonas y power-ups), con la paleta de las naves.

Densidad: 30 px por unidad de mundo en el tamaño en que se ve cada efecto (igual que naves y balas).
Cada sprite define sus PPU para que sus 'bounds' coincidan con el sprite provisional que reemplaza
(1 unidad para los de ProceduralSprites, 2 para Ring.png), así el código conserva sus escalas.
Los sprites "tintables" son blancos/grises: el código les pone el color (cian del parry, color de la zona...).

Uso: python fx.py <carpeta_salida> <meta_plantilla.png.meta>
Crea/actualiza los .meta (conserva el GUID si ya existe; solo cambia los PPU).
"""
import math
import os
import re
import sys
import uuid
from PIL import Image


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def grey(v, a=255):
    c = int(255 * v)
    return (c, c, c, a)


OUT = '0a0717'
HULL = ['1c1a36', '251c4a', '35335f', '55598f', '7f8bbd', 'b4c2e2', 'eef4ff']
TEAL = ['0a3346', '0f6474', '169ea6', '3cd3c6', '9bf5e4', 'f0fffb']
MINT = ['0f3a2a', '1f8a5a', '3cd38a', '7ff0b0', 'd4ffe6', 'ffffff']
LIME = ['22380a', '4a7a10', '8fd11e', 'c8ff3a', 'f4ffc8']

MAGENTA = ['3a0a3a', '7a1a72', 'c42fb8', 'ff5cf0', 'ffb3fa', 'ffffff']  # Rail Gun

SPRITES = {}  # nombre -> (imagen, ppu, malla Full Rect)


def add(name, img, ppu, full_rect=False):
    """full_rect: malla rectangular, necesaria para dibujar el sprite en modo Tiled (repetido)."""
    SPRITES[name] = (img, ppu, full_rect)


def canvas(n):
    return Image.new('RGBA', (n, n), (0, 0, 0, 0))


def polar(n):
    """Itera píxeles con (x, y, distancia al centro, ángulo). El ángulo 0 apunta a la derecha y crece hacia abajo."""
    c = n / 2.0
    for y in range(n):
        for x in range(n):
            dx, dy = x + 0.5 - c, y + 0.5 - c
            yield x, y, math.hypot(dx, dy), math.atan2(dy, dx)


def lit(ang):
    """1 en el arco superior-izquierdo (luz de las naves), -1 abajo-derecha."""
    return math.cos(ang - math.radians(225))


def checker(x, y, density):
    """Tramado ordenado 4x4 (Bayer): devuelve True para la fracción 'density' de los píxeles."""
    bayer = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
    return bayer[y % 4][x % 4] < density * 16


# ---------------------------------------------------------------- explosión
def sparks():
    """Partículas de la explosión: chispa en cruz -> rombo -> punto (blancas, las tiñe el color de la explosión)."""
    shapes = [
        ["...w...", "...w...", "..lwl..", "wwwWwww", "..lwl..", "...w...", "...w..."],
        [".......", ".......", "...w...", "..wWw..", "...w...", ".......", "......."],
        [".......", ".......", ".......", "...W...", ".......", ".......", "......."],
    ]
    pal = {'W': grey(1.0), 'w': grey(0.85), 'l': grey(0.6, 200)}
    # Una sola tira de 3 fotogramas: el módulo Texture Sheet Animation (modo Grid) la recorre durante la vida de la partícula
    sheet = Image.new('RGBA', (7 * len(shapes), 7), (0, 0, 0, 0))
    for i, rows in enumerate(shapes):
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch in pal:
                    sheet.putpixel((i * 7 + x, y), pal[ch])
    add('fx_sparks_sheet', sheet, 30)


def flash():
    """Destello central de la explosión (4 fotogramas, 1 unidad)."""
    n = 32
    for f in range(4):
        img = canvas(n)
        for x, y, d, a in polar(n):
            if f == 0:
                spike = (abs(x + 0.5 - n / 2) < 1 or abs(y + 0.5 - n / 2) < 1) and d < 11
                if d < 6.5 or spike:
                    img.putpixel((x, y), grey(1.0 if d < 4.5 else 0.85))
            elif f == 1:
                if d < 3.5:
                    img.putpixel((x, y), grey(1.0))
                elif 8 <= d < 11:
                    img.putpixel((x, y), grey(1.0 if d >= 10 else 0.8))
            elif f == 2:
                if 11 <= d < 13.5 and checker(x, y, 0.75 if d >= 12.5 else 0.5):
                    img.putpixel((x, y), grey(0.9))
            else:
                if 13.5 <= d < 15 and checker(x, y, 0.3):
                    img.putpixel((x, y), grey(0.75))
        add(f'fx_flash_{f}', img, n)


# ----------------------------------------------------------- onda expansiva
SHOCKWAVE_FRAMES = 12


def shockwave():
    """Onda del Parry / Estallido Nova: 12 fotogramas de 1.6 a 9 unidades (ease-out cúbico, como ShockwaveEffect).
    Cada lienzo mide exactamente el diámetro del aro: el código escala el fotograma al diámetro de cada instante,
    así la expansión es continua y los píxeles apenas cambian de tamaño entre fotogramas."""
    frames = SHOCKWAVE_FRAMES
    for k in range(frames):
        # Repartidos por diámetro (no por tiempo): todos los saltos de tamaño entre fotogramas son iguales y pequeños
        ease = k / (frames - 1.0)
        t = 1 - (1 - ease) ** (1 / 3.0)  # instante en que la onda (ease-out cúbico) tiene ese diámetro
        n = int(round((1.6 + (9.0 - 1.6) * ease) * 30))
        r = n / 2.0
        thick = max(1.0, 5.0 * (1 - t) + 0.6)
        trail = 2 + int(6 * t)
        img = canvas(n)
        for x, y, d, a in polar(n):
            if r - thick <= d < r:
                edge = d >= r - 1
                col = hexc('ffffff') if edge else hexc(TEAL[4] if lit(a) > -0.3 else TEAL[3])
                img.putpixel((x, y), col)
            elif r - thick - trail <= d < r - thick:
                depth = (r - thick - d) / trail  # 0 junto al aro, 1 al final de la estela
                if checker(x, y, 0.55 * (1 - depth)):
                    img.putpixel((x, y), hexc(TEAL[3], 200))
        add(f'fx_shockwave_{k}', img, 30)


# ------------------------------------------------------------------ anillos
def ring_generic():
    """Anillo fino genérico (1 unidad, para 7.2 u): recarga del escudo del jefe y temporizador de zonas."""
    n, r = 216, 107.5
    img = canvas(n)
    for x, y, d, a in polar(n):
        if r - 2 <= d < r:
            img.putpixel((x, y), grey(1.0 if lit(a) > -0.4 else 0.85))
        elif r - 3 <= d < r - 2:
            img.putpixel((x, y), grey(0.65))
    add('fx_ring', img, n)


def disc_generic():
    """Disco relleno con el borde tramado (1 unidad, para 6.4 u)."""
    n, r = 192, 95.5
    img = canvas(n)
    for x, y, d, a in polar(n):
        if d < r - 6:
            img.putpixel((x, y), grey(1.0))
        elif d < r and checker(x, y, (r - d) / 6.0):
            img.putpixel((x, y), grey(1.0))
    add('fx_disc', img, n)


def ring_tension():
    """Anillo de tensión del Parry: segmentado, se contrae de 4 a 2 unidades (sustituye a Ring.png: 2 unidades)."""
    n, r = 120, 59
    img = canvas(n)
    seg = 16
    for x, y, d, a in polar(n):
        if r - 3 <= d < r:
            phase = ((a + math.pi) / (2 * math.pi)) * seg
            if phase % 1.0 < 0.72:  # huecos entre segmentos
                img.putpixel((x, y), grey(1.0 if d >= r - 1 or lit(a) > 0.3 else 0.8))
    add('fx_ring_tension', img, n / 2.0)


def ring_anchor():
    """Borde de las zonas de anclaje (1 unidad, para 6.4 u): aro con marcas de dial y 4 rombos."""
    n, r = 192, 95.5
    img = canvas(n)
    for x, y, d, a in polar(n):
        deg = (math.degrees(a) + 360) % 360
        if r - 2 <= d < r:
            img.putpixel((x, y), grey(1.0 if lit(a) > -0.4 else 0.85))
        elif r - 6 <= d < r - 2 and (deg % 15 < 1.6 or (deg + 1.6) % 15 < 1.6) and deg % 90 > 3 and deg % 90 < 87:
            img.putpixel((x, y), grey(0.75))
    for k in range(4):  # rombos en los puntos cardinales
        ang = k * math.pi / 2
        cx = n / 2 + (r - 6) * math.cos(ang)
        cy = n / 2 + (r - 6) * math.sin(ang)
        for dy in range(-5, 6):
            for dx in range(-5, 6):
                if abs(dx) + abs(dy) <= 4:
                    px, py = int(cx + dx), int(cy + dy)
                    if 0 <= px < n and 0 <= py < n:
                        img.putpixel((px, py), grey(1.0 if abs(dx) + abs(dy) <= 1 else 0.8))
    add('fx_ring_anchor', img, n)


HEX_DIRS = [(math.cos(math.radians(60 * i)), math.sin(math.radians(60 * i))) for i in range(3)]


def hex_line(x, y, size):
    """True si el píxel cae sobre una arista de una rejilla hexagonal (hexágonos de 'size' px de radio interior)."""
    # Centros de la rejilla: base de vectores a 2*size de distancia (hexágonos con lados verticales)
    w = 2 * size
    bx = (w, 0.0)
    by = (w / 2.0, w * math.sqrt(3) / 2.0)
    # coordenadas en la base, y prueba de los 4 centros vecinos más cercanos
    det = bx[0] * by[1] - bx[1] * by[0]
    u = (x * by[1] - y * by[0]) / det
    v = (bx[0] * y - bx[1] * x) / det
    best = 1e9
    for du in (0, 1):
        for dv in (0, 1):
            cu, cv = math.floor(u) + du, math.floor(v) + dv
            cx = cu * bx[0] + cv * by[0]
            cy = cu * bx[1] + cv * by[1]
            px, py = x - cx, y - cy
            dist = max(abs(px * d[0] + py * d[1]) for d in HEX_DIRS)  # métrica hexagonal: radio interior
            best = min(best, dist)
    return best > size - 1.0


def shield_boss():
    """Escudo de Custodius (1 unidad, para 7.2 u = el tamaño real de su collider): aro + panal hexagonal tenue."""
    n, r = 216, 107.5
    img = canvas(n)
    for x, y, d, a in polar(n):
        if r - 3 <= d < r:
            v = 1.0 if d >= r - 1 else (0.9 if lit(a) > -0.2 else 0.75)
            img.putpixel((x, y), grey(v))
        elif r - 9 <= d < r - 3:
            if checker(x, y, 0.5 * (d - (r - 9)) / 6.0):
                img.putpixel((x, y), grey(0.8, 220))
        elif d < r - 3 and hex_line(x + 0.5 - n / 2, y + 0.5 - n / 2, 9):
            fade = 0.15 + 0.85 * (d / r) ** 3  # el panal casi solo se ve hacia el borde
            img.putpixel((x, y), grey(0.9, int(70 * fade)))
    # brillos especulares arriba-izquierda
    for x, y, d, a in polar(n):
        if r - 7 <= d < r - 5 and -0.10 < (a + math.radians(135)) < 0.22:
            img.putpixel((x, y), grey(1.0))
    add('fx_shield_boss', img, n)


def shield_player():
    """Energy Shield del jugador (1 unidad, para 1.9 u)."""
    n, r = 57, 28.0
    img = canvas(n)
    for x, y, d, a in polar(n):
        if r - 2 <= d < r:
            img.putpixel((x, y), grey(1.0 if lit(a) > -0.3 else 0.8))
        elif r - 4 <= d < r - 2 and checker(x, y, 0.35):
            img.putpixel((x, y), grey(0.8, 200))
        elif r - 6 <= d < r - 4 and -0.15 < (a + math.radians(135)) < 0.35:
            img.putpixel((x, y), grey(1.0))
    add('fx_shield_player', img, n)


# ---------------------------------------------------------------- power-ups
def vortex():
    """Anomalía Gravitacional: espiral lima de 3 brazos con núcleo de vacío (sustituye a Ring.png, a 3 u). Sin tinte."""
    n = 90
    r = 44.5
    img = canvas(n)
    for x, y, d, a in polar(n):
        if d >= r:
            continue
        if d < 13:
            # núcleo: vacío con borde violeta oscuro
            img.putpixel((x, y), hexc(OUT) if d < 11 else hexc(HULL[1]))
            continue
        arms = 3
        phase = (a * arms + math.log(d) * 5.2) % (2 * math.pi)
        width = 1.25 - 0.5 * (d / r)
        on = phase < width or phase > 2 * math.pi - 0.15
        near = phase < width + 0.45
        if on:
            k = 4 if d < 18 else 3 if d < 27 else 2 if d < 36 else 1
            img.putpixel((x, y), hexc(LIME[k], 255 if d < 38 else 170))
        elif near and checker(x, y, 0.45 * (1 - d / r)):
            img.putpixel((x, y), hexc(LIME[1], 180))
        elif d < 20 and checker(x, y, 0.5):
            img.putpixel((x, y), hexc(HULL[0]))
    # aro exterior tenue
    for x, y, d, a in polar(n):
        if r - 1.5 <= d < r and checker(x, y, 0.5):
            img.putpixel((x, y), hexc(LIME[2], 150))
    add('fx_vortex', img, n / 2.0)


def satellite():
    """Orbe del Módulo Satélite (1 unidad, para 0.42 u). Sin tinte."""
    n = 13
    img = canvas(n)
    ramp = [MINT[1], MINT[2], MINT[3], MINT[4], MINT[5]]
    for x, y, d, a in polar(n):
        dd = math.hypot(x + 0.5 - n / 2 + 0.8, y + 0.5 - n / 2 + 0.8)  # brillo desplazado arriba-izquierda
        if d < 5.2:
            k = 4 if dd < 1.6 else 3 if dd < 3.0 else 2 if d < 3.8 else 1
            img.putpixel((x, y), hexc(ramp[k]))
        elif d < 6.5:
            img.putpixel((x, y), hexc(MINT[1], 150))
    add('fx_satellite', img, n)


def medkit():
    """Botiquín (1 unidad): cápsula clara con cruz verde y halo verde tramado. Sin tinte."""
    n = 28
    img = canvas(n)
    c = n / 2.0
    for y in range(n):
        for x in range(n):
            dx, dy = abs(x + 0.5 - c), abs(y + 0.5 - c)
            body = (max(dx - 6, 0) ** 2 + max(dy - 6, 0) ** 2) <= 9  # cuadrado de esquinas redondas
            rim = (max(dx - 6, 0) ** 2 + max(dy - 6, 0) ** 2) <= 16
            glow = (max(dx - 6, 0) ** 2 + max(dy - 6, 0) ** 2) <= 42
            if body:
                v = HULL[6] if (x + y) < n - 4 else HULL[5]
                if dx >= 8 or dy >= 8:
                    v = HULL[4] if (x + y) >= n else HULL[5]
                img.putpixel((x, y), hexc(v))
            elif rim:
                img.putpixel((x, y), hexc(OUT))
            elif glow and checker(x, y, 0.5):
                img.putpixel((x, y), hexc(MINT[2], 160))
    # cruz
    for y in range(n):
        for x in range(n):
            dx, dy = x + 0.5 - c, y + 0.5 - c
            if (abs(dx) <= 1.5 and abs(dy) <= 5.5) or (abs(dy) <= 1.5 and abs(dx) <= 5.5):
                hi = dx < 0 and dy < 0
                img.putpixel((x, y), hexc(MINT[3] if hi else MINT[2]))
            elif (abs(dx) <= 2.5 and abs(dy) <= 6.5) or (abs(dy) <= 2.5 and abs(dx) <= 6.5):
                img.putpixel((x, y), hexc(MINT[1]))
    img.putpixel((int(c - 5), int(c - 1)), hexc(MINT[4]))
    add('fx_medkit', img, n)


# ----------------------------------------------------------------- Rail Gun
RAIL_FRAMES = 8
RAIL_TILE = 32  # periodo horizontal: el tramo se repite a lo largo del rayo (modo Tiled)


def hash01(x, y, seed):
    """Ruido determinista 0..1 (periódico en x con el tramo, para que la repetición no tenga costuras)."""
    v = ((x % RAIL_TILE) * 73856093) ^ (y * 19349663) ^ (seed * 83492791)
    v = (v ^ (v >> 13)) * 1274126177
    return ((v ^ (v >> 16)) & 0xFFFF) / 65535.0


RAIL_HEIGHTS = [90, 84, 72, 58, 44, 30, 18, 8]
RAIL_CAP = 16  # largo de la punta redondeada en la boca


def rail_color(d, x, y, k, t):
    """Color del rayo a una distancia relativa 'd' del eje (0 centro, 1 borde) o None si es transparente."""
    core = max(0.1, 0.26 * (1 - t))
    light = core + 0.16
    mid = light + 0.26
    # Vetas: trazos horizontales que avanzan en cada fotograma (sensación de flujo a lo largo del rayo)
    streak = ((x + y * 7 + k * 6) % RAIL_TILE) < 4 + (y % 3) * 2
    if d < core:
        return hexc(MAGENTA[5] if t < 0.6 else MAGENTA[4])
    if d < light:
        return hexc(MAGENTA[5] if streak and t < 0.6 else MAGENTA[4])
    if d < mid:
        if streak:
            return hexc(MAGENTA[4] if t < 0.6 else MAGENTA[3])
        return hexc(MAGENTA[3] if t < 0.6 else MAGENTA[2])
    if d < 0.9:
        if hash01(x, y, k) > 0.9:  # chispitas de energía en el halo
            return hexc(MAGENTA[4])
        return hexc(MAGENTA[2], 230) if (x + y + k) % 2 == 0 or t < 0.3 else hexc(MAGENTA[1], 200)
    if d <= 1.0 and checker(x, y, 0.6 * (1 - t)):  # borde tramado que se deshace al final
        return hexc(MAGENTA[1], 180)
    return None


def rail_beam():
    """Tramo del rayo del Rail Gun, 8 fotogramas: del impacto (3 u de ancho, núcleo blanco y doble hélice)
    a un hilo que se apaga. Se dibuja repetido en horizontal a lo largo de los 45 u del rayo (modo Tiled).
    También genera la punta redondeada que tapa el corte recto en la boca."""
    for k in range(RAIL_FRAMES):
        t = k / (RAIL_FRAMES - 1.0)
        h = RAIL_HEIGHTS[k]
        c = h / 2.0
        img = Image.new('RGBA', (RAIL_TILE, h), (0, 0, 0, 0))
        for y in range(h):
            for x in range(RAIL_TILE):
                col = rail_color(abs(y + 0.5 - c) / c, x, y, k, t)
                if col:
                    img.putpixel((x, y), col)

        # Doble hélice (gira: la fase avanza en cada fotograma) con sombra para leerse sobre el blanco
        if t < 0.8:
            amp = c * (0.62 - 0.1 * t)
            for x in range(RAIL_TILE):
                for strand in (0.0, math.pi):
                    phase = 2 * math.pi * x / RAIL_TILE + strand + k * 0.9
                    py = int(c + amp * math.sin(phase))
                    front = math.cos(phase) > 0  # la mitad que pasa por delante brilla más
                    if 0 <= py < h:
                        img.putpixel((x, py), hexc(MAGENTA[5] if front else MAGENTA[3]))
                    if 0 <= py + 1 < h:
                        img.putpixel((x, py + 1), hexc(MAGENTA[1] if front else MAGENTA[0]))
        add(f'fx_rail_beam_{k}', img, 30, full_rect=True)

        # Punta: misma banda de color con el perfil de un semicírculo achatado
        cap = Image.new('RGBA', (RAIL_CAP, h), (0, 0, 0, 0))
        for y in range(h):
            for x in range(RAIL_CAP):
                u = (RAIL_CAP - (x + 0.5)) / RAIL_CAP  # 1 en la punta, 0 donde empieza el tramo
                half = c * math.sqrt(max(0.0, 1 - u * u))
                if half <= 0:
                    continue
                d = abs(y + 0.5 - c) / half
                col = rail_color(d, x, y, k, t) if d <= 1.0 else None
                if col:
                    cap.putpixel((x, y), col)
        add(f'fx_rail_cap_{k}', cap, 30)


def rail_muzzle():
    """Destello en la boca del Rail Gun (4 fotogramas, 96 px = 3.2 u)."""
    n = 96
    for f in range(4):
        img = canvas(n)
        for x, y, d, a in polar(n):
            ray = min(abs(math.sin(a * 4)), 1.0) < 0.12  # 8 rayos
            if f == 0:
                if d < 15:
                    img.putpixel((x, y), hexc(MAGENTA[5] if d < 10 else MAGENTA[4]))
                elif ray and d < 40:
                    img.putpixel((x, y), hexc(MAGENTA[5] if d < 26 else MAGENTA[3]))
            elif f == 1:
                if d < 7:
                    img.putpixel((x, y), hexc(MAGENTA[5]))
                elif 22 <= d < 28:
                    img.putpixel((x, y), hexc(MAGENTA[5] if d >= 26 else MAGENTA[3]))
                elif ray and 28 <= d < 44 and checker(x, y, 0.6):
                    img.putpixel((x, y), hexc(MAGENTA[3]))
            elif f == 2:
                if 32 <= d < 36 and checker(x, y, 0.7):
                    img.putpixel((x, y), hexc(MAGENTA[4]))
                elif d < 4:
                    img.putpixel((x, y), hexc(MAGENTA[4]))
            else:
                if 40 <= d < 43 and checker(x, y, 0.3):
                    img.putpixel((x, y), hexc(MAGENTA[2]))
        add(f'fx_rail_muzzle_{f}', img, 30)


def pixel():
    """Píxel blanco (1 unidad) para los rayos: filtro point, bordes nítidos."""
    img = Image.new('RGBA', (4, 4), (255, 255, 255, 255))
    add('fx_pixel', img, 4)


# --------------------------------------------------------------------- main
def write_meta(path_png, template, ppu, full_rect=False):
    meta = path_png + '.meta'
    if os.path.exists(meta):
        text = open(meta, encoding='utf-8').read()
    else:
        text = open(template, encoding='utf-8').read()
        text = re.sub(r'^guid: .*$', 'guid: ' + uuid.uuid4().hex, text, flags=re.M)
        text = re.sub(r'spriteID: .*$', 'spriteID: ' + uuid.uuid4().hex, text, flags=re.M)
    ppu_text = ('%g' % ppu)
    text = re.sub(r'spritePixelsToUnits: .*$', 'spritePixelsToUnits: ' + ppu_text, text, flags=re.M)
    # 0 = Full Rect (necesario para el modo Tiled), 1 = Tight
    text = re.sub(r'spriteMeshType: .*$', 'spriteMeshType: ' + ('0' if full_rect else '1'), text, flags=re.M)
    open(meta, 'w', encoding='utf-8', newline='\n').write(text)


def main():
    out, template = sys.argv[1], (sys.argv[2] if len(sys.argv) > 2 else None)
    for fn in (sparks, flash, shockwave, ring_generic, disc_generic, ring_tension, ring_anchor,
               shield_boss, shield_player, vortex, satellite, medkit, rail_beam, rail_muzzle, pixel):
        fn()
    for name, (img, ppu, full_rect) in SPRITES.items():
        path = os.path.join(out, name + '.png')
        img.save(path)
        if template:
            write_meta(path, template, ppu, full_rect)
    print(len(SPRITES), 'sprites')


if __name__ == '__main__':
    main()
