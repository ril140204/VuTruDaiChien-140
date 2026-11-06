#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Probe QLTK server (127.0.0.1:28082).

Muc dich: ghi lai chinh xac byte server gui xuong client, de suy ra protocol
(command byte + payload) ma Client.dll phai noi dung.

Chi ghi, KHONG gui gi - de xem server lam gi khi co client ket noi.
"""
import socket
import sys
import time

HOST = "127.0.0.1"
PORT = 28082
DURATION = float(sys.argv[1]) if len(sys.argv) > 1 else 10.0


def dump(b, label):
    print(f'\n--- {label} ({len(b)} bytes) ---')
    if not b:
        return
    print('hex :', b.hex(' '))
    print('raw :', repr(b))
    # thu giai theo protocol Message2: [1 byte command][payload]
    cmd = b[0]
    signed = cmd - 256 if cmd > 127 else cmd
    print(f'cmd : {signed} (0x{cmd:02x})')
    payload = b[1:]
    print(f'payload({len(payload)}) hex:', payload.hex(' '))


def main():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(3)
    try:
        s.connect((HOST, PORT))
        print(f'connected to {HOST}:{PORT}')
    except Exception as e:
        print(f'CONNECT FAILED: {e}')
        return

    s.settimeout(1.0)
    deadline = time.time() + DURATION
    total = b''
    while time.time() < deadline:
        try:
            chunk = s.recv(4096)
            if not chunk:
                print('server closed connection')
                break
            print(f'\n[recv {len(chunk)} bytes at {time.strftime("%H:%M:%S")}]')
            dump(chunk, 'chunk')
            total += chunk
        except socket.timeout:
            continue
        except Exception as e:
            print(f'recv error: {e}')
            break

    print(f'\n=== TOTAL {len(total)} bytes ===')
    if total:
        dump(total, 'all')

    s.close()


if __name__ == '__main__':
    main()
