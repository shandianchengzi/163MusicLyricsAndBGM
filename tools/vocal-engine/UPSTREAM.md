# Bundled vocal-separate engine

- Upstream: https://github.com/jianchang512/vocal-separate
- Version: v0.0.4, commit 5d11a4cff3b6eafcb41f92a0772301c0e1fe1465
- Binary distribution: https://github.com/jianchang512/vocal-separate/releases/download/v0.0.4/vocal-separate-v0.0.4.7z
- Corresponding upstream source is attached to the application release as vocal-separate-v0.0.4-source.zip.
- Original source: https://github.com/jianchang512/vocal-separate/tree/5d11a4cff3b6eafcb41f92a0772301c0e1fe1465
- GPL-3.0; see LICENSE. No warranty. You may modify and redistribute the engine under that license.
- The frozen runtime includes Spleeter, TensorFlow, NumPy, FFmpeg and their dependencies; their original embedded notices are preserved. Spleeter model: 2stems.

Modified on 2026-10-21: replace the web entry script with cli.py, a headless two-stem worker. No browser or HTTP listener is started. Keep the original bootloader, hooks, frozen modules, Python runtime, FFmpeg and model unchanged. Remove the unused 4stems/5stems model directories and web assets. The desktop UI communicates with the separate worker process through file paths and exit status.

Rebuild with Python 3.10 on Windows:

1. Download/extract the pinned upstream binary archive.
2. Run `python tools/vocal-engine/package_engine.py <extracted-upstream-directory> <publish-directory>/engine`.
3. Run the real-model smoke test from `.github/workflows/build.yml`.

The exact adaptation and rebuild script are shipped in the engine folder and in this repository. The original desktop application retains its Apache-2.0 license; the separate engine retains GPL-3.0 and its dependency licenses.
