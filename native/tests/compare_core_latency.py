"""Compare retained C# and native pure-operation NDJSON round trips."""

import argparse
import json
import math
import pathlib
import queue
import statistics
import subprocess
import threading
import time


TIMEOUT_SECONDS = 180
WARMUP_PAIRS = 3
MEASURED_PAIRS = 30


def utf16_units(text):
    return len(text.encode("utf-16-le")) // 2


def synthetic_cases():
    cases = [
        {
            "id": "short_de",
            "original": "Der kurze Satz ist korekt.",
            "corrected": "Der kurze Satz ist korrekt.",
        },
        {
            "id": "unicode_mixed",
            "original": "Grüße e\u0301 👩🏽‍💻 क्‍ष — der Satz ist korekt.",
            "corrected": "Grüße e\u0301 👩🏽‍💻 क्‍ष — der Satz ist korrekt.",
        },
        {
            "id": "markdown_structure",
            "original": (
                "## Prüfplan\r\nBitte öffne [die Anleitung]"
                "(https://example.test/guide?lang=de&step=2) und prüfe `const eror = 1;`.\r\n"
                "\r\nDer Satz ist korekt."
            ),
            "corrected": (
                "## Prüfplan\r\nBitte öffne [die Anleitung]"
                "(https://example.test/guide?lang=de&step=2) und prüfe `const eror = 1;`.\r\n"
                "\r\nDer Satz ist korrekt."
            ),
        },
    ]

    prefix = (
        "Grüße e\u0301 👩🏽‍💻 [Anleitung](https://example.test/a?x=1&y=2) "
        "`const eror = 1;`\r\n"
    )
    suffix = "\r\nDer Status ist koreckt."
    padding_units = 20_000 - utf16_units(prefix) - utf16_units(suffix)
    if padding_units < 0:
        raise RuntimeError("synthetic long-case prefix exceeds target length")
    long_original = prefix + ("x" * padding_units) + suffix
    if utf16_units(long_original) != 20_000:
        raise RuntimeError("synthetic long-case length is not 20,000 UTF-16 units")
    long_corrected = long_original.replace("koreckt.", "korrekt.", 1)
    if utf16_units(long_corrected) != 20_000:
        raise RuntimeError("synthetic corrected long-case length is not 20,000 UTF-16 units")
    cases.append({
        "id": "mixed_unicode_markdown_20k",
        "original": long_original,
        "corrected": long_corrected,
    })
    return cases


class NdjsonChild:
    def __init__(self, command, deadline):
        self.deadline = deadline
        self.responses = queue.Queue()
        self.process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            encoding="utf-8",
            errors="strict",
            bufsize=1,
        )
        self.reader = threading.Thread(target=self._read_stdout, daemon=True)
        self.reader.start()

    def _read_stdout(self):
        try:
            for line in self.process.stdout:
                self.responses.put(line)
        except BaseException:
            self.responses.put(None)
        else:
            self.responses.put(None)

    def request(self, payload):
        started = time.perf_counter_ns()
        try:
            self.process.stdin.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
            self.process.stdin.flush()
        except (BrokenPipeError, OSError):
            raise RuntimeError("child_stdin_closed") from None

        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("total_timeout")
        try:
            line = self.responses.get(timeout=remaining)
        except queue.Empty:
            raise TimeoutError("total_timeout") from None
        if line is None:
            raise RuntimeError("child_stdout_closed")
        try:
            result = json.loads(line)
        except json.JSONDecodeError:
            raise RuntimeError("child_reply_invalid_json") from None
        return result, time.perf_counter_ns() - started

    def close(self):
        if self.process.poll() is not None:
            return
        try:
            self.process.stdin.close()
        except OSError:
            pass
        try:
            self.process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            self.process.terminate()
            try:
                self.process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=2)


def canonical_json(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def percentile(values, quantile):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(quantile * len(ordered)) - 1)]


def summarize_ns(values):
    return {
        "medianMs": statistics.median(values) / 1_000_000,
        "p95Ms": percentile(values, 0.95) / 1_000_000,
        "maxMs": max(values) / 1_000_000,
    }


