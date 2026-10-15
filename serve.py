#!/usr/bin/env python3
"""Servidor estático do Range Lab — sem cache e multi-thread.

    python3 serve.py [porta]      # porta por omissão: 8080

Porquê um servidor próprio em vez de `python3 -m http.server`:
o SimpleHTTPRequestHandler não envia Cache-Control, portanto o browser aplica
cache heurística e pode servir um ficheiro antigo mesmo depois de corrigido.
Aqui enviamos `no-store`, o que elimina essa classe de bug.
"""
import functools
import os
import socketserver
import sys
from http.server import SimpleHTTPRequestHandler

ROOT = os.path.dirname(os.path.abspath(__file__))


class NoCacheHandler(SimpleHTTPRequestHandler):
    """Serve ficheiros do repositório com cabeçalhos anti-cache."""

    def end_headers(self):
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        self.send_header("Pragma", "no-cache")
        self.send_header("Expires", "0")
        super().end_headers()

    def log_message(self, fmt, *args):  # mantém o log, mas sem ruído extra
        sys.stderr.write("%s %s\n" % (self.address_string(), fmt % args))


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    handler = functools.partial(NoCacheHandler, directory=ROOT)
    with Server(("0.0.0.0", port), handler) as httpd:
        print(f"Range Lab a servir {ROOT} em http://0.0.0.0:{port} (sem cache)", flush=True)
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nparado.")


if __name__ == "__main__":
    main()
