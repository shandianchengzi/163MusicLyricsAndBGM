# SPDX-License-Identifier: GPL-3.0-only
# Adapted from jianchang512/vocal-separate v0.0.4 start.py (2026-10-21).
# This headless entry point replaces the web UI, retaining the same Spleeter
# two-stem separation pipeline. See LICENSE and UPSTREAM.md in this directory.
import os
import sys
import subprocess


def main():
    if len(sys.argv) != 3:
        raise ValueError("Expected absolute input path and output directory")
    source, destination = map(os.path.abspath, sys.argv[1:])
    root = os.path.dirname(os.path.abspath(sys.executable))
    os.chdir(root)
    os.environ['PATH'] = root + os.pathsep + os.environ.get('PATH', '')
    os.environ['MODEL_PATH'] = os.path.join(root, 'pretrained_models')
    # Use the shipped checkpoint only; no first-run download is necessary.
    model = os.path.join(root, 'pretrained_models', '2stems')
    if not os.path.isfile(os.path.join(model, 'model.meta')):
        raise FileNotFoundError('Bundled 2stems model is missing')
    probe = subprocess.run([
        os.path.join(root, 'ffprobe.exe'), '-v', 'error', '-show_entries',
        'format=duration', '-of', 'default=noprint_wrappers=1:nokey=1', source
    ], capture_output=True, check=True, creationflags=subprocess.CREATE_NO_WINDOW)
    duration = float(probe.stdout.strip())
    if not 0 < duration < float('inf'):
        raise ValueError('Invalid audio duration')
    # Spleeter's ffmpeg-python subprocesses must also remain windowless.
    original_popen = subprocess.Popen

    class QuietPopen(original_popen):
        def __init__(self, *args, **kwargs):
            kwargs.setdefault('creationflags', subprocess.CREATE_NO_WINDOW)
            super().__init__(*args, **kwargs)

    subprocess.Popen = QuietPopen
    from spleeter.separator import Separator
    separator = Separator('spleeter:2stems', multiprocess=False)
    separator.separate_to_file(source, destination=destination,
                              filename_format='{instrument}.{codec}', duration=duration)
    if not os.path.isfile(os.path.join(destination, 'accompaniment.wav')):
        raise RuntimeError('Separation produced no accompaniment.wav')
    print('Separation completed', flush=True)


if __name__ == '__main__':
    main()
