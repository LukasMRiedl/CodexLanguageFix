"""Compare internal pure-operation timings; never grants product latency acceptance."""

import argparse
import json
import pathlib
import time

from compare_core_latency import NdjsonChild, synthetic_cases


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native", required=True)
    parser.add_argument("--reference", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    deadline = time.monotonic() + 180
    native = reference = None
    report = {
        "schema": 1,
        "scope": "internal pure-operation timings; excludes JSON, pipe I/O, result consumption and native result destruction; not overlay/correction/undo acceptance",
        "releasePass": False,
        "cases": [],
    }
    try:
        native = NdjsonChild([args.native], deadline)
        reference = NdjsonChild([args.reference], deadline)
        for index, case in enumerate(synthetic_cases()):
            request = {**case, "coreCostProbe": True}
            children = (native, reference) if index % 2 else (reference, native)
            replies = {child: child.request(request)[0] for child in children}
            cpp, csharp = replies[native], replies[reference]
            cpp_ops = {op["id"]: op for op in cpp["operations"]}
            csharp_ops = {op["id"]: op for op in csharp["operations"]}
            if cpp_ops.keys() != csharp_ops.keys():
                raise RuntimeError("operation_ids_differ")
            comparisons = []
            for operation_id in cpp_ops:
                left, right = csharp_ops[operation_id], cpp_ops[operation_id]
                equal = all(left[key] == right[key] for key in ("resultCount", "checksum"))
                comparisons.append({
                    "id": operation_id,
                    "resultConsumptionEqual": equal,
                    "csharp": left,
                    "cpp": right,
                    "cppMinusCsharpMedianUs": right["medianUs"] - left["medianUs"],
                    "cppMinusCsharpP95Us": right["p95Us"] - left["p95Us"],
                })
            report["cases"].append({
                "caseId": case["id"],
                "originalUtf16Units": cpp["originalUtf16Units"],
                "correctedUtf16Units": cpp["correctedUtf16Units"],
                "warmupIterations": cpp["warmupIterations"],
                "measuredIterations": cpp["measuredIterations"],
                "operations": comparisons,
            })
        report["status"] = "passed" if all(
            operation["resultConsumptionEqual"]
            for case in report["cases"] for operation in case["operations"]
        ) else "result_consumption_mismatch"
    except (OSError, RuntimeError, KeyError, TypeError, TimeoutError):
        report["status"] = "harness_error"
    finally:
        if native:
            native.close()
        if reference:
            reference.close()
    target = pathlib.Path(args.report)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": report["status"], "releasePass": False}))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
