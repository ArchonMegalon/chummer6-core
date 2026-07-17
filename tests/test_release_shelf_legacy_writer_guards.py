from __future__ import annotations

import os
import subprocess
from pathlib import Path

import pytest


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPTS_DIR = REPO_ROOT / "scripts"
FILESYSTEM_WRITERS = tuple(
    name
    for name in (
        "publish-download-bundle.sh",
        "publish-latest-nightly-to-downloads.sh",
    )
    if (SCRIPTS_DIR / name).is_file()
)


def run_writer(
    script_name: str,
    *arguments: Path | str,
    environment: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    child_environment = os.environ.copy()
    child_environment.update(environment or {})
    return subprocess.run(
        ["bash", str(SCRIPTS_DIR / script_name), *(str(value) for value in arguments)],
        cwd=REPO_ROOT,
        env=child_environment,
        capture_output=True,
        text=True,
        timeout=15,
        check=False,
    )


@pytest.mark.parametrize("script_name", FILESYSTEM_WRITERS)
@pytest.mark.parametrize("sentinel_name", (".release-shelf-layout-v1", "current.json"))
def test_filesystem_legacy_writer_refuses_layout_v1_before_mutation(
    tmp_path: Path,
    script_name: str,
    sentinel_name: str,
) -> None:
    bundle_root = tmp_path / "missing-bundle"
    downloads_root = tmp_path / "downloads"
    downloads_root.mkdir()
    sentinel = downloads_root / sentinel_name
    sentinel.write_text("{}\n", encoding="utf-8")

    arguments = (
        (downloads_root,)
        if script_name == "publish-latest-nightly-to-downloads.sh"
        else (bundle_root, downloads_root)
    )
    result = run_writer(script_name, *arguments)

    assert result.returncode != 0
    assert "immutable release shelf layout v1 is active" in result.stderr
    assert "current.json" in result.stderr
    assert sentinel.read_text(encoding="utf-8") == "{}\n"
    assert not (downloads_root / "files").exists()


def test_object_storage_legacy_writer_is_disabled_before_aws(
    tmp_path: Path,
) -> None:
    fake_bin = tmp_path / "bin"
    fake_bin.mkdir()
    aws_receipt = tmp_path / "aws-invoked"
    fake_aws = fake_bin / "aws"
    fake_aws.write_text(
        "#!/usr/bin/env bash\n"
        f"touch {str(aws_receipt)!r}\n"
        "exit 0\n",
        encoding="utf-8",
    )
    fake_aws.chmod(0o755)

    result = run_writer(
        "publish-download-bundle-s3.sh",
        environment={
            "CHUMMER_PORTAL_DOWNLOADS_S3_URI": "s3://unit-test/downloads",
            "PATH": f"{fake_bin}:{os.environ.get('PATH', '')}",
        },
    )

    assert result.returncode == 78
    assert ".release-shelf-layout-v1" in result.stderr
    assert "current.json" in result.stderr
    assert not aws_receipt.exists()
