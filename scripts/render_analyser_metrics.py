#!/usr/bin/env python3
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


METRICS = [
    ("identity", "Identity"),
    ("sources", "Sources"),
    ("products", "Products"),
    ("certifications", "Certifications"),
    ("noiseControl", "Noise control"),
    ("groundedness", "Groundedness"),
]


def load_font(size: int, bold: bool = False):
    candidates = [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/Library/Fonts/Arial.ttf",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def draw_text(draw: ImageDraw.ImageDraw, xy: tuple[int, int], text: str, font, fill: str) -> None:
    draw.text(xy, text, font=font, fill=fill)


def render_chart(data: dict, output_path: Path) -> None:
    width = 1400
    height = 900
    margin = 70
    plot_left = 320
    plot_top = 190
    row_height = 86
    bar_height = 24
    max_bar_width = width - plot_left - margin

    image = Image.new("RGB", (width, height), "#f2f4f8")
    draw = ImageDraw.Draw(image)

    title_font = load_font(42, bold=True)
    subtitle_font = load_font(22)
    label_font = load_font(24, bold=True)
    small_font = load_font(18)
    value_font = load_font(19, bold=True)

    title = data.get("title", "Analyser quality metrics")
    runs = data.get("runs", [])
    if len(runs) < 2:
        raise ValueError("Metrics JSON needs at least two runs.")

    before = runs[0]
    after = runs[-1]

    draw.rectangle((0, 0, width, 120), fill="#183a6b")
    draw_text(draw, (margin, 32), title, title_font, "#ffffff")
    draw_text(
        draw,
        (margin, 132),
        "Manual scoring from 0-100. Use this as a lightweight before/after quality report.",
        subtitle_font,
        "#313844",
    )

    legend_y = 160
    draw.rectangle((plot_left, legend_y, plot_left + 28, legend_y + 18), fill="#9aa8bb")
    draw_text(draw, (plot_left + 40, legend_y - 4), before.get("name", "Before"), small_font, "#313844")
    draw.rectangle((plot_left + 210, legend_y, plot_left + 238, legend_y + 18), fill="#2f6f4e")
    draw_text(draw, (plot_left + 250, legend_y - 4), after.get("name", "After"), small_font, "#313844")

    for index, (key, label) in enumerate(METRICS):
        y = plot_top + index * row_height
        before_value = int(before.get(key, 0))
        after_value = int(after.get(key, 0))

        draw_text(draw, (margin, y + 4), label, label_font, "#172033")
        draw_text(draw, (margin, y + 38), f"+{after_value - before_value} points", small_font, "#526071")

        draw.rectangle((plot_left, y, plot_left + max_bar_width, y + bar_height), fill="#d7dde7")
        draw.rectangle(
            (plot_left, y, plot_left + int(max_bar_width * before_value / 100), y + bar_height),
            fill="#9aa8bb",
        )
        draw_text(draw, (plot_left + max_bar_width + 12, y - 2), str(before_value), value_font, "#313844")

        y2 = y + 36
        draw.rectangle((plot_left, y2, plot_left + max_bar_width, y2 + bar_height), fill="#d7dde7")
        draw.rectangle(
            (plot_left, y2, plot_left + int(max_bar_width * after_value / 100), y2 + bar_height),
            fill="#2f6f4e",
        )
        draw_text(draw, (plot_left + max_bar_width + 12, y2 - 2), str(after_value), value_font, "#1f4f38")

    avg_before = round(sum(int(before.get(key, 0)) for key, _ in METRICS) / len(METRICS))
    avg_after = round(sum(int(after.get(key, 0)) for key, _ in METRICS) / len(METRICS))
    summary_top = plot_top + len(METRICS) * row_height + 35
    draw.rounded_rectangle((margin, summary_top, width - margin, summary_top + 120), radius=8, fill="#ffffff", outline="#c8d0dc")
    draw_text(draw, (margin + 28, summary_top + 24), "Overall analyser quality", label_font, "#172033")
    draw_text(draw, (margin + 28, summary_top + 62), f"{avg_before}/100 -> {avg_after}/100", title_font, "#2f6f4e")
    draw_text(draw, (margin + 390, summary_top + 72), "Track this after each analyser change.", subtitle_font, "#526071")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path)


def main() -> int:
    base_dir = Path(__file__).resolve().parent
    input_path = Path(sys.argv[1]) if len(sys.argv) > 1 else base_dir / "analyser-metrics-example.json"
    output_path = Path(sys.argv[2]) if len(sys.argv) > 2 else base_dir.parent / "docs" / "images" / "analyser-quality.png"

    with input_path.open("r", encoding="utf-8") as file:
        data = json.load(file)

    render_chart(data, output_path)
    print(output_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
