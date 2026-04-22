# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/11

Findings:
- [high] scripts/ai/verify_design_mirror.py [correctness] mirror-verifier-misses-extra-files
`collect_stale_paths(...)` in `scripts/ai/verify_design_mirror.py` only iterates manifest-backed source files plus the repo/review mirror targets; it never compares the current `.codex-design/product` tree against the expected mirrored file set.; `scripts/ai/sync_design_mirror.py` does prune unexpected files via `prune_product_root(...)`, which means the sync path knows extra files are part of drift, but the verify path does not fail when those extra files remain.; The only added regression in `Chummer.CoreEngine.Tests/Program.cs` (`DesignMirrorVerificationStaysClosedWhenMirrorIsCurrent`) asserts the already-clean green path; there is no test that leaves an extra stale file behind after a canonical removal and proves `verify_design_mirror.py` fails closed.; Result: if the design repo removes or renames a mirrored file, this repo can keep the old file, `QUEUE.generated.yaml` can stay empty, and `python3 scripts/ai/verify_design_mirror.py` still exits 0 even though the local mirror bundle is stale.
Expected fix: Make `verify_design_mirror.py` validate the full expected mirror tree, including unexpected leftover files under `.codex-design/product`, and add a regression test that simulates a canon-removed file so the verifier and queue guard fail closed.
