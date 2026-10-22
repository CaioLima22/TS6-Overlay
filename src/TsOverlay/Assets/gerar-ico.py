# Gera o app.ico (16 a 256 px) a partir do icon.svg.
# Uso: pip install cairosvg pillow && python gerar-ico.py
import io
from pathlib import Path

import cairosvg
from PIL import Image

here = Path(__file__).parent
svg = (here / "icon.svg").read_bytes()
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = [
    Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg, output_width=s, output_height=s))).convert("RGBA")
    for s in sizes
]
images[-1].save(here / "app.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])
