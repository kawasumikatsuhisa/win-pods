#!/usr/bin/env python3
"""WinPods のトレイ／アプリアイコン (app.ico) を生成する。

外部ライブラリを使わずに ICO (BMP 形式) を書き出す。
デザインを変えたいときはこのスクリプトを編集して再生成する:

    python3 tools/generate_icon.py src/WinPods.App/Assets/app.ico
"""
from __future__ import annotations

import struct
import sys

SS = 4  # スーパーサンプリング倍率 (アンチエイリアス用)
SIZES = (16, 32, 48, 64)

BACKGROUND = (0x2F, 0x6F, 0xED)  # RGB: Windows のアクセントに近い青
FOREGROUND = (0xFF, 0xFF, 0xFF)  # RGB: pods の白


def _rounded_rect(x: float, y: float, size: float) -> bool:
    """一辺 size の角丸正方形の内側かどうか。"""
    r = size * 0.22
    cx = min(max(x, r), size - r)
    cy = min(max(y, r), size - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def _pod(x: float, y: float, size: float, cx: float) -> bool:
    """イヤホン 1 個分の形 (上の丸 + 下に伸びるステム)。"""
    head_cy = size * 0.36
    head_r = size * 0.145
    if (x - cx) ** 2 + (y - head_cy) ** 2 <= head_r * head_r:
        return True

    stem_w = size * 0.085
    stem_top = head_cy
    stem_bottom = size * 0.74
    if stem_top <= y <= stem_bottom and abs(x - cx) <= stem_w / 2:
        return True

    # ステム下端を丸める
    return (x - cx) ** 2 + (y - stem_bottom) ** 2 <= (stem_w / 2) ** 2


def render(size: int) -> bytes:
    """32bpp BGRA のピクセル列 (上から下) を返す。"""
    left_cx = size * 0.32
    right_cx = size * 0.68
    pixels = bytearray()

    for py in range(size):
        for px in range(size):
            inside = 0
            on_pod = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = px + (sx + 0.5) / SS
                    y = py + (sy + 0.5) / SS
                    if _rounded_rect(x, y, size):
                        inside += 1
                        if _pod(x, y, size, left_cx) or _pod(x, y, size, right_cx):
                            on_pod += 1

            total = SS * SS
            alpha = round(255 * inside / total)
            if alpha == 0:
                pixels += b"\x00\x00\x00\x00"
                continue

            mix = on_pod / inside
            r = round(BACKGROUND[0] * (1 - mix) + FOREGROUND[0] * mix)
            g = round(BACKGROUND[1] * (1 - mix) + FOREGROUND[1] * mix)
            b = round(BACKGROUND[2] * (1 - mix) + FOREGROUND[2] * mix)
            pixels += bytes((b, g, r, alpha))

    return bytes(pixels)


def to_dib(size: int, pixels: bytes) -> bytes:
    """BITMAPINFOHEADER + ボトムアップの BGRA + AND マスク。"""
    header = struct.pack(
        "<IiiHHIIiiII",
        40,          # biSize
        size,        # biWidth
        size * 2,    # biHeight (XOR + AND の合計)
        1,           # biPlanes
        32,          # biBitCount
        0,           # biCompression = BI_RGB
        size * size * 4,
        0, 0, 0, 0,
    )

    rows = [pixels[y * size * 4:(y + 1) * size * 4] for y in range(size)]
    xor = b"".join(reversed(rows))

    mask_stride = ((size + 31) // 32) * 4
    and_mask = b"\x00" * (mask_stride * size)

    return header + xor + and_mask


def main(path: str) -> None:
    images = [(size, to_dib(size, render(size))) for size in SIZES]

    out = bytearray(struct.pack("<HHH", 0, 1, len(images)))
    offset = 6 + 16 * len(images)

    for size, dib in images:
        out += struct.pack(
            "<BBBBHHII",
            size if size < 256 else 0,
            size if size < 256 else 0,
            0,   # 色数 (32bpp なので 0)
            0,   # 予約
            1,   # プレーン数
            32,  # ビット深度
            len(dib),
            offset,
        )
        offset += len(dib)

    for _, dib in images:
        out += dib

    with open(path, "wb") as f:
        f.write(out)

    print(f"wrote {path} ({len(out)} bytes, sizes={SIZES})")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "app.ico")
