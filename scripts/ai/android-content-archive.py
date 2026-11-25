#!/usr/bin/env python3
"""Download one immutable Actions ZIP, check its digest, and verify canonical content.

The network path uses only the existing read-only token. gh owns the official
archive redirect; no credentials or redirect URLs are logged. --archive exercises
the same extraction/verification path offline against an exact producer checkout.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import resource
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import zipfile

MAX_ARCHIVE_BYTES = 40 * 1024 * 1024

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None

def wait_for_artifact(env, open_url=None, sleep=time.sleep):
    artifact_id = env["CONTENT_ARTIFACT_ID"]
    digest = env["CONTENT_ARTIFACT_DIGEST"].removeprefix("sha256:")
    repository = env["GITHUB_REPOSITORY"]
    run_id = env["GITHUB_RUN_ID"]
    repository_id = env["GITHUB_REPOSITORY_ID"]
    run_attempt = env["GITHUB_RUN_ATTEMPT"]
    sha = env["GITHUB_SHA"]
    if (not all(re.fullmatch(r"[1-9][0-9]*", value) for value in
                (artifact_id, run_id, repository_id, run_attempt))
            or not re.fullmatch(r"[0-9a-f]{64}", digest)
            or not re.fullmatch(r"[0-9a-f]{40}", sha)
            or repository != "ArchonMegalon/chummer6-core"):
        raise RuntimeError("invalid upload authority receipt")
    name = f"chummer-core-android-content-{sha}-{run_id}-{run_attempt}"
    request = urllib.request.Request(
        f"https://api.github.com/repos/{repository}/actions/artifacts/{artifact_id}",
        headers={"Authorization": f"Bearer {env['GH_TOKEN']}",
                 "Accept": "application/vnd.github+json",
                 "X-GitHub-Api-Version": "2022-11-28"},
    )
    if open_url is None:
        open_url = urllib.request.build_opener(NoRedirect()).open
    # At most 12 ten-second requests and 11 fifteen-second waits.
    # Read only: never re-upload or select an artifact by name/latest.
    for attempt in range(12):
        try:
            with open_url(request, timeout=10) as response:
                artifact = json.load(response)
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise RuntimeError(f"artifact lookup failed: HTTP {error.code}") from None
            if attempt == 11:
                raise RuntimeError("exact uploaded artifact remained invisible after 12 attempts") from None
            print(f"Exact artifact {artifact_id} not visible; retry {attempt + 1}/11", flush=True)
            sleep(15)
            continue
        run = artifact.get("workflow_run") or {}
        if (artifact.get("id") != int(artifact_id)
                or artifact.get("name") != name
                or artifact.get("expired") is not False
                or artifact.get("digest") != f"sha256:{digest}"
                or run.get("id") != int(run_id)
                or run.get("repository_id") != int(repository_id)):
            raise RuntimeError("visible artifact does not match exact upload authority receipt")
        return artifact



def download_archive(env, archive_path, expected_size, run=subprocess.run, sleep=time.sleep):
    """Bound exact-ID GETs; stderr stays private and is never surfaced to logs."""
    artifact_id = env["CONTENT_ARTIFACT_ID"]
    if (env["GITHUB_REPOSITORY"] != "ArchonMegalon/chummer6-core"
            or not re.fullmatch(r"[1-9][0-9]*", artifact_id)
            or type(expected_size) is not int or not 0 < expected_size <= MAX_ARCHIVE_BYTES):
        raise RuntimeError("invalid exact archive authority")
    endpoint = f"repos/ArchonMegalon/chummer6-core/actions/artifacts/{artifact_id}/zip"
    process_env = dict(env)
    # Prevent gh diagnostic tracing from logging request headers/signed redirects.
    process_env.pop("GH_DEBUG", None)
    process_env.pop("DEBUG", None)
    process_env["GH_PROMPT_DISABLED"] = "1"

    def limit_output():
        resource.setrlimit(resource.RLIMIT_FSIZE, (expected_size + 1, expected_size + 1))

    for attempt in range(6):
        with archive_path.open("xb") as output, tempfile.TemporaryFile() as errors:
            try:
                result = run(
                    ["gh", "api", "--hostname", "github.com", "--method", "GET", endpoint],
                    env=process_env, stdin=subprocess.DEVNULL, stdout=output, stderr=errors,
                    timeout=20, check=False, preexec_fn=limit_output,
                )
            except (OSError, subprocess.TimeoutExpired):
                raise RuntimeError("exact archive transfer failed or timed out") from None
            errors.seek(0)
            error = errors.read(4097)
        if result.returncode == 0:
            if archive_path.stat().st_size != expected_size:
                raise RuntimeError("exact archive byte count differs from metadata")
            return
        archive_path.unlink()
        if result.returncode != 1 or not re.fullmatch(rb"gh: Not Found \(HTTP 404\)\r?\n?", error):
            raise RuntimeError("exact archive transfer returned an unexpected response")
        if attempt == 5:
            raise RuntimeError("exact archive remained unavailable after 6 attempts")
        print(f"Exact archive not yet available; retry {attempt + 1}/5", flush=True)
        sleep(10)


def load_plane(repo_root):
    spec = importlib.util.spec_from_file_location(
        "android_content_archive_plane", repo_root / "scripts/ai/android-content-plane.py"
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def verify_archive(plane, authority, archive_path, digest, expected_size, export_dir, run_id, run_attempt):
    """Only canonical regular members may be written, after the ZIP digest passes."""
    digest = digest.removeprefix("sha256:")
    if (not re.fullmatch(r"[0-9a-f]{64}", digest) or type(expected_size) is not int
            or not 0 < expected_size <= MAX_ARCHIVE_BYTES):
        raise RuntimeError("invalid expected archive digest or size")
    expected, _, _ = plane.build_artifact_members(authority, run_id, run_attempt)
    plane._require_safe_existing_directory(archive_path.parent, "archive parent")
    plane._require_safe_existing_directory(export_dir.parent, "extraction parent")
    fd = os.open(archive_path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as stream:
        metadata = os.fstat(stream.fileno())
        if (not stat.S_ISREG(metadata.st_mode) or metadata.st_nlink != 1
                or metadata.st_size != expected_size):
            raise RuntimeError("archive is not the expected regular ZIP")
        if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
            raise RuntimeError("archive SHA-256 differs from the exact upload digest")
        stream.seek(0)
        with zipfile.ZipFile(stream) as archive:
            members = archive.infolist()
            names = [member.filename for member in members]
            if len(names) != len(expected) or set(names) != set(expected):
                raise RuntimeError("archive member set differs from canonical content")
            for member in members:
                mode = member.external_attr >> 16
                if (member.orig_filename != member.filename or member.is_dir()
                        or member.flag_bits & 1 or member.create_system != 3
                        or not stat.S_ISREG(mode) or mode & 0o7777 != 0o644
                        or member.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
                        or member.file_size != len(expected[member.filename])):
                    raise RuntimeError("archive member type, mode, or expansion differs from canonical content")
            # All names and sizes above come from the canonical producer, never ZIP paths.
            writer = plane._DescriptorExport(export_dir)
            try:
                for name, value in expected.items():
                    member = archive.getinfo(name)
                    with archive.open(member) as source:
                        actual = source.read(len(value) + 1)
                    if actual != value:
                        raise RuntimeError("archive member bytes differ from canonical content")
                    writer.write(name, actual)
            finally:
                writer.close()
    return plane.verify_export(authority, export_dir, run_id, run_attempt)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--export-dir", type=Path, required=True)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--expected-archive-digest")
    parser.add_argument("--expected-archive-size", type=int)
    parser.add_argument("--expected-producer-commit")
    parser.add_argument("--run-id")
    parser.add_argument("--run-attempt")
    args = parser.parse_args()
    try:
        repo_root = args.repo_root.absolute()
        plane = load_plane(repo_root)
        if args.archive:
            if not all((args.expected_producer_commit, args.run_id, args.run_attempt,
                        args.expected_archive_digest, args.expected_archive_size)):
                raise RuntimeError("offline archive verification requires exact producer/run/digest/size")
            producer, run_id, run_attempt = args.expected_producer_commit, args.run_id, args.run_attempt
            digest, size = args.expected_archive_digest, args.expected_archive_size
        else:
            artifact = wait_for_artifact(os.environ)
            producer, run_id, run_attempt = (os.environ[key] for key in
                ("GITHUB_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"))
            digest, size = artifact["digest"], artifact.get("size_in_bytes")
        authority = plane.build_authority(repo_root, repo_root / "eng/android-content-plane.lock.json", producer)
        with tempfile.TemporaryDirectory(prefix="android-content-archive-") as temporary:
            archive = args.archive.absolute() if args.archive else Path(temporary) / "artifact.zip"
            if not args.archive:
                download_archive(os.environ, archive, size)
            receipt = verify_archive(plane, authority, archive, digest, size,
                                     args.export_dir.absolute(), run_id, run_attempt)
        print(json.dumps(receipt, sort_keys=True, indent=2))
        return 0
    except (RuntimeError, OSError, ValueError, KeyError, TypeError,
            subprocess.SubprocessError, zipfile.BadZipFile, EOFError):
        # Exception text may contain a signed URL or token. Only a fixed error is public.
        print("android-content-archive: exact archive verification failed", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
