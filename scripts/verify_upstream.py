"""Cross-check the codec against an independently authored official export.

Downloads only the pinned example, verifies its hash and inspects structure.
The upstream C# source is neither executed nor distributed in this repository.
"""
import hashlib
from urllib.request import urlopen

from build import decode

URL = ("https://raw.githubusercontent.com/Streamerbot/docs/"
       "56253da4b3aebf70a1d134356368f1b13fcecd0e/"
       "streamerbot/4.examples/.imports/chatbot-commands.sb")
SHA256 = "bc2618615c6d7d8cb5bb1af1803229bfd447a10f35611a0ef6c767bd7eac16d0"


def verify():
    with urlopen(URL, timeout=30) as response:
        encoded = response.read()
    if hashlib.sha256(encoded).hexdigest() != SHA256:
        raise ValueError("Pinned upstream fixture checksum mismatch")
    payload = decode(encoded)
    if payload["version"] != 23 or payload["exportedFrom"] != "1.0.4":
        raise ValueError("Unexpected official export format")
    first = payload["data"]["actions"][0]
    if first["triggers"][0]["type"] != 401 or first["subActions"][0]["type"] != 99999:
        raise ValueError("Official command/C# structure differs")
    print("Verified codec against pinned official Streamer.bot 1.0.4 export.")


if __name__ == "__main__":
    verify()
