from PIL import Image
from pathlib import Path
root = Path(__file__).resolve().parents[1]
source = root / "Assets" / "GD1.png"
target = root / "Assets" / "GD1.ico"
image = Image.open(source).convert("RGBA")
image.save(target, format="ICO", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print(f"created {target} ({target.stat().st_size} bytes)")
