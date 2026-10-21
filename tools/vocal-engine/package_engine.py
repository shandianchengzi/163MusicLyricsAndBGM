"""Replace only the PyInstaller entry script; preserve the upstream frozen runtime.

Run with CPython 3.10 on Windows after extracting vocal-separate v0.0.4.
The original bootloader, PYZ and runtime hooks remain byte-for-byte unchanged.
This avoids downloading/re-resolving old TensorFlow dependencies during builds.
"""
import hashlib
import marshal
from pathlib import Path
import shutil
import struct
import sys
import zlib

MAGIC = b'MEI\x0c\x0b\x0a\x0b\x0e'
COOKIE = struct.Struct('!8sIIII64s')
ENTRY = struct.Struct('!iIIIBc')


def patch_launcher(launcher, script):
    original = launcher.read_bytes()
    cookie_offset = original.rfind(MAGIC)
    if cookie_offset < 0:
        raise ValueError('No PyInstaller cookie')
    magic, size, toc_offset, toc_size, version, library = COOKIE.unpack_from(original, cookie_offset)
    if version != 310 or sys.version_info[:2] != (3, 10):
        raise ValueError('The upstream executable requires Python 3.10 bytecode')
    archive_start = cookie_offset + COOKIE.size - size
    old_toc = original[archive_start + toc_offset:archive_start + toc_offset + toc_size]
    payload = bytearray()
    toc = bytearray()
    position = 0
    replaced = 0
    while position < len(old_toc):
        length, offset, compressed, uncompressed, flag, kind = ENTRY.unpack_from(old_toc, position)
        if length < ENTRY.size + 1 or position + length > len(old_toc):
            raise ValueError('Invalid PyInstaller table')
        name = old_toc[position + ENTRY.size:position + length]
        data = original[archive_start + offset:archive_start + offset + compressed]
        if name.rstrip(b'\x00') == b'start' and kind == b's':
            code = compile(script.read_text(encoding='utf-8'), 'vocal_engine_cli.py', 'exec')
            marshalled = marshal.dumps(code)
            uncompressed = len(marshalled)
            data = zlib.compress(marshalled) if flag else marshalled
            replaced += 1
        toc.extend(ENTRY.pack(length, len(payload), len(data), uncompressed, flag, kind))
        toc.extend(name)
        payload.extend(data)
        position += length
    if replaced != 1:
        raise ValueError('Expected exactly one upstream start script')
    cookie = COOKIE.pack(magic, len(payload) + len(toc) + COOKIE.size,
                         len(payload), len(toc), version, library)
    launcher.write_bytes(original[:archive_start] + payload + toc + cookie)


def main():
    source, target = map(Path, sys.argv[1:])
    target.mkdir(parents=True, exist_ok=False)
    for name in ('_internal', 'pretrained_models'):
        if not (source / name).is_dir():
            raise FileNotFoundError(source / name)
    shutil.copytree(source / '_internal', target / '_internal')
    shutil.copytree(source / 'pretrained_models' / '2stems', target / 'pretrained_models' / '2stems')
    for name in ('ffmpeg.exe', 'ffprobe.exe'):
        shutil.copy2(source / name, target / name)
    launcher = target / 'vocal-engine.exe'
    shutil.copy2(source / 'start.exe', launcher)
    patch_launcher(launcher, Path(__file__).with_name('cli.py'))
    # Preserve the adaptation and license alongside the redistributable engine.
    for name in ('cli.py', 'package_engine.py', 'LICENSE', 'UPSTREAM.md'):
        shutil.copy2(Path(__file__).with_name(name), target / name)
    if not (target / 'pretrained_models/2stems/model.meta').is_file():
        raise ValueError('Missing bundled model')
    manifest = []
    for file in sorted(target.rglob('*')):
        if file.is_file():
            digest = hashlib.sha256()
            with file.open('rb') as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(block)
            manifest.append(digest.hexdigest() + '  ' + file.relative_to(target).as_posix())
    (target / 'SHA256SUMS.txt').write_text('\n'.join(manifest) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