def compare_case(case, csharp_child, cpp_child, case_index, deadline):
    csharp_samples = []
    cpp_samples = []
    equal_warmup_pairs = 0
    equal_measured_pairs = 0
    equality_failures = 0

    for trial in range(WARMUP_PAIRS + MEASURED_PAIRS):
        if time.monotonic() >= deadline:
            raise TimeoutError("total_timeout")
        csharp_first = (trial + case_index) % 2 == 0
        if csharp_first:
            csharp_result, csharp_ns = csharp_child.request(case)
            cpp_result, cpp_ns = cpp_child.request(case)
        else:
            cpp_result, cpp_ns = cpp_child.request(case)
            csharp_result, csharp_ns = csharp_child.request(case)

        equal = canonical_json(csharp_result) == canonical_json(cpp_result)
        if equal:
            if trial < WARMUP_PAIRS:
                equal_warmup_pairs += 1
            else:
                equal_measured_pairs += 1
        else:
            equality_failures += 1
        if trial >= WARMUP_PAIRS:
            csharp_samples.append(csharp_ns)
            cpp_samples.append(cpp_ns)

    delta_samples = [left - right for left, right in zip(csharp_samples, cpp_samples)]
    return {
        "caseId": case["id"],
        "originalUtf16Units": utf16_units(case["original"]),
        "correctedUtf16Units": utf16_units(case["corrected"]),
        "pairedRuns": MEASURED_PAIRS,
        "equalWarmupPairs": equal_warmup_pairs,
        "equalMeasuredPairs": equal_measured_pairs,
        "equalityFailures": equality_failures,
        "csharp": summarize_ns(csharp_samples),
        "cpp": summarize_ns(cpp_samples),
        "csharpMinusCpp": summarize_ns(delta_samples),
        "maxAbsoluteDifferenceMs": max(abs(value) for value in delta_samples) / 1_000_000,
    }


def write_report(path, report):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main():
    root = pathlib.Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--native",
        default=str(root / "native" / "build" / "Release" / "native_differential.exe"),
    )
    parser.add_argument(
        "--reference",
        default=str(root / "native" / "tests" / "reference" / "bin" / "Release"
                    / "net8.0-windows" / "win-x64" / "CodexLanguageFix.Tests.exe"),
    )
    parser.add_argument("--report", default=str(root / "benchmark-results" / "native-core-latency.json"))
    args = parser.parse_args()

    report_path = pathlib.Path(args.report)
    cases = synthetic_cases()
    deadline = time.monotonic() + TIMEOUT_SECONDS
    csharp_child = cpp_child = None
    try:
        csharp_child = NdjsonChild([args.reference], deadline)
        cpp_child = NdjsonChild([args.native], deadline)
        results = [
            compare_case(case, csharp_child, cpp_child, index, deadline)
            for index, case in enumerate(cases)
        ]
        failures = sum(item["equalityFailures"] for item in results)
        report = {
            "schema": 1,
            "status": "passed" if failures == 0 else "output_mismatch",
            "scope": "pure core-operation NDJSON round trips, including JSON and pipe I/O; not overlay, real correction, or undo latency",
            "releasePass": False,
            "timeoutSeconds": TIMEOUT_SECONDS,
            "warmupPairsPerCase": WARMUP_PAIRS,
            "measuredPairsPerCase": MEASURED_PAIRS,
            "orderAlternated": True,
            "equalityFailures": failures,
            "cases": results,
        }
        write_report(report_path, report)
        print(json.dumps({
            "status": report["status"],
            "report": str(report_path),
            "caseCount": len(results),
            "equalityFailures": failures,
            "releasePass": False,
        }, ensure_ascii=False))
        return 0 if failures == 0 else 1
    except TimeoutError:
        write_report(report_path, {
            "schema": 1,
            "status": "timeout",
            "scope": "pure core-operation NDJSON round trips; not overlay, real correction, or undo latency",
            "releasePass": False,
            "timeoutSeconds": TIMEOUT_SECONDS,
        })
        print(json.dumps({"status": "timeout", "report": str(report_path), "releasePass": False}))
        return 2
    except (OSError, RuntimeError):
        write_report(report_path, {
            "schema": 1,
            "status": "harness_error",
            "scope": "pure core-operation NDJSON round trips; not overlay, real correction, or undo latency",
            "releasePass": False,
        })
        print(json.dumps({"status": "harness_error", "report": str(report_path), "releasePass": False}))
        return 2
    finally:
        if csharp_child is not None:
            csharp_child.close()
        if cpp_child is not None:
            cpp_child.close()


if __name__ == "__main__":
    raise SystemExit(main())
