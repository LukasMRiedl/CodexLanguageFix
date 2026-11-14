"""Run the retained C# oracle and native pure-operation harness on identical input."""
import argparse
import json
import pathlib
import random
import subprocess
import time


def settings_cases():
    fixtures = [
        (
            "integer_strings",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"shortcutModifiers":"6","shortcutVirtualKey":"81","enabled":false,"language":"de"}',
        ),
        (
            "integer_string_leading_zero",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"shortcutModifiers":"06","shortcutVirtualKey":"188"}',
        ),
        (
            "integer_string_plus_sign_invalid",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"shortcutModifiers":"+6","shortcutVirtualKey":"81"}',
        ),
        (
            "integer_string_leading_space_invalid",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"shortcutModifiers":" 6","shortcutVirtualKey":"81"}',
        ),
        (
            "integer_string_trailing_space_invalid",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"shortcutModifiers":"6 ","shortcutVirtualKey":"81"}',
        ),
        (
            "uint_negative_zero_invalid",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"shortcutModifiers":"3","shortcutVirtualKey":"-0"}',
        ),
        (
            "signed_negative_zero",
            '{"settingsVersion":"-0","correctionProvider":1,"prepareLunaCorrections":true,'
            '"shortcutModifiers":"3","shortcutVirtualKey":"188"}',
        ),
        (
            "numeric_string_version_migration",
            '{"settingsVersion":"2","prepareLunaCorrections":true,'
            '"shortcutModifiers":"3","shortcutVirtualKey":"188"}',
        ),
        (
            "case_insensitive_deserialization_last",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"enabled":false,"Enabled":true,"shortcutModifiers":8,"ShortcutModifiers":"6",'
            '"shortcutVirtualKey":65,"ShortcutVirtualKey":"81"}',
        ),
        (
            "duplicate_provider_case_insensitive_first",
            '{"correctionProvider":0,"CorrectionProvider":1,"settingsVersion":2,'
            '"prepareLunaCorrections":true,"enabled":false}',
        ),
        (
            "duplicate_provider_exact_first",
            '{"correctionProvider":0,"correctionProvider":1,"settingsVersion":2,'
            '"prepareLunaCorrections":true}',
        ),
        (
            "duplicate_version_case_insensitive_first_for_migration",
            '{"settingsVersion":1,"SettingsVersion":2,"prepareLunaCorrections":true,"enabled":false}',
        ),
        (
            "duplicate_version_exact_first_for_migration",
            '{"settingsVersion":1,"settingsVersion":2,"prepareLunaCorrections":true}',
        ),
        (
            "invalid_duplicate_exact_before_valid",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"enabled":"bad","enabled":false}',
        ),
        (
            "invalid_duplicate_case_insensitive_before_valid",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"enabled":"bad","Enabled":false}',
        ),
        (
            "invalid_duplicate_case_insensitive_after_valid",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"enabled":false,"Enabled":"bad"}',
        ),
        (
            "invalid_integer_string_resets_defaults",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,'
            '"enabled":false,"shortcutVirtualKey":"81x"}',
        ),
        (
            "utf8_bom",
            '\ufeff{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,"language":"de"}',
        ),
        (
            "language_null_effective_auto",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,"language":null}',
        ),
        (
            "language_unknown_preserved",
            '{"settingsVersion":2,"correctionProvider":1,"prepareLunaCorrections":true,"language":"FR"}',
        ),
        ("root_null", "null"),
    ]
    return [{"caseId": case_id, "settingsJson": settings_json} for case_id, settings_json in fixtures]


