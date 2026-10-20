"""Obtain only the two official DLLs required for compatibility checks."""
import argparse
import hashlib
import io
from pathlib import Path
import shutil
from urllib.request import urlopen
from zipfile import ZipFile

URL = "https://streamer.bot/api/releases/streamer.bot/1.0.4/download"
SHA256 = "de660b8a86eaeeccab0c7f22ebdd7102b6671ff59fee2839294c843454e1ce36"
NAMES = {"Streamer.bot.Plugin.Interface.dll", "Newtonsoft.Json.dll"}
ROOT = Path(__file__).resolve().parents[1]


def prepare(output, source=None):
    output.mkdir(parents=True, exist_ok=True)
    if source:
        for name in NAMES:
            shutil.copyfile(source / name, output / name)
        return
    with urlopen(URL, timeout=60) as response:
        data = response.read(50 * 1024 * 1024 + 1)
    if len(data) > 50 * 1024 * 1024 or hashlib.sha256(data).hexdigest() != SHA256:
        raise ValueError("Official Streamer.bot package checksum mismatch")
    with ZipFile(io.BytesIO(data)) as archive:
        found = set()
        for member in archive.infolist():
            name = Path(member.filename).name
            if name in NAMES:
                if name in found:
                    raise ValueError("Duplicate compatibility DLL")
                (output / name).write_bytes(archive.read(member))
                found.add(name)
        if found != NAMES:
            raise ValueError("Missing official compatibility DLL")
    print("Verified official 1.0.4 API and JSON assemblies for compatibility checks.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / ".framework-runtime")
    parser.add_argument("--source", type=Path, help="Use an already inspected local application directory")
    args = parser.parse_args()
    prepare(args.output, args.source)
