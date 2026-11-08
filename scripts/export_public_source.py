"""Export tracked source and legal notices; developer records stay in the local repository."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess

root = Path(__file__).resolve().parent.parent
target = (root / "artifacts/public-source").resolve()
assert target.is_relative_to((root / "artifacts").resolve())
def git(*args, cwd=root):
    return subprocess.check_output(["git", *args], cwd=cwd)
assert not git("status", "--porcelain").strip(), "Commit reviewed local source before exporting"
assert not git("status", "--porcelain", cwd=target).strip(), "Public checkout has unreviewed edits"
assert git("remote", "get-url", "origin", cwd=target).decode().strip() == "https://github.com/cbdoglolz/tvbox-pc-public.git"
def public(name):
    return name not in {"AGENTS.md", "CHANGELOG.md"} and (not name.startswith("docs/") or name == "docs/UPSTREAM.md" or name.startswith("docs/licenses/"))
paths = [p for p in git("ls-files", "-z").decode().split("\0") if p and public(p)]
old = [p for p in git("ls-files", "-z", cwd=target).decode().split("\0") if p]
removed = sorted(set(old) - set(paths))
for name in removed:
    destination = (target / name).resolve()
    assert destination.is_relative_to(target)
    destination.unlink()
for name in paths:
    destination = (target / name).resolve()
    assert destination.is_relative_to(target)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(root / name, destination)
    assert hashlib.sha256(destination.read_bytes()).digest() == hashlib.sha256((root / name).read_bytes()).digest()
record = {"files": len(paths), "removedDeveloperFiles": removed, "sourceRevision": git("rev-parse", "HEAD").decode().strip()}
(root / ".tools/public-export-v088.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
print(f"Exported {len(paths)} source files; removed {len(removed)} developer records from current public tree")
