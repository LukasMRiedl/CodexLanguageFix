"""Worker-v2 framing tests that never capture or write a real editor."""
import argparse
import json
import os
import pathlib
import struct
import subprocess
import time


def frame(value):
    data = json.dumps(value).encode("utf-8")
    return struct.pack("<I", len(data)) + data


def decode(data):
    values = []
    while data:
        assert len(data) >= 4, "truncated response prefix"
        length, = struct.unpack("<I", data[:4])
        assert 0 < length <= 262144 and len(data) >= length + 4, "invalid response frame"
        values.append(json.loads(data[4:4 + length]))
        data = data[4 + length:]
    return values


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    exe = str(pathlib.Path(args.exe).resolve())
    checks = []

    def run(name, payload, expected):
        start = time.monotonic()
        process = subprocess.run([exe, "--composer-worker", str(os.getpid())], input=payload, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=8)
        values = decode(process.stdout)
        assert values == expected, (name, values)
        assert not process.stderr, "worker emitted unexpected diagnostic data"
        checks.append({"name": name, "seconds": time.monotonic() - start, "exit_code": process.returncode})

    run("ping_v2", frame({"version": 2, "operation": "ping"}), [{"version": 2, "status": "ok", "capture_status":"not_attempted"}])
    run("v1_rejected", frame({"version": 1, "operation": "ping"}), [])
    run("oversized_rejected", struct.pack("<I", 262145), [])
    run("zero_rejected", struct.pack("<I", 0), [])
    run("truncated_rejected", struct.pack("<I", 20) + b"{}", [])
    run("invalid_utf8_rejected", struct.pack("<I", 1) + b"\xff", [])
    run("unknown_operation_rejected", frame({"version": 2, "operation": "undefined"}), [])
    run("stale_refresh_rejected", frame({"version": 2, "operation": "refresh", "token": 0}), [{"version": 2, "status": "ok", "capture_status":"snapshot_expired"}])
    expected = [{"version": 2, "status": "ok", "capture_status":"not_attempted", "write": {"state": 0, "status": "invalid_identity", "count": 0}}]
    run("stale_write_never_applied", frame({"version": 2, "operation": "replace", "token": 0, "expected": "synthetic", "replacement": "Synthetic"}), expected)
    run("too_long_write_rejected", frame({"version": 2, "operation": "replace", "token": 0, "expected": "a" * 20001, "replacement": "b"}), [])
    run("surrogate_rejected", frame({"version": 2, "operation": "replace", "token": 0, "expected": "\ud800", "replacement": "b"}), [])
    result = {"schema": 1, "passed": True, "checks": checks}
    pathlib.Path(args.report).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(f"{len(checks)} worker protocol checks passed")


if __name__ == "__main__":
    main()
