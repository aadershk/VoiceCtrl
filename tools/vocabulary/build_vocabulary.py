"""Builds the app's hidden vocabulary (ticket VED-62 / 36b).

Writes two gzip text files, one entry per line, into the Core project's Vocabulary folder:

  terms.txt.gz           names, places, brands, jargon; most useful first
  everyday-words.txt.gz  ordinary lower-case English words (a recogniser is assumed to have heard
                         these right, so the matcher never rewrites them)

Every source is CC0, public domain, a US government work, CC BY 4.0 or MIT-like; see
THIRD-PARTY-NOTICES.md. Raw downloads are cached under --cache so a rerun costs nothing. The held-out
accuracy scripts are never read here, and nothing is tuned against them.

    python tools/vocabulary/build_vocabulary.py --cache <folder>
"""
from __future__ import annotations

import argparse
import csv
import gzip
import io
import json
import math
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

UA = {"User-Agent": "VoiceCtrl-vocabulary-build/1.0 (https://github.com/aadershk/VoiceCtrl)"}
WDQS = "https://query.wikidata.org/sparql"
MAX_TERM_LENGTH = 60

# Wikidata's public endpoint is slow and rate-limited, so fetching stops at this time and the
# build uses whatever bands are cached; a later run resumes where this one stopped.
DEADLINE = float("inf")

# (Wikidata class, label, lowest sitelink count worth keeping). A sitelink count is how many
# Wikipedias have an article: the cheapest popularity signal there is, and it needs no licence.
WIKIDATA_CLASSES: list[tuple[str, str, int]] = [
    ("Q4830453", "business", 3), ("Q431289", "brand", 2), ("Q7397", "software", 3),
    ("Q9143", "programming language", 1), ("Q46970", "airline", 2), ("Q3918", "university", 5),
    ("Q6256", "country", 1), ("Q12140", "medication", 2), ("Q12136", "disease", 6),
    ("Q746549", "dish", 4), ("Q476028", "football club", 6), ("Q215380", "musical group", 8),
    ("Q11424", "film", 16), ("Q7889", "video game", 12), ("Q35127", "website", 6),
    ("Q891723", "public company", 3), ("Q9135", "operating system", 1), ("Q188860", "software library", 2),
    ("Q8366", "algorithm", 3), ("Q132364", "communication protocol", 3), ("Q235557", "file format", 3),
    ("Q35657", "US state", 1), ("Q10864048", "first-level division", 12), ("Q8502", "mountain", 8),
    ("Q4022", "river", 9), ("Q23397", "lake", 9), ("Q23442", "island", 9), ("Q570116", "tourist attraction", 10),
    ("Q33506", "museum", 6), ("Q16917", "hospital", 4), ("Q31855", "research institute", 5),
    ("Q12973014", "sports team", 5), ("Q483110", "stadium", 4), ("Q482994", "album", 14),
    ("Q5398426", "television series", 10), ("Q8261", "novel", 14), ("Q11032", "newspaper", 6),
    ("Q41298", "magazine", 6), ("Q7278", "political party", 6), ("Q484652", "international organisation", 4),
    ("Q79913", "non-governmental organisation", 5), ("Q327333", "government agency", 5),
    ("Q2095", "food", 5), ("Q40050", "drink", 5), ("Q169872", "symptom", 3), ("Q796194", "medical procedure", 4),
    ("Q4936952", "anatomical structure", 8), ("Q11344", "chemical element", 1), ("Q7946", "mineral", 5),
    ("Q46169", "national park", 5), ("Q23413", "castle", 9), ("Q12280", "bridge", 9), ("Q11303", "skyscraper", 7),
    ("Q9259", "world heritage site", 1), ("Q34770", "language", 6), ("Q132241", "festival", 6), ("Q8142", "currency", 6),
    ("Q6881511", "enterprise", 5), ("Q167270", "trademark", 3), ("Q620615", "mobile app", 2),
    ("Q101352", "family name", 5), ("Q11879590", "female given name", 1), ("Q12308941", "male given name", 1),
    ("Q3409032", "unisex given name", 1), ("Q202444", "given name", 1),
]


