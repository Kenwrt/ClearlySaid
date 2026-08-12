from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "artifacts" / "Google-Play-Listing"
OUTPUT.mkdir(parents=True, exist_ok=True)

INDIGO = "#5865E8"
INDIGO_DARK = "#4451D7"
NAVY = "#111C3A"
MUTED = "#5F6680"
SURFACE = "#FFFFFF"
BACKGROUND = "#F6F7FC"
BORDER = "#DDE1F0"


def font(size: int, bold: bool = False):
    names = [
        "C:/Windows/Fonts/seguisb.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
    ]
    for name in names:
        if Path(name).exists():
            return ImageFont.truetype(name, size)
    return ImageFont.load_default()


def rounded(draw, box, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def draw_mark(draw, box, background=INDIGO):
    x0, y0, x1, y1 = box
    size = x1 - x0
    rounded(draw, box, int(size * .23), background)
    bx0, by0 = x0 + size * .22, y0 + size * .18
    bx1, by1 = x0 + size * .78, y0 + size * .69
    rounded(draw, (bx0, by0, bx1, by1), int(size * .12), "white")
    draw.polygon([
        (x0 + size * .35, y0 + size * .65),
        (x0 + size * .30, y0 + size * .84),
        (x0 + size * .50, y0 + size * .67),
    ], fill="white")
    bars = [(.33, .40, .07, .18), (.44, .32, .07, .34), (.55, .37, .07, .24), (.66, .43, .07, .12)]
    for rx, ry, rw, rh in bars:
        rounded(draw, (x0 + size * rx, y0 + size * ry,
                       x0 + size * (rx + rw), y0 + size * (ry + rh)),
                int(size * .035), background)


def fit_text(draw, text, max_width, start_size, bold=False):
    size = start_size
    while size > 12:
        candidate = font(size, bold)
        if draw.textbbox((0, 0), text, font=candidate)[2] <= max_width:
            return candidate
        size -= 1
    return font(size, bold)


def icon():
    image = Image.new("RGB", (512, 512), INDIGO)
    draw = ImageDraw.Draw(image)
    draw_mark(draw, (0, 0, 512, 512))
    image.save(OUTPUT / "clearlysaid-icon-512.png", optimize=True)


def feature():
    image = Image.new("RGB", (1024, 500), BACKGROUND)
    draw = ImageDraw.Draw(image)
    draw.ellipse((742, -190, 1150, 218), fill="#E9EBFF")
    draw.ellipse((-160, 320, 260, 740), fill="#EEF0FF")
    draw_mark(draw, (72, 72, 192, 192))
    draw.text((226, 70), "ClearlySaid", font=font(58, True), fill=NAVY)
    draw.text((226, 145), "Say it naturally. Send it clearly.", font=font(28), fill=MUTED)
    rounded(draw, (72, 248, 930, 409), 28, SURFACE, BORDER, 2)
    draw.text((108, 274), "Turn rough thoughts into clear messages", font=font(31, True), fill=NAVY)
    draw.text((108, 330), "Dictate or type  •  Choose your style  •  Copy and send", font=font(23), fill=MUTED)
    rounded(draw, (782, 327, 898, 379), 18, INDIGO)
    draw.text((810, 338), "Clear", font=font(22, True), fill="white")
    image.save(OUTPUT / "clearlysaid-feature-1024x500.png", optimize=True)


def screenshot(path, title, subtitle, input_text, output_text, show_style=False):
    image = Image.new("RGB", (1080, 1920), BACKGROUND)
    draw = ImageDraw.Draw(image)
    draw_mark(draw, (72, 78, 188, 194))
    draw.text((220, 82), "ClearlySaid", font=font(66, True), fill=NAVY)
    draw.text((76, 220), title, font=fit_text(draw, title, 928, 50, True), fill=NAVY)
    draw.text((76, 284), subtitle, font=font(28), fill=MUTED)

    top = 356
    rounded(draw, (60, top, 1020, 830), 34, SURFACE, BORDER, 3)
    draw.text((100, top + 42), "Your words", font=font(30, True), fill=NAVY)
    y = top + 100
    for line in wrap(draw, input_text, font(34), 850):
        draw.text((100, y), line, font=font(34), fill=NAVY)
        y += 47

    if show_style:
        style_y = top + 264
        draw.text((100, style_y), "Purpose", font=font(23, True), fill=MUTED)
        draw.text((398, style_y), "Tone", font=font(23, True), fill=MUTED)
        draw.text((696, style_y), "Directness", font=font(23, True), fill=MUTED)
        for x, value in [(100, "Request"), (398, "Professional"), (696, "Balanced")]:
            rounded(draw, (x, style_y + 38, x + 252, style_y + 106), 18, "#F2F3FF", "#C8CDF8", 2)
            draw.text((x + 18, style_y + 54), value, font=font(23, True), fill=INDIGO_DARK)

    button_y = top + 376
    rounded(draw, (100, button_y, 315, button_y + 76), 22, "#EEF0FF")
    draw.text((145, button_y + 20), "Dictate", font=font(27, True), fill=INDIGO_DARK)
    rounded(draw, (340, button_y, 980, button_y + 76), 22, INDIGO)
    draw.text((556, button_y + 20), "Make it clear", font=font(27, True), fill="white")

    out_top = 890
    rounded(draw, (60, out_top, 1020, 1450), 34, SURFACE, BORDER, 3)
    draw.text((100, out_top + 42), "Clearly said", font=font(30, True), fill=NAVY)
    y = out_top + 108
    for line in wrap(draw, output_text, font(35), 850):
        draw.text((100, y), line, font=font(35), fill=NAVY)
        y += 50
    rounded(draw, (100, 1330, 340, 1406), 22, "#EEF0FF")
    draw.text((141, 1350), "Copy message", font=font(25, True), fill=INDIGO_DARK)

    draw.text((76, 1555), "Simple when you need it.", font=font(42, True), fill=NAVY)
    draw.text((76, 1615), "Powerful when your message matters.", font=font(32), fill=MUTED)
    image.save(OUTPUT / path, optimize=True)


def wrap(draw, text, text_font, max_width):
    lines, current = [], ""
    for word in text.split():
        candidate = word if not current else f"{current} {word}"
        if draw.textbbox((0, 0), candidate, font=text_font)[2] <= max_width:
            current = candidate
        else:
            lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


icon()
feature()
screenshot(
    "clearlysaid-phone-1.png",
    "From spoken thought to polished message",
    "Dictate naturally or type what you want to say.",
    "hey just checking if you had a chance to look at the proposal can you let me know by friday",
    "Hi, I’m following up to see whether you’ve had a chance to review the proposal. Could you please let me know by Friday?",
)
screenshot(
    "clearlysaid-phone-2.png",
    "Shape every message for the moment",
    "Paid plans add purpose, tone, and directness controls.",
    "need the report today because the meeting got moved up",
    "Could you please send the report today? The meeting has been moved up, so I’ll need it sooner than expected.",
    show_style=True,
)
print(OUTPUT)