def cases(root):
    corpus = json.loads((root / "benchmark_cases.json").read_text(encoding="utf-8-sig"))
    result = []
    for language in ("de", "en"):
        for case in corpus[language]:
            result.append({"original": case["input"], "corrected": case["target"]})
    atoms = ["korekt", "korrekt", "e\u0301", "é", "👩🏽‍💻", "👨‍👩‍👧‍👦", "🇩🇪", "\u200fשלום", "العربية", "中文", "日本語", "क्‍ष", "a\u034f\u0308", "각", "https://example.test/falsch?x=1", "`var eror = 1;`", "[Titel](https://a.test/x)", "C:\\Temp\\file.txt", "--option", "foo_bar", "a@b.test", "\u27e6CLF_PROTECTED_FF_0001\u27e7"]
    prefixes = ["", "  ", "> ", "- ", "1. ", "## ", "- [x] ", "\t"]
    endings = ["", "\n", "\r\n", "\r"]
    rng = random.Random(154)
    for _ in range(800):
        before = rng.choice(prefixes) + " ".join(rng.choices(atoms, k=rng.randint(1, 12))) + rng.choice(["", "  ", "\t"]) + rng.choice(endings)
        after = before.replace("korekt", "korrekt")
        if rng.randrange(4) == 0:
            after = after.replace("\r\n", "\n").strip()
        if rng.randrange(6) == 0:
            after += "\nNeue Zeile."
        result.append({"original": before, "corrected": after})
    result.extend([
        {"original": "", "corrected": ""},
        {"original": "Satz. Satz. Satz.", "corrected": "Satz. Satz."},
        {"original": "```text\r\ncode\r\n```\r\n\r\nDer Satz ist korekt.\r\n", "corrected": "```text\r\ncode\r\n```\r\n\r\nDer Satz ist korrekt.\r\n"},
        {"original": "a" * 19992 + " korekt.", "corrected": "a" * 19992 + " korrekt."},
        {"original": "👩🏽‍💻" * 1000 + " e\u0301 korekt.", "corrected": "👩🏽‍💻" * 1000 + " e\u0301 korrekt."},
    ])
    # Exercise segmentation-property transitions independently of German spelling.
    unicode_atoms = ["\r", "\n", "\x00", "\x85", "\u0300", "\u034f", "\u200c", "\u200d", "\u0600", "\u0903", "\u093e", "\u094d", "\u1100", "\u1161", "\u11a8", "\uac00", "\uac01", "\ufe0f", "\U0001f1e6", "\U0001f1e7", "\U0001f3fd", "\U0001f469", "\U0001f4bb", "\U000e0020", "\U000e007f", "a", " "]
    for _ in range(2000):
        text = "".join(rng.choices(unicode_atoms, k=rng.randint(1, 18)))
        result.append({"original": text, "corrected": text})
    for _ in range(1000):
        scalar = rng.randrange(0x110000)
        if 0xD800 <= scalar <= 0xDFFF:
            continue
        text = chr(scalar) + "\u0301\u200d" + rng.choice(unicode_atoms)
        result.append({"original": text, "corrected": text})
    result.extend(settings_cases())
    return result


def run(command, payload):
    start = time.monotonic()
    process = subprocess.run(command, input=payload, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=180, check=True)
    return [json.loads(line) for line in process.stdout.splitlines()], time.monotonic() - start


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--native", required=True)
    parser.add_argument("--reference", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parents[2]
    inputs = cases(root)
    payload = "".join(json.dumps(case, ensure_ascii=False) + "\n" for case in inputs).encode("utf-8")
    reference, reference_seconds = run([args.reference], payload)
    native, native_seconds = run([args.native], payload)
    failures = []
    for index, (case, expected, observed) in enumerate(zip(inputs, reference, native)):
        if expected != observed:
            failures.append({"index": index, "input": case, "differences": {key: {"reference": expected.get(key), "native": observed.get(key)} for key in expected.keys() | observed.keys() if expected.get(key) != observed.get(key)}})
    valid_count = len(reference) == len(native) == len(inputs)
    report = {"schema": 1, "cases": len(inputs), "reference_rows": len(reference), "native_rows": len(native), "passed": valid_count and not failures, "reference_seconds": reference_seconds, "native_seconds": native_seconds, "failures": failures}
    pathlib.Path(args.report).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "failures"}))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
