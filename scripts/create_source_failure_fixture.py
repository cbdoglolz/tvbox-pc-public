"""Create an isolated portable copy with a deliberately unavailable first source."""
import json
import shutil
import argparse
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--all-unavailable", action="store_true", help="Make every source unavailable in the isolated copy")
parser.add_argument("--decoder-failure", action="store_true", help="Use the loopback media fixture to test decoder-triggered fallback")
args = parser.parse_args()
version = ET.parse(repo / 'windows/TVBox.Windows/TVBox.Windows.csproj').findtext('.//Version')
source = repo / f"artifacts/TVBox-x64-{version}"
target = repo / f"artifacts/failure-client-{version}"
assert target.parent.resolve() == (repo / "artifacts").resolve()
if not target.exists():
    shutil.copytree(source, target)
elif not args.all_unavailable:
    raise RuntimeError("Inspect an existing fixture before reusing it")
asset = target / "assets/catalog/default.json"
catalog = json.loads(asset.read_text(encoding="utf-8"))
for site in catalog["sites"] if args.all_unavailable else catalog["sites"][:1]:
    site.update(api="http://127.0.0.1:18890/api" if args.decoder_failure else "http://127.0.0.1:1/unavailable", timeout=2)
    if args.decoder_failure: site['name'] = '解码失败测试源'
asset.write_text(json.dumps(catalog, ensure_ascii=False, indent=2), encoding="utf-8")
print("Created isolated bad-source fixture:", target)
