from PIL import Image
import sys

def ascii_art(path, cols=110):
    im = Image.open(path).convert("L")
    w, h = im.size
    rows = max(1, int(cols * h / w / 2.1))
    im = im.resize((cols, rows))
    chars = " .:-=+*#%@"
    px = im.load()
    out = []
    for y in range(rows):
        line = "".join(chars[min(9, px[x, y] * 10 // 256)] for x in range(cols))
        out.append(line)
    return "\n".join(out)

for p in [r"D:\game\VuTruDaiChien_140\tools\game_window.png", r"D:\game\VuTruDaiChien_140\tools\bepinex_console.png"]:
    print("=" * 40)
    print(p)
    print("=" * 40)
    print(ascii_art(p))
