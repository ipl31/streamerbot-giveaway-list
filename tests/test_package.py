import base64
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import uuid
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import build


class PackageTests(unittest.TestCase):
    def test_104_passes_both_native_import_version_gates(self):
        # Streamer.bot 1.0.4 importer: root minimum is inclusive (>=), but
        # optional meta.minimumVersion is exclusive (>). Setting both to
        # 1.0.4 caused v0.1.0 to reject precisely the version it advertised.
        _, payload = build.assemble()
        self.assertIsNone(payload["meta"]["minimumVersion"])
        minimum = tuple(map(int, payload["minimumVersion"].split(".")))
        optional = payload["meta"]["minimumVersion"]
        def accepts(version):
            current = tuple(map(int, version.split(".")))
            return current >= minimum and (
                optional is None or current > tuple(map(int, optional.split("."))))
        self.assertFalse(accepts("1.0.3"))
        self.assertTrue(accepts("1.0.4"))
        self.assertTrue(accepts("1.0.5"))

    def test_envelope_and_embedded_source(self):
        with tempfile.TemporaryDirectory() as directory:
            artifact = build.build(Path(directory))
            payload = build.decode(artifact.read_bytes())
            self.assertEqual(payload["version"], 23)
            self.assertEqual(payload["minimumVersion"], "1.0.4")
            self.assertEqual(payload["meta"]["version"], (build.ROOT / "VERSION").read_text().strip())
            self.assertIsNone(payload["meta"]["autoRunAction"])
            codes = [s for s in build.walk_subactions(payload["data"]["actions"][0]["subActions"])
                     if s["type"] == 99999]
            self.assertEqual(len(codes), 2)
            for code in codes:
                self.assertEqual(base64.b64decode(code["byteCode"]), build.inline_source(code["id"] == build.FETCH_CODE_ID))
                self.assertEqual(code["references"], [])
            self.assertTrue(base64.b64decode(codes[0]["byteCode"]).startswith(b"#define GIVEAWAY_FETCH\n"))
            self.assertFalse(base64.b64decode(codes[1]["byteCode"]).startswith(b"#define"))
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
        self.assertEqual([s["type"] for s in action["subActions"]], [99900, 99900, 99900])
        self.assertEqual([s["index"] for s in action["subActions"]], [0, 1, 2])
        self.assertEqual([s["name"] for s in action["subActions"]], [
            "Get all connected Twitch users", "Save timestamped giveaway file",
            "Post users to chat (if the list fits)"])
        self.assertEqual(action["collapsedGroups"], [])
        collect = action["subActions"][0]["subActions"][0]
        self.assertEqual(collect["type"], 99999)
        self.assertEqual(collect["id"], build.FETCH_CODE_ID)
        self.assertFalse(collect["saveResultToVariable"])
        self.assertNotIn(22, [s["type"] for s in build.walk_subactions(action["subActions"])])
        self.assertNotIn("randomUserName", build.inline_source().decode())
        identifiers = [action["id"], command["id"], trigger["id"]]
        identifiers += [s["id"] for s in build.walk_subactions(action["subActions"])]
        for identifier in identifiers:
            self.assertEqual(str(uuid.UUID(identifier)), identifier)
        self.assertEqual(len(set(identifiers)), len(identifiers))
        for key in ("queues", "timers", "websocketServers", "websocketClients"):
            self.assertEqual(data[key], [])

        def check_children(items, parent=None):
            for index, child in enumerate(items):
                self.assertEqual(child["index"], index)
                self.assertEqual(child["parentId"], parent)
                self.assertTrue(child["enabled"])
                if "subActions" in child:
                    self.assertFalse(child.get("random", False))
                    check_children(child["subActions"], child["id"])
        check_children(action["subActions"])

    def test_native_chat_subactions_use_length_condition(self):
        _, payload = build.assemble()
        groups = payload["data"]["actions"][0]["subActions"]
        code = groups[1]["subActions"][0]
        source = base64.b64decode(code["byteCode"]).decode()
        self.assertNotIn("CPH.SendMessage(", source)  # Sending must be visible in the UI.
        self.assertEqual(code["name"], "Save timestamped giveaway file")
        self.assertFalse(code["saveResultToVariable"])  # Unauthorized calls must stop before chat.
        condition = groups[2]["subActions"][0]
        self.assertEqual(condition["type"], 120)
        self.assertEqual(condition["input"], "%giveawayListFitsChat%")
        self.assertEqual(condition["operation"], 0)  # Equals.
        self.assertEqual(condition["value"], "True")
        self.assertTrue(condition["autoType"])
        yes, no = condition["subActions"]
        self.assertEqual([yes["type"], no["type"]], [99901, 99902])
        for branch in [yes, no]:
            self.assertEqual(len(branch["subActions"]), 1)
            message = branch["subActions"][0]
            self.assertEqual(message["type"], 10)  # Native Twitch Send Message.
            self.assertTrue(message["useBot"])
            self.assertTrue(message["fallback"])
        self.assertEqual(yes["subActions"][0]["text"], "%giveawayListChatMessage%")
        self.assertEqual(no["subActions"][0]["text"],
                         "User list is too long for chat. Mods: please ask the streamer to check the text file.")

    def test_deterministic_output(self):
        with tempfile.TemporaryDirectory() as directory:
            artifact = build.build(Path(directory))
            first = artifact.read_bytes()
            envelope = base64.b64decode(first)
            self.assertEqual(envelope[8:12], b"\0\0\0\0")  # gzip timestamp
            self.assertEqual(envelope[13], 255)  # canonical OS header
            build.build(Path(directory))
            self.assertEqual(first, artifact.read_bytes())

    def test_bad_envelope_rejected(self):
        with self.assertRaises(ValueError):
            build.decode(base64.b64encode(b"WRONG"))

    def test_missing_duplicate_or_unknown_code_binding_fails(self):
        template = json.loads((build.ROOT / "extension/template.json").read_text())
        for problem in ["missing", "duplicate", "unknown"]:
            with tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                (root / "src").mkdir()
                (root / "extension").mkdir()
                (root / "VERSION").write_text("0.4.0\n")
                for filename in ["GiveawayList.cs", "ChattersClient.cs"]:
                    (root / "src" / filename).write_bytes((build.ROOT / "src" / filename).read_bytes())
                payload = json.loads(json.dumps(template))
                groups = payload["data"]["actions"][0]["subActions"]
                if problem == "missing": groups[0]["subActions"] = []
                elif problem == "duplicate": groups[1]["subActions"][0]["id"] = build.FETCH_CODE_ID
                else: groups[0]["subActions"][0]["id"] = str(uuid.uuid4())
                (root / "extension/template.json").write_text(json.dumps(payload))
                with patch.object(build, "ROOT", root):
                    with self.assertRaises(ValueError): build.assemble()

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
