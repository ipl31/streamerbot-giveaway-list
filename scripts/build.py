"""Build Streamer.bot's Base64(SBAE + gzip(JSON)) import envelope."""
import argparse
import base64
import gzip
import hashlib
import io
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def decode(encoded):
    raw = base64.b64decode(encoded.strip(), validate=True)
    if raw[:4] != b"SBAE":
        raise ValueError("Not a Streamer.bot SBAE envelope")
    return json.loads(gzip.decompress(raw[4:]).decode("utf-8-sig"))


def encode(payload):
    raw = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    # GzipFile fixes the OS header byte to 255 across Linux and Windows;
    # gzip.compress(mtime=0) can use a platform-specific OS byte on Python 3.12.
    compressed = io.BytesIO()
    with gzip.GzipFile(fileobj=compressed, mode="wb", filename="", mtime=0) as stream:
        stream.write(raw)
    return base64.b64encode(b"SBAE" + compressed.getvalue()).decode("ascii")


def walk_subactions(items):
    for item in items:
        yield item
        yield from walk_subactions(item.get("subActions", []))


def assemble():
    version = (ROOT / "VERSION").read_text().strip()
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version):
        raise ValueError("VERSION must be a semantic version")
    payload = json.loads((ROOT / "extension/template.json").read_text())
    payload["meta"]["version"] = version
    source = (ROOT / "src/GiveawayList.cs").read_bytes()
    for action in payload["data"]["actions"]:
        for subaction in walk_subactions(action["subActions"]):
            if subaction["type"] == 99999:
                subaction["byteCode"] = base64.b64encode(source).decode("ascii")
    return version, payload


def build(output):
    version, payload = assemble()
    output.mkdir(parents=True, exist_ok=True)
    artifact = output / ("streamerbot-giveaway-list-" + version + ".sb")
    artifact.write_text(encode(payload) + "\n", encoding="ascii")
    digest = hashlib.sha256(artifact.read_bytes()).hexdigest()
    artifact.with_suffix(".sb.sha256").write_text(digest + "  " + artifact.name + "\n")
    print(artifact)
    return artifact


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "dist")
    parser.add_argument("--tag", help="Verify a release tag matches VERSION")
    args = parser.parse_args()
    if args.tag and args.tag != "v" + (ROOT / "VERSION").read_text().strip():
        parser.error("release tag does not match VERSION")
    build(args.output)
