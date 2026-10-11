"""Pixel art del jefe CUSTODIUS (núcleo + 2 generadores), con la paleta de las naves del jugador.

Escala: 45 px por unidad local (el jefe tiene escala 1.5) = 30 px por unidad de mundo, igual que las naves.
Uso: python custodius.py <carpeta_salida>
"""
import math
import sys
from PIL import Image

def hexc(h):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)

OUT = hexc('0a0717')
HULL = [hexc(c) for c in ['1c1a36', '251c4a', '35335f', '55598f', '7f8bbd', 'b4c2e2', 'eef4ff']]
TEAL = [hexc(c) for c in ['0a3346', '0f6474', '169ea6', '3cd3c6', '9bf5e4', 'f0fffb']]
RED = [hexc(c) for c in ['360c22', '6a1430', 'a5233b', 'de4a3e', 'ff8a5c', 'ffe2bb']]
GOLD = [hexc(c) for c in ['3a1d0c', '763b10', 'bd651b', 'efa02b', 'ffd65e', 'fff7cc']]


class Canvas:
    """Lienzo con origen en el centro: x a la derecha, y hacia abajo."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.cx, self.cy = w // 2, h // 2
        self.img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        self.mask = [[False] * w for _ in range(h)]
        self.part = [[0] * w for _ in range(h)]

    def inside(self, x, y):
        X, Y = x + self.cx, y + self.cy
        return 0 <= X < self.w and 0 <= Y < self.h

    def m(self, x, y):
        return self.inside(x, y) and self.mask[y + self.cy][x + self.cx]

    def pid(self, x, y):
        return self.part[y + self.cy][x + self.cx] if self.inside(x, y) else 0

    def put(self, x, y, c):
        if self.inside(x, y):
            self.img.putpixel((x + self.cx, y + self.cy), c)

    def get(self, x, y):
        return self.img.getpixel((x + self.cx, y + self.cy))

    def coords(self):
        for y in range(-self.cy, self.h - self.cy):
            for x in range(-self.cx, self.w - self.cx):
                yield x, y

    def fill_shape(self, test, part_id, ramp, base=3, bevel=2, grad=None):
        """Rellena la forma con un bisel iluminado desde arriba-izquierda (luz de todas las naves)."""
        pix = [(x, y) for x, y in self.coords() if test(x, y)]
        for x, y in pix:
            self.mask[y + self.cy][x + self.cx] = True
            self.part[y + self.cy][x + self.cx] = part_id
        for x, y in pix:
            k = base + (grad(x, y) if grad else 0)
            for d in range(1, bevel + 1):
                tl = not test(x - d, y) or not test(x, y - d)
                br = not test(x + d, y) or not test(x, y + d)
                if tl and not br:
                    k += 2 if d == 1 else 1
                    break
                if br and not tl:
                    k -= 2 if d == 1 else 1
                    break
            self.put(x, y, ramp[max(0, min(len(ramp) - 1, k))])

    def seams(self):
        """Juntas entre piezas distintas: línea oscura en el lado de la pieza de mayor id."""
        for x, y in self.coords():
            p = self.pid(x, y)
            if p == 0:
                continue
            for nx, ny in ((x - 1, y), (x, y - 1), (x + 1, y), (x, y + 1)):
                q = self.pid(nx, ny)
                if q and q < p:
                    self.put(x, y, HULL[0])
                    break

    def rim_light(self, test, color, min_y=None):
        """Luz rebotada (cian) en el borde en sombra: abajo-derecha, al estilo Sea of Stars."""
        for x, y in self.coords():
            if not test(x, y) or (min_y is not None and y < min_y):
                continue
            if not test(x + 1, y) and test(x - 1, y) or (not test(x, y + 1) and test(x, y - 1) and not test(x + 1, y + 1)):
                self.put(x, y, color)

    def outline(self):
        for x, y in self.coords():
            if self.m(x, y):
                continue
            if any(self.m(nx, ny) for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))):
                self.put(x, y, OUT)
                self.mask[y + self.cy][x + self.cx] = False

    # --- detalles -----------------------------------------------------------
    def rivet(self, x, y):
        self.put(x, y, HULL[6])
        self.put(x + 1, y + 1, HULL[1])

    def hline(self, x0, x1, y, c):
        for x in range(x0, x1 + 1):
            self.put(x, y, c)

    def vline(self, x, y0, y1, c):
        for y in range(y0, y1 + 1):
            self.put(x, y, c)

    def panel_line(self, x0, x1, y):
        """Junta horizontal hundida: oscura con un brillo debajo."""
        for x in range(x0, x1 + 1):
            if self.m(x, y):
                self.put(x, y, HULL[1])
            if self.m(x, y + 1) and self.m(x, y + 2):
                self.put(x, y + 1, lighten(self.get(x, y + 1)))


def lighten(c):
    for ramp in (HULL, TEAL, RED, GOLD):
        if c in ramp:
            i = ramp.index(c)
            return ramp[min(len(ramp) - 1, i + 1)]
    return c


def superellipse(cx, cy, a, b, n=2.6):
    return lambda x, y: (abs((x - cx) / a) ** n + abs((y - cy) / b) ** n) <= 1.0


# ===================================================================== NÚCLEO
def build_core():
    # Ancho: los brazos llegan hasta el bloque interior de cada generador (x = ±71).
    c = Canvas(147, 83)
    POD_Y = 7  # la cápsula cuelga un poco por debajo de la línea de los brazos (como en el boceto)

    def pod(x, y):
        yy = y - POD_Y
        if yy < -27 or yy > 27:
            return False
        t = (yy + 27) / 54.0
        a = 30 - 9 * t            # más ancha arriba, se estrecha hacia abajo
        top = abs(x / a) ** 3 + abs(min(0, yy + 6) / 21.0) ** 3 <= 1.0
        bottom = abs(x / a) ** 2.2 + abs(max(0, yy + 6) / 33.0) ** 2.2 <= 1.0
        return top if yy < -6 else bottom

    def arm(x, y):
        return 22 <= abs(x) <= 71 and -5 <= y <= 5

    def arm_collar(x, y):  # anclaje del brazo a la cápsula
        return 20 <= abs(x) <= 31 and -8 <= y <= 8 and not (abs(x) >= 29 and abs(y) >= 7)

    def arm_end(x, y):  # abrazadera en el extremo, donde encaja el generador
        return 64 <= abs(x) <= 71 and -7 <= y <= 7

    # Brazos: viga con sombreado cilíndrico (iluminada arriba)
    c.fill_shape(arm, 1, HULL, base=3, bevel=1, grad=lambda x, y: 1 if y <= -2 else (-1 if y >= 3 else 0))
    c.fill_shape(arm_collar, 2, HULL, base=3, bevel=1)
    c.fill_shape(arm_end, 2, HULL, base=2, bevel=1)
    # Cápsula: degradado radial suave + bisel
    c.fill_shape(pod, 3, HULL, base=3, bevel=2,
                 grad=lambda x, y: 1 if (x + 8) ** 2 + (y - POD_Y + 12) ** 2 < 200 else (-1 if (x - 6) ** 2 + (y - POD_Y - 10) ** 2 > 560 else 0))
    c.seams()
    c.rim_light(lambda x, y: pod(x, y) and x > 4, TEAL[1], min_y=POD_Y - 4)

    # Conducto de energía cian por el centro de cada brazo (alimenta el escudo)
    for s in (-1, 1):
        for i, x in enumerate(range(32, 64)):
            X = s * x
            c.put(X, -1, TEAL[3] if (x // 4) % 2 == 0 else TEAL[2])
            c.put(X, 0, TEAL[4] if (x // 4) % 2 == 0 else TEAL[3])
            c.put(X, 1, TEAL[1])
        c.hline(min(s * 32, s * 63), max(s * 32, s * 63), -2, HULL[1])
        c.hline(min(s * 32, s * 63), max(s * 32, s * 63), 2, HULL[1])
        # remaches de la viga
        for x in (36, 46, 56):
            c.rivet(s * x, -4)
            c.rivet(s * x, 3)
        # abrazadera del extremo: ranuras
        c.vline(s * 67, -5, 5, HULL[1])
        c.vline(s * 68, -5, 5, HULL[4])
        # collar: luz roja de estado
        c.put(s * 25, -5, RED[4])
        c.put(s * 25, -4, RED[2])
        c.rivet(s * 22 - (1 if s > 0 else 0), 5)

    # Placas de la cápsula
    c.panel_line(-19, 19, POD_Y - 21)
    c.panel_line(-14, 14, POD_Y + 20)
    for x in (-22, 21):
        c.rivet(x, POD_Y - 16)
    for x in (-13, 12):
        c.rivet(x, POD_Y + 24)
    # Rejillas inferiores
    for i in range(3):
        y = POD_Y + 23 + i * 2
        c.hline(-5 + i, 5 - i, y, HULL[0])
    # Cresta frontal (arriba = frente del jefe)
    for x in range(-6, 7):
        c.put(x, POD_Y - 25, HULL[5] if x < 0 else HULL[4])
    c.put(-1, POD_Y - 24, RED[3])
    c.put(0, POD_Y - 24, RED[4])
    c.put(1, POD_Y - 24, RED[3])

    # ---- El OJO ----
    EX, EY = 0, POD_Y + 1
    for x, y in list(c.coords()):
        d = math.hypot(x - EX + 0.5, y - EY + 0.5)
        if d <= 16.5:
            ang = math.atan2(y - EY, x - EX)
            lit = math.cos(ang - math.radians(225))  # luz desde arriba-izquierda
            if d > 15.2:
                c.put(x, y, OUT)
            elif d > 12.6:  # aro metálico exterior
                c.put(x, y, HULL[5 if lit > 0.55 else 4 if lit > 0 else 3 if lit > -0.6 else 2])
            elif d > 11.4:  # ranura interior del aro
                c.put(x, y, HULL[0])
            elif d > 9.6:   # párpado/obturador oscuro
                c.put(x, y, HULL[1] if lit < 0.2 else HULL[2])
            elif d > 8.6:   # resplandor rojo en el borde del iris
                c.put(x, y, RED[1])
            else:           # iris: más brillante hacia el centro, con estrías radiales
                k = 4 if d < 3.2 else 3 if d < 5.5 else 2
                if d >= 3.2 and int((math.degrees(ang) + 360) / 30) % 2 == 0:
                    k -= 1
                c.put(x, y, RED[k])
    # marcas del aro (como un objetivo de cámara)
    for ang in (0, 90, 180, 270):
        r = math.radians(ang)
        for rr in (13, 14):
            c.put(round(EX + rr * math.cos(r)), round(EY + rr * math.sin(r)), HULL[1])
    # pupila: rendija vertical
    c.vline(EX, EY - 4, EY + 3, RED[0])
    c.vline(EX - 1, EY - 2, EY + 1, RED[0])
    c.put(EX, EY - 5, RED[1])
    c.put(EX, EY + 4, RED[1])
    # brillos especulares
    c.put(EX - 4, EY - 5, RED[5])
    c.put(EX - 3, EY - 5, RED[5])
    c.put(EX - 4, EY - 4, RED[5])
    c.put(EX + 4, EY + 4, RED[4])

    c.outline()
    return c.img


# ================================================================= GENERADOR
def build_wing(side):
    """side = -1 para el generador izquierdo (el exterior queda a la izquierda), +1 para el derecho."""
    c = Canvas(39, 135)
    o = side  # dirección hacia el exterior

    def bar(x, y):  # pilar vertical con extremos redondeados
        if abs(x) > 6 or abs(y) > 64:
            return False
        if abs(y) > 58:
            return (abs(x) / 6.5) ** 2 + ((abs(y) - 58) / 6.5) ** 2 <= 1
        return True

    def box(x, y):  # bloque cruzado a media altura (más saliente hacia fuera, como en el boceto)
        xo = x * o
        return -10 <= xo <= 18 and abs(y) <= 10 and not (xo >= 16 and abs(y) >= 9)

    def caps(x, y):  # remates de los extremos
        return bar(x, y) and abs(y) >= 56

    c.fill_shape(bar, 1, HULL, base=3, bevel=1,
                 grad=lambda x, y: 1 if x <= -3 else (-1 if x >= 3 else 0))
    c.fill_shape(caps, 2, HULL, base=2, bevel=1, grad=lambda x, y: 1 if x <= -2 else 0)
    c.fill_shape(box, 3, HULL, base=3, bevel=2)
    c.seams()
    c.rim_light(lambda x, y: bar(x, y) and not box(x, y) and abs(y) < 57, TEAL[1])
    c.rim_light(box, TEAL[1], min_y=0)

    # Celdas de energía a lo largo del pilar (alimentan el escudo)
    for s in (-1, 1):
        for i in range(5):
            y0 = s * (16 + i * 8)
            ys = sorted((y0, y0 + s * 5))
            for y in range(ys[0], ys[1] + 1):
                c.put(-2, y, TEAL[1])
                c.put(-1, y, TEAL[3])
                c.put(0, y, TEAL[4] if i % 2 == 0 else TEAL[3])
                c.put(1, y, TEAL[2])
                c.put(2, y, TEAL[1])
            c.hline(-3, 3, ys[0] - 1, HULL[0])
            c.hline(-3, 3, ys[1] + 1, HULL[0])
        # emisores en las puntas
        tip = s * 61
        c.put(-1, tip, TEAL[4])
        c.put(0, tip, TEAL[5])
        c.put(1, tip, TEAL[3])
        c.put(0, tip + s, TEAL[3])
        c.panel_line(-5, 5, s * 56 if s > 0 else -57)

    # Bloque central: núcleo del generador + rejilla + remaches
    for x, y in list(c.coords()):
        d = math.hypot(x - 0.0, y - 0.0)
        if d <= 5.6:
            c.put(x, y, OUT if d > 4.7 else TEAL[2] if d > 3.4 else TEAL[4] if d > 1.6 else TEAL[5])
    c.put(-1, -2, TEAL[5])
    for i in range(4):  # rejilla en la parte exterior del bloque
        x = o * (9 + i * 2)
        c.vline(x, -5, 5, HULL[1])
    for x in (o * 15, o * -8):
        c.rivet(x, -8)
        c.rivet(x, 7)
    # luz de estado roja en el borde exterior (a juego con el ojo)
    c.put(o * 16, -1, RED[4])
    c.put(o * 16, 0, RED[2])

    c.outline()
    return c.img


def main():
    out = sys.argv[1]
    core = build_core()
    core.save(f'{out}/custodius_core.png')
    wl = build_wing(-1)
    wr = build_wing(1)
    wl.save(f'{out}/custodius_generator_left.png')
    wr.save(f'{out}/custodius_generator_right.png')
    print('core', core.size, 'wing', wl.size)


if __name__ == '__main__':
    main()
