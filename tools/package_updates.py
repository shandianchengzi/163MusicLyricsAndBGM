"""Build program-only and adjacent-release delta ZIPs; never redistribute engine in updates."""
import hashlib
import json
from pathlib import Path
import sys
import zipfile

ENGINE_VERSION = 'vocal-separate-0.0.4-headless-v1'


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def build(publish, assets, version, previous=None):
    publish, assets = Path(publish), Path(assets)
    assets.mkdir(parents=True, exist_ok=True)
    (publish / 'app-version.txt').write_text(version + '\n', encoding='utf-8')
    files = [p for p in sorted(publish.rglob('*')) if p.is_file()
             and p.relative_to(publish).parts[0].lower() != 'engine'
             and p.name != 'update-manifest.json']
    entries = [{'path': p.relative_to(publish).as_posix(), 'sha256': digest(p)} for p in files]

    def package(name, selected):
        target = assets / name
        with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for item in selected:
                archive.write(publish / item['path'], item['path'])
        return {'assetName': name, 'sha256': digest(target)}

    manifest = {'version': version, 'engineVersion': ENGINE_VERSION, 'files': entries,
                'package': package('MusicLyricsAndBGM-update-win-x64.zip', entries)}
    if previous and previous.get('engineVersion') == ENGINE_VERSION and previous.get('version') != version:
        old = {f['path']: f['sha256'] for f in previous['files']}
        changed = [f for f in entries if old.get(f['path']) != f['sha256']]
        delta = package('MusicLyricsAndBGM-delta-win-x64.zip', changed)
        delta.update(baseVersion=previous['version'], files=[f['path'] for f in changed])
        manifest['delta'] = delta
    text = json.dumps(manifest, ensure_ascii=False, indent=2) + '\n'
    (assets / 'update-manifest.json').write_text(text, encoding='utf-8')
    (publish / 'update-manifest.json').write_text(text, encoding='utf-8')
    return manifest


if __name__ == '__main__':
    previous = None
    if len(sys.argv) > 4 and Path(sys.argv[4]).is_file():
        previous = json.loads(Path(sys.argv[4]).read_text(encoding='utf-8-sig'))
    build(sys.argv[1], sys.argv[2], sys.argv[3], previous)
