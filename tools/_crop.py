from PIL import Image
im = Image.open(r"D:\game\VuTruDaiChien_140\tools\bepinex_console.png")
w, h = im.size
# crop the console text area (exclude right-side scrollbar)
crop = im.crop((90, 0, int(w*0.88), h)).resize((int((int(w*0.88)-90)*1.6), int(h*1.6)), Image.LANCZOS)
crop.save(r"D:\game\VuTruDaiChien_140\tools\bepinex_log_crop.png")
print("console crop", crop.size)

im2 = Image.open(r"D:\game\VuTruDaiChien_140\tools\game_window.png")
w2, h2 = im2.size
c2 = im2.crop((0, 0, 260, h2)).resize((260*2, h2*2), Image.LANCZOS)
c2.save(r"D:\game\VuTruDaiChien_140\tools\game_left_crop.png")
print("game left crop", c2.size)
