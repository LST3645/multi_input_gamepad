"""生成 MultiInputGamepad 应用图标：深色圆角底 + 金色手柄，多尺寸 ICO。"""

from PIL import Image, ImageDraw

S = 1024
BG = (26, 26, 31, 255)
BORDER = (58, 58, 68, 255)
GOLD = (224, 160, 48, 255)
DARK = (20, 20, 24, 255)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 圆角底
d.rounded_rectangle([8, 8, S - 8, S - 8], radius=190, fill=BG, outline=BORDER, width=10)

# 手柄：机身 + 两个握把
d.rounded_rectangle([150, 380, 874, 660], radius=130, fill=GOLD)
d.ellipse([170, 520, 470, 830], fill=GOLD)
d.ellipse([554, 520, 854, 830], fill=GOLD)
# 中间下方挖出握把间隙（用背景色覆盖，不能用透明，否则会把手柄挖穿）
d.ellipse([412, 636, 612, 884], fill=BG)

# 左摇杆
d.ellipse([248, 470, 392, 614], fill=DARK)
d.ellipse([290, 512, 350, 572], fill=(70, 70, 80, 255))
# 十字键
for box in ([470, 452, 534, 632], [422, 502, 582, 582]):
    d.rounded_rectangle(box, radius=18, fill=DARK)
# 右侧四键
for cx, cy in ((700, 470), (760, 530), (700, 590), (640, 530)):
    d.ellipse([cx - 30, cy - 30, cx + 30, cy + 30], fill=DARK)

img.save("src/multi_input_gamepad.App/app.ico",
         sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)])
# 预览图写到仓库外的工作区临时目录，避免污染工作树
img.resize((256, 256), Image.LANCZOS).save("../.tmp/app-icon-preview.png")
print("ok")
