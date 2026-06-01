#!/usr/bin/env python3
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


DEFAULT_MAX_PATH_LENGTH = 220


def tracked_paths(repo_root: Path) -> list[str]:
    completed = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=repo_root,
        check=True,
        stdout=subprocess.PIPE,
    )
    raw_paths = completed.stdout.decode("utf-8", errors="replace").split("\0")
    return [path for path in raw_paths if path]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Fail when tracked paths are too long for Windows checkout lanes.")
    parser.add_argument("--repo-root", default=".", help="Repository root to inspect.")
    parser.add_argument(
        "--max-path-length",
        type=int,
        default=DEFAULT_MAX_PATH_LENGTH,
        help="Maximum allowed tracked path length from repo root.",
    )
    args = parser.parse_args(argv)

    repo_root = Path(args.repo_root).resolve()
    too_long = [
        path
        for path in tracked_paths(repo_root)
        if len(path) > args.max_path_length
    ]
    if too_long:
        print(
            f"tracked paths exceed Windows checkout path budget ({args.max_path_length} chars):",
            file=sys.stderr,
        )
        for path in sorted(too_long, key=lambda value: (len(value), value), reverse=True):
            print(f"{len(path)} {path}", file=sys.stderr)
        return 1

    print(f"windows checkout path proof passed: tracked paths <= {args.max_path_length} chars")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
