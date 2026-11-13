#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 marketing 下的卡片 HTML 逐张渲染为 1080x1440 PNG。

用法（在仓库根目录或任意位置均可）：
    python marketing/render-cards.py

依赖：本机 Chrome（自动探测常见安装路径），不依赖任何 Python 第三方包。
改了 HTML 文案后重跑本脚本即可覆盖 marketing/images/ 下的图片。
"""
import re
import struct
import subprocess
import shutil
import sys
from pathlib import Path

W, H = 1080, 1440
ROOT = Path(__file__).resolve().parent.parent
TMP = Path.home() / "AppData" / "Local" / "Temp" / "ollamahub-render"

CHROME_CANDIDATES = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

JOBS = [
    ("marketing/xiaohongshu-cards.html", "marketing/images/xiaohongshu"),
    ("marketing/wechat-xiaolvshu-cards.html", "marketing/images/wechat-xiaolvshu"),
]


def find_browser():
    for p in CHROME_CANDIDATES:
        if Path(p).exists():
            return p
    sys.exit("未找到 Chrome/Edge，请先安装其中之一")


def split_cards(html):
    """按 <!-- N 标题 --> 标记切出每张 .card，并取出共享的 <style>。"""
    style = re.search(r"<style>(.*?)</style>", html, re.S).group(1)
    body = html[html.index("<body>") + 6: html.index("</body>")]
    marks = [(m.start(), int(m.group(1)), m.group(2).strip())
             for m in re.finditer(r"<!--\s*(\d+)\s+([^-]*?)\s*-->", body)]
    cards = []
    for i, (pos, num, title) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else len(body)
        frag = re.sub(r"<!--.*?-->", "", body[pos:end], count=1, flags=re.S).strip()
        cards.append((num, title, frag))
    return style, cards


def build(style, frag):
    # 单卡独占画幅：去掉外边距与卡片底边线，避免 1441px
    return ("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            f"<style>{style}</style>"
            "<style>.card{margin:0 !important;border-bottom:none !important;}"
            "html,body{overflow:hidden;}</style></head><body>" + frag + "</body></html>")


def png_size(p):
    with open(p, "rb") as f:
        head = f.read(33)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", head[16:24])


def main():
    chrome = find_browser()
    if TMP.exists():
        shutil.rmtree(TMP, ignore_errors=True)
    TMP.mkdir(parents=True, exist_ok=True)

    total = 0
    for src, outdir in JOBS:
        style, cards = split_cards((ROOT / src).read_text(encoding="utf-8"))
        dst = ROOT / outdir
        dst.mkdir(parents=True, exist_ok=True)
        print(f"\n=== {src} -> {outdir}（{len(cards)} 张）===")
        for num, title, frag in cards:
            page = TMP / f"c{num}.html"
            page.write_text(build(style, frag), encoding="utf-8")
            png = dst / f"{num:02d}-{title}.png"
            r = subprocess.run(
                [chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                 "--force-device-scale-factor=1", f"--window-size={W},{H}",
                 "--virtual-time-budget=3000", f"--screenshot={png}", page.as_uri()],
                capture_output=True, text=True, timeout=120)
            if not png.exists():
                print(f"  [FAIL] {num} {title}: {(r.stderr or '')[:160]}")
                continue
            size = png_size(png)
            print(f"  [{'OK ' if size == (W, H) else '!! '}] {png.name}  {size}  {png.stat().st_size // 1024} KB")
            total += 1

    shutil.rmtree(TMP, ignore_errors=True)
    print(f"\n共渲染 {total} 张 -> {ROOT / 'marketing/images'}")


if __name__ == "__main__":
    main()
