"""Read-only live source verification; never executes provider scripts."""
import concurrent.futures
import argparse
import json
import time
import urllib.parse
import urllib.request
from pathlib import Path

SOURCES = [
    ("ruyi", "如意资源", "https://cj.rycjapi.com/api.php/provide/vod"),
    ("bfzy", "暴风资源", "https://bfzyapi.com/api.php/provide/vod"),
    ("ffzy", "非凡资源", "https://api.ffzyapi.com/api.php/provide/vod"),
    ("zy360", "360资源", "https://360zyzz.com/api.php/provide/vod"),
    ("mdzy", "魔都资源", "https://www.mdzyapi.com/api.php/provide/vod"),
    ("ikun", "iKun资源", "https://ikunzyapi.com/api.php/provide/vod"),
]

def get(url, limit=2_000_000):
    url = urllib.parse.quote(url, safe=":/?&=%+#@!$'()*;,[]~")
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(request, timeout=12) as response:
        return response.read(limit), response.url

def probe(source):
    key, name, api = source
    started = time.monotonic()
    report = {"key": key, "name": name, "api": api}
    try:
        home = json.loads(get(api)[0].decode("utf-8-sig"))
        report["categories"] = len(home.get("class", []))
        # Check both a feature film and an episodic series.
        report["samples"] = []
        # The providers have different catalogues: Modu carries the first season.
        series_keyword = "庆余年" if key.endswith("mdzy") or key == "mdzy" else "庆余年第二季"
        for keyword in ["星际穿越", series_keyword]:
            search = json.loads(get(api + "?" + urllib.parse.urlencode({"ac": "detail", "wd": keyword}))[0])
            videos = search.get("list", [])
            sample = {"keyword": keyword, "results": len(videos)}
            if videos:
                video = next((v for v in videos if v["vod_name"] == keyword), videos[0])
                if not video.get("vod_play_url"):
                    video = json.loads(get(api + "?ac=detail&ids=" + str(video["vod_id"]))[0])["list"][0]
                sample.update(id=str(video["vod_id"]), name=video["vod_name"], play_from=video.get("vod_play_from"))
                episodes = video.get("vod_play_url", "").split("$$$")
                playlist = next((entry.split("$", 1)[1] for line in episodes for entry in line.split("#") if "$" in entry and ".m3u8" in entry), None)
                sample["episodes"] = max((len(line.split("#")) for line in episodes), default=0)
                if playlist:
                    sample["media"] = playlist
                    text, final = get(playlist)
                    for _ in range(3):
                        lines = text.decode("utf-8-sig").splitlines()
                        if not any(line.startswith("#EXTM3U") for line in lines):
                            raise ValueError("media is not an HLS playlist")
                        next_uri = next((line.strip() for line in lines if line.strip() and not line.startswith("#")), None)
                        if not next_uri:
                            raise ValueError("HLS contains no segment/variant")
                        resolved = urllib.parse.urljoin(final, next_uri)
                        if "#EXT-X-STREAM-INF" not in text.decode("utf-8-sig"):
                            data, _ = get(resolved, 4096)
                            if not data or data.lstrip().startswith((b"<!DOCTYPE", b"<html")):
                                raise ValueError("media segment is empty or HTML")
                            sample["segment_bytes"] = len(data)
                            break
                        text, final = get(resolved)
            report["samples"].append(sample)
        report["ok"] = (report["categories"] > 0 and
                        all(s.get("segment_bytes", 0) > 0 and s.get("name") == s["keyword"] for s in report["samples"]) and
                        report["samples"][1].get("episodes", 0) > 1)
    except Exception as error:
        report.update(ok=False, error=str(error))
    report["elapsed_ms"] = round((time.monotonic() - started) * 1000)
    return report

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidates", action="store_true", help="Inspect all candidates instead of gating all bundled sources")
    parser.add_argument("--require-all", action="store_true", help="Require every configured source to pass both title fixtures")
    args = parser.parse_args()
    if not args.candidates:
        asset = Path(__file__).resolve().parent.parent / "windows/TVBox.Windows/Assets/catalog/default.json"
        SOURCES = [(s["key"], s["name"], s["api"]) for s in json.loads(asset.read_text(encoding="utf-8"))["sites"]]
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as executor:
        reports = list(executor.map(probe, SOURCES))
    output = Path(__file__).resolve().parent.parent / "artifacts" / "source-probe.json"
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    for report in reports:
        print(json.dumps(report, ensure_ascii=True))
    passed = all(r["ok"] for r in reports) if args.require_all else sum(bool(r["ok"]) for r in reports) >= (1 if args.candidates else 2)
    print(f"Media fixture checks: {sum(bool(r['ok']) for r in reports)}/{len(reports)} passed; unavailable and missing-title sources remain explicitly reported.")
    raise SystemExit(0 if passed else 1)
