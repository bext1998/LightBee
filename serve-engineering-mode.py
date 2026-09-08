#!/usr/bin/env python3
"""LightBee 工程模式伺服器 — 把 .tmp-probe-server/ 服務出去給手機採集資料用。

用法：
    python serve-engineering-mode.py [port]        # 預設 port 8888

搭配既有的 `tailscale serve`：本機只跑純 HTTP 綁 127.0.0.1:<port>，
由 Tailscale Serve 在 tailnet 上包成 HTTPS。手機開
    https://<你的主機名>.ts.net/
（不帶 port；Tailscale Serve 走 443）。

若 `tailscale serve` 還沒設定，腳本會印出要跑的指令。Ctrl+C 停止。
"""
import functools
import http.server
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, ".tmp-probe-server")
DEFAULT_PORT = 8888

_TS_CANDIDATES = [
    "tailscale",
    r"C:\Program Files\Tailscale\tailscale.exe",
    r"C:\Program Files (x86)\Tailscale\tailscale.exe",
]


def tailscale_serve_status():
    """回傳 `tailscale serve status` 的文字輸出，找不到 CLI 或失敗回傳 None。"""
    for exe in _TS_CANDIDATES:
        try:
            out = subprocess.run(
                [exe, "serve", "status"],
                capture_output=True, text=True, timeout=8,
            )
        except (FileNotFoundError, subprocess.TimeoutExpired):
            continue
        if out.returncode == 0:
            return out.stdout
        # CLI 找到了但沒設定 serve：stdout 常是 "No serve config" 之類
        return out.stdout or out.stderr
    return None


def public_url_for_port(status_text, port):
    """從 serve status 找出代理到本機這個 port 的公開 https URL。"""
    if not status_text:
        return None
    if f"127.0.0.1:{port}" not in status_text and f"localhost:{port}" not in status_text:
        return None
    m = re.search(r"https://[^\s]+", status_text)
    return m.group(0) if m else None


def main(argv):
    port = DEFAULT_PORT
    if len(argv) > 1:
        try:
            port = int(argv[1])
        except ValueError:
            print(f"port 要是數字，收到：{argv[1]}")
            return 2

    if not os.path.isdir(ROOT):
        print(f"找不到要服務的目錄：{ROOT}")
        return 1

    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=ROOT)
    try:
        httpd = http.server.ThreadingHTTPServer(("127.0.0.1", port), handler)
    except OSError as exc:
        print(f"無法在 127.0.0.1:{port} 開伺服器：{exc}")
        print(f"（可能已經有一個在跑）換 port：python {os.path.basename(__file__)} 8890")
        return 1

    status = tailscale_serve_status()
    public = public_url_for_port(status, port)

    print("LightBee 工程模式伺服器")
    print(f"  服務目錄：{ROOT}")
    print(f"  本機：    http://127.0.0.1:{port}/")
    if public:
        print(f"  手機：    {public}   （經 Tailscale Serve）")
    elif status is not None:
        print("  手機：    Tailscale Serve 目前沒有代理到這個 port。先跑一次：")
        print(f"              tailscale serve --bg {port}")
        print("            然後手機開 https://<你的主機名>.ts.net/")
    else:
        print("  手機：    找不到 tailscale CLI。裝好 Tailscale 後跑：")
        print(f"              tailscale serve --bg {port}")
    print("  Ctrl+C 停止")
    print(flush=True)

    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n已停止")
    finally:
        httpd.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
