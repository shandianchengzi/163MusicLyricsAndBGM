import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from package_updates import build


class UpdatePackagesTest(unittest.TestCase):
    def test_delta_excludes_engine_and_unchanged_runtime(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            publish = root / 'publish'
            publish.mkdir()
            (publish / 'engine').mkdir()
            (publish / 'engine/model.bin').write_bytes(b'large-model')
            (publish / 'runtime.dll').write_bytes(b'unchanged-runtime')
            (publish / 'MusicLyricApp.dll').write_bytes(b'old-app')
            old = build(publish, root / 'old', 'v7.3.0-bgm.4')
            (publish / 'MusicLyricApp.dll').write_bytes(b'new-app')
            new = build(publish, root / 'new', 'v7.3.0-bgm.5', old)
            with zipfile.ZipFile(root / 'new' / new['delta']['assetName']) as delta:
                self.assertEqual(set(delta.namelist()), {'MusicLyricApp.dll', 'app-version.txt'})
            with zipfile.ZipFile(root / 'new' / new['package']['assetName']) as full:
                self.assertIn('runtime.dll', full.namelist())
                self.assertFalse(any(n.startswith('engine/') for n in full.namelist()))
            self.assertEqual(new['delta']['baseVersion'], 'v7.3.0-bgm.4')
            self.assertEqual(json.loads((publish / 'update-manifest.json').read_text()), new)


if __name__ == '__main__':
    unittest.main()