def http(url: str, tries: int = 5) -> bytes:
    for attempt in range(tries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=75) as response:
                return response.read()
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError) as error:
            wait = 8 * (attempt + 1)
            print(f"  retry {attempt + 1} after {error}; waiting {wait}s", file=sys.stderr)
            time.sleep(wait)
    raise RuntimeError(f"gave up on {url}")


def cached(cache: Path, name: str, url: str) -> Path:
    path = cache / name
    if not path.exists():
        print(f"downloading {name}")
        path.write_bytes(http(url))
    return path


def wikidata_rows(cache: Path, qid: str, lo: int, hi: int = 1_000_000, depth: int = 0) -> list[tuple[str, int]]:
    """Labels and sitelink counts of a class in the sitelink band [lo, hi), bisecting the band
    (geometrically, since counts are heavy-tailed) when the endpoint times out or fills its page.
    A band that keeps failing at depth 6 is given up on: the list is best-effort by design."""
    key = cache / "wikidata" / f"{qid}_{lo}_{hi}.json"
    if key.exists():
        return [tuple(row) for row in json.loads(key.read_text("utf-8"))]
    if time.time() > DEADLINE:
        return []
    limit = 6000
    query = (
        f'SELECT ?l ?s WHERE {{ ?i wdt:P31 wd:{qid}; wikibase:sitelinks ?s; rdfs:label ?l . '
        f'FILTER(?s>={lo} && ?s<{hi} && LANG(?l)="en") }} LIMIT {limit}'
    )
    url = WDQS + "?" + urllib.parse.urlencode({"query": query, "format": "json"})
    rows: list[tuple[str, int]] | None = None
    try:
        data = json.loads(http(url, tries=2))
        rows = [(b["l"]["value"], int(b["s"]["value"])) for b in data["results"]["bindings"]]
    except Exception as error:  # truncated JSON, 5xx, timeout: all mean "too big or unlucky"
        print(f"  {qid} [{lo},{hi}) failed: {str(error)[:60]}", flush=True)
    if rows is None or len(rows) >= limit - 1:
        middle = int((lo * hi) ** 0.5) if lo > 0 else hi // 2
        if depth >= 6 or middle <= lo or middle >= hi:
            return rows or []
        rows = wikidata_rows(cache, qid, middle, hi, depth + 1) + wikidata_rows(cache, qid, lo, middle, depth + 1)
    if time.time() > DEADLINE:
        return rows  # children may have been cut short; caching this band would make the gap permanent
    key.parent.mkdir(parents=True, exist_ok=True)
    key.write_text(json.dumps(rows), "utf-8")
    time.sleep(3)
    return rows


NAME_OK = re.compile(r"^[^\W\d_][^\W\d_'’\- .]*(?:[ \-.'’][^\W\d_][^\W\d_'’\- .]*){0,3}\.?$", re.UNICODE)


def clean_term(raw: str) -> str | None:
    term = unicodedata.normalize("NFC", raw).replace("’", "'").strip()
    term = re.sub(r"\s+", " ", term)
    if term.endswith("."):
        term = term[:-1]
    if len(term) < 3 or len(term) > MAX_TERM_LENGTH or "�" in term:
        return None
    if re.search(r"[\d_/\\(),:;&+@#%*=<>\[\]{}|~^\"!?]", term):
        return None
    if not NAME_OK.match(term) or len(term.split()) > 4:
        return None
    return term


def title_case_surname(upper: str) -> str:
    word = upper.title()
    word = re.sub(r"^(Mc)([a-z])", lambda m: m.group(1) + m.group(2).upper(), word)
    word = re.sub(r"^(O')([a-z])", lambda m: m.group(1) + m.group(2).upper(), word)
    return word


def census_surnames(cache: Path) -> list[tuple[str, float]]:
    path = cached(cache, "census-names.zip", "https://www2.census.gov/topics/genealogy/2010surnames/names.zip")
    with zipfile.ZipFile(path) as archive, archive.open("Names_2010Census.csv") as handle:
        reader = csv.DictReader(io.TextIOWrapper(handle, "utf-8"))
        rows = [(title_case_surname(r["name"]), int(r["count"])) for r in reader if r["count"].isdigit() and r["name"] != "ALL OTHER NAMES"]
    return rows[:60000]


