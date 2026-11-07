"""Check a portable ZIP, including the shipped defaults and absence of user data."""
import hashlib
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parent.parent
version = ET.parse(repo / 'windows/TVBox.Windows/TVBox.Windows.csproj').findtext('.//Version')
package = Path(sys.argv[1]) if len(sys.argv) > 1 else repo / f"artifacts/TVBox-x64-{version}.zip"
forbidden = {"prefs.json", "configs.json", "history.json", "keep.json", "favorites.json", "app.log", "cookies.json", "credentials.json", "tokens.json"}
with zipfile.ZipFile(package) as archive:
    assert archive.testzip() is None, "Corrupt ZIP member"
    names = archive.namelist()
    roots = {name.replace("\\", "/").split("/")[0] for name in names}
    assert len(roots) == 1, "Expected one portable directory"
    prefix = roots.pop() + "/"
    assert not any(Path(name).name.lower() in forbidden or '.credential' in Path(name).name.lower() or Path(name).name.lower().startswith('metadata-v4') for name in names), "User data leaked into package"
    assert len([n for n in names if n.endswith("/TVBox.exe")]) == 1, "Duplicate app entry"
    for required in ["TVBox.exe", "libs/TVBox.dll", "libs/e_sqlite3.dll", "libs/Microsoft.Data.Sqlite.dll", "libs/Microsoft.ui.xaml.dll", "libs/Microsoft.UI.Xaml.Phone.dll", "libs/Microsoft.UI.pri", "libs/Microsoft.UI.Xaml.Controls.pri", "libs/zh-CN/Microsoft.ui.xaml.dll.mui", "assets/icons/douban.png", "assets/icons/netflix.svg", "assets/icons/netflix-wordmark.svg", "assets/catalog/default.json", "LICENSE", "README.md", "docs/ACCEPTANCE.md", "docs/UPSTREAM.md", "THIRD-PARTY-NOTICES.md", "ffmpeg/avcodec-62.dll"]:
        assert prefix + required in names, f"Missing {required}"
    import json
    provider_root = repo / "windows/TVBox.Windows/Assets/providers"
    for provider in ["4khdhub.cjs", "anizone.cjs", "vixsrc.cjs", "run-provider.mjs", "provenance.json", "LICENSE"]:
        assert archive.read(prefix + "assets/providers/" + provider) == (provider_root / provider).read_bytes(), "Stale provider: " + provider
    for provider in json.loads((provider_root / "provenance.json").read_text()):
        script = provider_root / (provider["id"][6:] + ".cjs")
        assert hashlib.sha256(script.read_bytes()).hexdigest() == provider["sha256"], "Provider provenance hash mismatch"
    default = archive.read(prefix + "assets/catalog/default.json")
    assert default == (repo / "windows/TVBox.Windows/Assets/catalog/default.json").read_bytes(), "Stale defaults"
    for document in ["README.md", "docs/ACCEPTANCE.md", "docs/UPSTREAM.md", "docs/PUBLIC-RELEASE.md", "THIRD-PARTY-NOTICES.md"]:
        assert archive.read(prefix + document) == (repo / document).read_bytes(), f"Stale document: {document}"
    for document in [f"docs/reports/TVBox-PC-v{version}-Update-Report.md", "docs/licenses/Microsoft.Data.Sqlite-MIT.txt", "docs/licenses/SQLitePCLRaw-Apache-2.0.txt"]:
        assert archive.read(prefix + document) == (repo / document).read_bytes(), f"Missing or stale release evidence: {document}"
    for document in list((repo / 'docs').glob('ACCEPTANCE-*.md')) + list((repo / 'docs/reports').glob('*.md')):
        relative = document.relative_to(repo).as_posix()
        assert archive.read(prefix + relative) == document.read_bytes(), f"Missing or stale linked document: {relative}"
    resource_plan = repo / 'docs/development/TVBox-PC-v0.9.0-HD-Resource-Integration-Plan.md'
    assert archive.read(prefix + resource_plan.relative_to(repo).as_posix()) == resource_plan.read_bytes(), 'Missing or stale HD resource design linked by README and update report'
    for document in (repo / 'docs/reports/images').glob('*'):
        if document.is_file():
            relative = document.relative_to(repo).as_posix()
            assert archive.read(prefix + relative) == document.read_bytes(), f"Missing or stale screenshot: {relative}"
    assert not any('/locales/winui/' in n for n in names), "Windows App SDK 2.x WinUI resources must remain beside their modules in libs"
    compiled = repo / "windows/TVBox.Windows/bin/x64/Release/net9.0-windows10.0.19041.0/win-x64/TVBox.dll"
    assert archive.read(prefix + "libs/TVBox.dll") == compiled.read_bytes(), "Stale application assembly"
    assert len(archive.read(prefix + "ffmpeg/avcodec-62.dll")) > 1_000_000, "Git LFS binary not materialized"
print("PASS: ZIP integrity, bundled defaults, current assembly, unique entry, native codecs, no user data")
print("SHA256:", hashlib.sha256(package.read_bytes()).hexdigest())
