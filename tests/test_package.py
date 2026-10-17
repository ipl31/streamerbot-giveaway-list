import base64
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import build


class PackageTests(unittest.TestCase):
    def test_envelope_and_embedded_source(self):
        with tempfile.TemporaryDirectory() as directory:
            artifact = build.build(Path(directory))
            payload = build.decode(artifact.read_bytes())
            self.assertEqual(payload["version"], 23)
            self.assertEqual(payload["minimumVersion"], "1.0.4")
            self.assertEqual(payload["meta"]["version"], (build.ROOT / "VERSION").read_text().strip())
            self.assertIsNone(payload["meta"]["autoRunAction"])
            code = payload["data"]["actions"][0]["subActions"][1]
            self.assertEqual(base64.b64decode(code["byteCode"]), (build.ROOT / "src/GiveawayList.cs").read_bytes())
            self.assertEqual(code["references"], [])
            digest, filename = artifact.with_suffix(".sb.sha256").read_text().split()
            self.assertEqual(filename, artifact.name)
            self.assertEqual(digest, hashlib.sha256(artifact.read_bytes()).hexdigest())

    def test_import_wiring_and_permissions(self):
        _, payload = build.assemble()
        data = payload["data"]
        self.assertEqual(len(data["actions"]), 1)
        self.assertEqual(len(data["commands"]), 1)
        action, command = data["actions"][0], data["commands"][0]
        self.assertEqual(command["command"], "!giveawaylist")
        self.assertFalse(command["enabled"])
        self.assertEqual(command["sources"], 1)
        self.assertEqual(command["location"], 1)
        self.assertEqual(command["permittedGroups"], ["Moderators"])
        self.assertEqual(command["permittedUsers"], [])
        self.assertEqual(command["grantType"], 0)
        self.assertEqual(len(action["triggers"]), 1)
        trigger = action["triggers"][0]
        self.assertEqual(trigger["type"], 401)
        self.assertEqual(trigger["commandId"], command["id"])
        self.assertEqual([s["type"] for s in action["subActions"]], [22, 99999])
        self.assertEqual([s["index"] for s in action["subActions"]], [0, 1])
        collect = action["subActions"][0]
        self.assertIs(collect["present"], True)
        self.assertEqual(collect["count"], "100000")  # Native model stores count as a string.
        self.assertIs(collect["includeFrom"], False)
        self.assertEqual(collect["groups"], [])
        identifiers = [action["id"], command["id"], trigger["id"]]
        identifiers += [s["id"] for s in action["subActions"]]
        for identifier in identifiers:
            self.assertEqual(str(uuid.UUID(identifier)), identifier)
        self.assertEqual(len(set(identifiers)), len(identifiers))
        for key in ("queues", "timers", "websocketServers", "websocketClients"):
            self.assertEqual(data[key], [])

    def test_deterministic_output(self):
        with tempfile.TemporaryDirectory() as directory:
            artifact = build.build(Path(directory))
            first = artifact.read_bytes()
            build.build(Path(directory))
            self.assertEqual(first, artifact.read_bytes())

    def test_bad_envelope_rejected(self):
        with self.assertRaises(ValueError):
            build.decode(base64.b64encode(b"WRONG"))

    def test_mismatched_release_tag_fails_before_writing(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, str(build.ROOT / "scripts/build.py"),
                                     "--tag", "v999.0.0", "--output", directory],
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("does not match VERSION", result.stderr)
            self.assertEqual(list(Path(directory).iterdir()), [])


if __name__ == "__main__":
    unittest.main()