def geonames_places(cache: Path) -> list[tuple[str, float]]:
    path = cached(cache, "cities5000.zip", "https://download.geonames.org/export/dump/cities5000.zip")
    out: list[tuple[str, float]] = []
    with zipfile.ZipFile(path) as archive, archive.open("cities5000.txt") as handle:
        for line in io.TextIOWrapper(handle, "utf-8"):
            cells = line.rstrip("\n").split("\t")
            population = float(cells[14] or 0)
            for name in {cells[1], cells[2]}:
                out.append((name, population))
    countries = cached(cache, "countryInfo.txt", "https://download.geonames.org/export/dump/countryInfo.txt")
    for line in countries.read_text("utf-8").splitlines():
        if line and not line.startswith("#"):
            cells = line.split("\t")
            out.append((cells[4], 1e9))
            out.append((cells[5], 1e7))
    return out


AIRPORT_NOISE = re.compile(r"\b(international|regional|municipal|airport|airfield|aerodrome|field|county|memorial)\b", re.I)


def airports(cache: Path) -> list[tuple[str, float]]:
    path = cached(cache, "airports.csv", "https://raw.githubusercontent.com/davidmegginson/ourairports-data/main/airports.csv")
    out: list[tuple[str, float]] = []
    with path.open(encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            if row["type"] not in ("large_airport", "medium_airport"):
                continue
            weight = 5e6 if row["type"] == "large_airport" else 1e5
            out.append((row["name"], weight))
            core = re.sub(r"\s+", " ", AIRPORT_NOISE.sub("", row["name"])).strip()
            out.append((core, weight))
    return out


def curated_terms() -> list[tuple[str, float]]:
    """Hand-written lists in tools/vocabulary/curated (one term per line, most useful first).
    They are the author's own knowledge, released as CC0 with the rest of the repository's data
    files, and rank ahead of the scraped sources because every line was chosen on purpose."""
    out: list[tuple[str, float]] = []
    for path in sorted((Path(__file__).parent / "curated").glob("*.txt")):
        lines = [line.strip() for line in path.read_text("utf-8").splitlines() if line.strip()]
        out.extend((line, float(len(lines) - index)) for index, line in enumerate(lines))
    return out


def words_of(terms: list[str]) -> set[str]:
    return {
        unicodedata.normalize("NFKD", w).encode("ascii", "ignore").decode().lower()
        for t in terms for w in re.findall(r"[^\W\d_]+", t)
    }


def everyday_words(cache: Path) -> list[str]:
    """Words of the SCOWL size-70 US list (names and place names included, with their capitals), with its prefix and suffix rules expanded."""
    release = json.loads(http("https://api.github.com/repos/en-wl/wordlist/releases"))[0]
    asset = next(a for a in release["assets"] if a["name"].startswith("hunspell-en_US-large"))
    path = cached(cache, "hunspell-en_US-large.zip", asset["browser_download_url"])
    with zipfile.ZipFile(path) as archive:
        dic = archive.read("en_US-large.dic").decode("utf-8").splitlines()[1:]
        aff = archive.read("en_US-large.aff").decode("utf-8").splitlines()
    rules: dict[tuple[str, str], list[tuple[str, str, re.Pattern[str]]]] = {}
    cross: dict[tuple[str, str], bool] = {}
    for line in aff:
        parts = line.split()
        if len(parts) >= 4 and parts[0] in ("PFX", "SFX"):
            kind, flag = parts[0], parts[1]
            if parts[3].isdigit():
                cross[(kind, flag)] = parts[2] == "Y"
                continue
            strip = "" if parts[2] == "0" else parts[2]
            add = "" if parts[3] == "0" else parts[3].split("/")[0]
            condition = parts[4] if len(parts) > 4 else "."
            pattern = re.compile(("^" + condition) if kind == "PFX" else (condition + "$"))
            rules.setdefault((kind, flag), []).append((strip, add, pattern))
    words: set[str] = set()
    for entry in dic:
        word, _, flags = entry.partition("/")
        if not word:
            continue
        forms = {word}
        suffixed = set()
        for flag in flags:
            for strip, add, pattern in rules.get(("SFX", flag), []):
                if pattern.search(word) and (not strip or word.endswith(strip)):
                    suffixed.add(word[: len(word) - len(strip)] + add)
        forms |= suffixed
        for flag in flags:
            for strip, add, pattern in rules.get(("PFX", flag), []):
                if pattern.search(word) and (not strip or word.startswith(strip)):
                    for base in [word, *(suffixed if cross.get(("PFX", flag)) else [])]:
                        forms.add(add + base[len(strip):])
        # Capitalised entries (Thursday, Amsterdam) stay capitalised: they are known words, but only
        # the lower-case ones count as ordinary English.
        words |= {form for form in forms if form.isalpha()}
    return sorted(words)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cache", required=True, type=Path)
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parents[2] / "src" / "VoiceCtrl.Core" / "Vocabulary")
    parser.add_argument("--wikidata-minutes", type=float, default=0, help="time allowed for new Wikidata fetches (0: cache only)")
    parser.add_argument("--exclude-scripts", type=Path, nargs="*", default=[], help="scripts.json files whose terms (and every word of them) are dropped: the leak-free evaluation build")
    arguments = parser.parse_args()
    global DEADLINE
    DEADLINE = time.time() + arguments.wikidata_minutes * 60
    cache: Path = arguments.cache
    cache.mkdir(parents=True, exist_ok=True)

    # Each source contributes (term, weight); weight is turned into a percentile inside its own
    # source, so a source with millions of rows does not push every other source down the list.
    sources: dict[str, list[tuple[str, float]]] = {
        "census": census_surnames(cache),
        "geonames": geonames_places(cache),
        "airports": airports(cache),
        "curated": curated_terms(),
    }
    seen_class: set[tuple[str, int]] = set()
    for qid, label, minimum in WIKIDATA_CLASSES:
        if (qid, minimum) in seen_class:
            continue
        seen_class.add((qid, minimum))
        rows = wikidata_rows(cache, qid, minimum)
        print(f"wikidata {label}: {len(rows)}")
        sources.setdefault("wikidata " + label, []).extend((name, float(sitelinks)) for name, sitelinks in rows)

    everyday = everyday_words(cache)
    everyday_lower = {word for word in everyday if word[0].islower()}

    scored: dict[str, tuple[float, str]] = {}  # lower-case term -> (best percentile, display form)
    for source, rows in sources.items():
        cleaned = [(term, weight) for raw, weight in rows if (term := clean_term(raw))]
        cleaned.sort(key=lambda row: -row[1])
        total = max(len(cleaned), 1)
        for rank, (term, _) in enumerate(cleaned):
            percentile = rank / total * (0.25 if source == 'curated' else 1.0)
            lower = term.lower()
            # An all-lower-case term that is an ordinary word is not a name.
            if term == lower and lower in everyday_lower:
                continue
            current = scored.get(lower)
            if current is None or percentile < current[0] or (percentile == current[0] and term != lower):
                keep = current[1] if current and current[1] != lower and term == lower else term
                scored[lower] = (percentile, keep)

    if arguments.exclude_scripts:
        excluded = words_of([t for f in arguments.exclude_scripts for script in json.loads(f.read_text("utf-8")) for t in script["terms"]])
        before = len(scored)
        scored = {k: v for k, v in scored.items() if not (words_of([v[1]]) & excluded)}
        print(f"leak-free: dropped {before - len(scored)} terms that share a word with the dev or held-out terms")

    ordered = [display for _, display in sorted(scored.values(), key=lambda value: (round(value[0], 4), value[1]))]
    arguments.out.mkdir(parents=True, exist_ok=True)
    with gzip.open(arguments.out / "terms.txt.gz", "wt", encoding="utf-8", newline="\n", compresslevel=9) as handle:
        handle.write("\n".join(ordered) + "\n")
    with gzip.open(arguments.out / "everyday-words.txt.gz", "wt", encoding="utf-8", newline="\n", compresslevel=9) as handle:
        handle.write("\n".join(everyday) + "\n")
    print(f"{len(ordered)} terms, {len(everyday)} everyday words")


if __name__ == "__main__":
    main()
