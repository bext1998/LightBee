#!/usr/bin/env python3
"""LightBee 工程模式伺服器 — 把 .tmp-probe-server/ 服務出去給手機採集資料用。

用法：
    python serve-engineering-mode.py [port]        # 預設 port 8765

若 repo 根目錄有一組 <主機名>.crt / <主機名>.key（例如 `tailscale cert` 產生、
在 .gitignore 中），就以 HTTPS 綁 0.0.0.0，手機可經 Tailscale 連
    https://<主機名>:<port>/

找不到憑證時退回 http://127.0.0.1:<port>/（只有本機能用）。手機端 getUserMedia
需要安全連線，所以要嘛補憑證、要嘛改用 `tailscale serve`。

Ctrl+C 停止。
"""
import functools
import http.server
import os
import ssl
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, ".tmp-probe-server")
DEFAULT_PORT = 8765


def find_cert(base):
    """在 base 目錄找第一組成對的 <name>.crt / <name>.key，回傳 (host, cert_path, key_path)。"""
    try:
        entries = sorted(os.listdir(base))
    except OSError:
        return None, None, None
    for name in entries:
        if name.endswith(".crt"):
            key = os.path.join(base, name[:-4] + ".key")
            if os.path.exists(key):
                return name[:-4], os.path.join(base, name), key
    return None, None, None


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

    host, cert, key = find_cert(HERE)
    bind = "0.0.0.0" if cert else "127.0.0.1"
    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=ROOT)

    try:
        httpd = http.server.ThreadingHTTPServer((bind, port), handler)
    except OSError as exc:
        print(f"無法在 {bind}:{port} 開伺服器：{exc}")
        print(f"換一個 port：python {os.path.basename(__file__)} 8890")
        return 1

    scheme = "http"
    if cert:
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        try:
            ctx.load_cert_chain(cert, key)
        except ssl.SSLError as exc:
            print(f"憑證載入失敗（{os.path.basename(cert)}）：{exc}")
            return 1
        httpd.socket = ctx.wrap_socket(httpd.socket, server_side=True)
        scheme = "https"

    print("LightBee 工程模式伺服器")
    print(f"  服務目錄：{ROOT}")
    print(f"  本機：    {scheme}://localhost:{port}/")
    if cert:
        print(f"  手機：    https://{host}:{port}/   （經 Tailscale，需該裝置在同一 tailnet 且被授權）")
    else:
        print("  憑證：    沒找到 <主機名>.crt/.key，只在本機 http 提供。")
        print("            手機要用相機需安全連線 → 在此資料夾跑 `tailscale cert <你的主機名>`")
        print("            產生憑證後重開，或改用 `tailscale serve`。")
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
