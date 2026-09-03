"""Generate redistributable FreeFlight cabin schematics from coded seat geometry."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "FreeFlight.CabinControl.App" / "Assets" / "CabinLayouts"
WIDTH, HEIGHT = 1033, 192


def add_rows(target, cabin_class, rows, xs, letters, ys, offsets, source_width, source_height):
    scale = WIDTH / source_width
    top = (HEIGHT - source_height * scale) / 2
    for row, x in zip(rows, xs):
        for letter, y, offset in zip(letters, ys, offsets):
            target.append((f"{row}{letter}", cabin_class, (x + offset) * scale, top + y * scale))


def add_row(target, cabin_class, row, positions, source_width, source_height):
    scale = WIDTH / source_width
    top = (HEIGHT - source_height * scale) / 2
    for letter, x, y in positions:
        target.append((f"{row}{letter}", cabin_class, x * scale, top + y * scale))


def ba_777_200er():
    seats = []
    add_rows(seats, "Club World", range(1, 8), [315, 379, 446, 512, 577, 640, 705], "AEFK", [318, 244, 152, 75], [0] * 4, 2860, 380)
    add_rows(seats, "Club World", range(10, 15), [927, 992, 1058, 1121, 1184], "AEFK", [318, 244, 152, 75], [0, 12, 12, 0], 2860, 380)
    add_rows(seats, "World Traveller Plus", range(15, 20), [1265.4, 1320.3, 1375.3, 1430.4, 1485.5], "ABDEFGJK", [343.2, 308.2, 249, 214.3, 177.3, 142.6, 81.2, 47.3], [0, 0, 12.4, 12.4, 12.4, 12.4, 0, 0], 2860, 380)
    add_rows(seats, "World Traveller", range(20, 25), [1579.1, 1623.5, 1668.1, 1714, 1758.9], "ABCDEFGHJK", [348.6, 318, 288, 241.2, 211.7, 181.2, 150.4, 105.1, 74.3, 43.4], [0, 0, 0, 8.7, 8.7, 8.7, 8.7, 0, 0, 0], 2860, 380)
    rows = list(range(26, 36))
    add_rows(seats, "World Traveller", rows, [1970.6, 2018.3, 2063.3, 2108.7, 2154.2, 2198.6, 2243.3, 2289.2, 2334.5, 2379.6], "ABCHJK", [348, 318.9, 289.5, 102.9, 73.7, 43.7], [0] * 6, 2860, 380)
    add_rows(seats, "World Traveller", rows, [1941.1, 1985.3, 2031.2, 2075.9, 2120.4, 2166.1, 2212.8, 2256.9, 2304.1, 2350.1], "DEFG", [240.8, 212, 182.8, 151.7], [0] * 4, 2860, 380)
    add_row(seats, "World Traveller", 36, [("A",2431.9,344),("B",2429.6,311.1),("D",2394.4,240.4),("E",2394.2,211.8),("F",2393.8,182.5),("G",2394.3,150.9),("J",2434,75.2),("K",2435.9,44.8)], 2860, 380)
    add_row(seats, "World Traveller", 37, [("A",2476.5,339.9),("B",2474.5,307.7),("D",2439.4,240),("E",2439.5,211.5),("F",2439.1,182.8),("G",2438.5,153),("J",2479,78),("K",2480.6,48.1)], 2860, 380)
    add_row(seats, "World Traveller", 38, [("A",2520,337.8),("B",2517.7,306.1),("D",2484.7,240.3),("E",2484.7,211.6),("F",2484.5,182.7),("G",2483.8,151.7),("J",2523.7,82.3),("K",2525.5,52)], 2860, 380)
    add_row(seats, "World Traveller", 39, [("D",2528.6,240.8),("E",2528.7,211.6),("F",2527.7,182.1),("G",2528.3,151.3),("J",2568.3,84.9),("K",2570.2,56.2)], 2860, 380)
    add_row(seats, "World Traveller", 40, [("D",2572.4,240.6),("E",2572.5,211.3),("F",2571.9,182.2),("G",2572,151.5)], 2860, 380)
    return seats


def ba_777_300():
    seats = []
    add_rows(seats, "First", [1, 2], [261, 367], "AEFK", [331, 273, 191, 134], [0, 10, 10, 0], 2855, 390)
    add_rows(seats, "Club World", [5, 6, 7], [471.5, 524.8, 581.1], "AEFK", [331, 273, 191, 134], [0, -6, -6, 0], 2855, 390)
    add_rows(seats, "Club World", range(8, 18), [815, 869.6, 925.2, 980.2, 1034.8, 1091, 1145.4, 1200.1, 1254.8, 1310], "AEFK", [331, 273, 191, 134], [0, 19, 19, 0], 2855, 390)
    add_rows(seats, "Club World", range(19, 25), [1466, 1521, 1576, 1630, 1685.5, 1740.5], "AEFK", [331, 273, 191, 134], [0, 19, 19, 0], 2855, 390)
    add_rows(seats, "World Traveller Plus", range(25, 30), [1811.2, 1858.1, 1905.3, 1951, 1997], "ABDEFGJK", [359.3, 330, 277.7, 249.2, 218.2, 189.2, 135.8, 107.4], [0, 0, 7, 7, 7, 7, 0, 0], 2855, 390)
    rows = list(range(30, 40))
    add_rows(seats, "World Traveller", rows, [2133.8, 2172.4, 2211, 2249.6, 2288.1, 2325.9, 2364.5, 2402.7, 2440.8, 2479], "ABCHJK", [363.4, 337.5, 312.5, 153.5, 128.4, 103.2], [0] * 6, 2855, 390)
    add_rows(seats, "World Traveller", rows, [2130.2, 2168.7, 2206.3, 2244.9, 2283.1, 2321.1, 2359.1, 2396.6, 2435.1, 2474], "DEFG", [271.8, 246.7, 221.8, 195.7], [0] * 4, 2855, 390)
    for row, positions in [
        (40, [("A",2520.1,360.2),("B",2517.9,333.6),("D",2512.5,272.6),("E",2512.4,246.8),("F",2511.8,222),("G",2512.1,196.3),("J",2517.3,132.4),("K",2519.4,106.9)]),
        (41, [("A",2558.2,357.6),("B",2556.2,330.9),("D",2550.3,272.4),("E",2550.6,247.4),("F",2549.5,222.3),("G",2549.7,197.4),("J",2556.7,134.8),("K",2558,109.8)]),
        (42, [("A",2595.7,355.8),("B",2593.6,328.2),("D",2589.6,273.1),("E",2589.7,246.9),("F",2589,222.3),("G",2589.1,196.9),("J",2594.1,137.6),("K",2595.7,112.8)]),
        (43, [("A",2634,353),("B",2631.7,325.7),("D",2626.8,272.5),("E",2627,247.1),("F",2625.9,222.2),("G",2626,196.5),("J",2634.4,141.5),("K",2636.1,116.5)]),
    ]:
        add_row(seats, "World Traveller", row, positions, 2855, 390)
    return seats


def render(name, aircraft, seats, doors, sections):
    image = Image.new("RGBA", (WIDTH * 2, HEIGHT * 2), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    s = 2
    hull = [(5, 96), (42, 48), (93, 20), (925, 20), (990, 32), (1028, 70), (1028, 122), (990, 160), (925, 172), (93, 172), (42, 144)]
    hull = [(x*s, y*s) for x, y in hull]
    draw.polygon(hull, fill="#071728", outline="#A8C4DA", width=3)
    draw.line([(44*s, 96*s), (1005*s, 96*s)], fill="#24415B", width=2)
    for x, color in sections:
        draw.line([(x*s, 24*s), (x*s, 168*s)], fill=color, width=3)
    for x, label in doors:
        draw.rounded_rectangle(((x-7)*s, 14*s, (x+7)*s, 36*s), radius=3*s, fill="#0C81C6", outline="#79D6FF", width=2)
        draw.text(((x-4)*s, 18*s), label, fill="white", font=ImageFont.load_default())
    colors = {"First":"#F4C95D", "Club World":"#44B7E8", "World Traveller Plus":"#B38CFF", "World Traveller":"#B6C6D4"}
    for _, cabin_class, x, y in seats:
        color = colors[cabin_class]
        cx, cy = int(x*s), int(y*s)
        draw.rounded_rectangle((cx-8, cy-7, cx+8, cy+7), radius=3, fill="#10253A", outline=color, width=2)
        draw.ellipse((cx-3, cy-3, cx+3, cy+3), fill=color)
    draw.text((72*s, 178*s), f"FREEFLIGHT CABIN PROFILE  |  BRITISH AIRWAYS {aircraft}  |  NOSE LEFT", fill="#8EA8BE", font=ImageFont.load_default())
    image.resize((WIDTH, HEIGHT), Image.Resampling.LANCZOS).save(OUTPUT / name, optimize=True)


OUTPUT.mkdir(parents=True, exist_ok=True)
render("BritishAirways777200Er.png", "777-200ER", ba_777_200er(), [(52, "1"), (295, "2")], [(270, "#44B7E8"), (449, "#B38CFF"), (563, "#B6C6D4"), (688, "#B6C6D4")])
render("BritishAirways777300.png", "777-300ER", ba_777_300(), [(50, "1"), (228, "2")], [(147, "#F4C95D"), (273, "#44B7E8"), (531, "#44B7E8"), (647, "#B38CFF"), (761, "#B6C6D4")])
