"""Track outlines from OpenStreetMap, for the briefing and the backdrop.

    python tools/trackmaps.py discover   # list OSM circuit relations near each track
    python tools/trackmaps.py build      # trace the relations in trackmaps.sources.json

`build` writes src/LmuCareer.App/wwwroot/data/trackmaps.json: one SVG path per track layout,
traced from the OSM circuit relation (type=circuit) that maps it. Map data (c) OpenStreetMap
contributors, ODbL; the app credits it wherever an outline shows. Nothing comes from the game.
"""
import json
import math
import sys
import time
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCES = Path(__file__).with_name("trackmaps.sources.json")
OUT = ROOT / "src/LmuCareer.App/wwwroot/data/trackmaps.json"
API = "https://api.openstreetmap.org/api/0.6"
AGENT = "FactorySeat-trackmaps/0.1 (fan-made career mode, run by hand a few times a year)"

# Rough boxes around each circuit (west, south, east, north), for discovery only.
BOXES = {
    "LeMans_2023": (0.185, 47.925, 0.245, 47.965),
    "Spa_2023": (5.955, 50.425, 5.985, 50.448),
    "Monza_2023": (9.270, 45.608, 9.300, 45.635),
    "Sebring_2023": (-81.370, 27.443, -81.340, 27.462),
    "BahrainWEC_2023": (50.500, 26.022, 50.522, 26.040),
    "PortimaoWEC_2023": (-8.640, 37.225, -8.620, 37.238),
    "FujiWEC_2023": (138.915, 35.365, 138.940, 35.378),
    "ImolaWEC_2024": (11.700, 44.335, 11.725, 44.346),
    "CotAWEC_2024": (-97.645, 30.126, -97.628, 30.140),
    "Interlagos_2024": (-46.705, -23.707, -46.692, -23.697),
    "Qatar_2024": (51.445, 25.483, 51.460, 25.496),
    "Silverstone_2025": (-1.030, 52.060, -1.000, 52.080),
    "PaulRicard_2025": (5.780, 43.245, 5.800, 43.258),
    "Barcelona_2025": (2.250, 41.565, 2.265, 41.575),
    "Daytona_2026": (-81.080, 29.178, -81.060, 29.195),
    "LagunaSeca_2026": (-121.760, 36.580, -121.748, 36.590),
    "RoadAtlanta_2026": (-83.822, 34.140, -83.808, 34.153),
    "LongBeach_2026": (-118.200, 33.758, -118.183, 33.770),
}


def fetch(url: str) -> ET.Element:
    request = urllib.request.Request(url, headers={"User-Agent": AGENT})
    with urllib.request.urlopen(request, timeout=120) as response:
        data = response.read()
    time.sleep(1.5)  # be gentle with the OSM API
    return ET.fromstring(data)


def tags(element: ET.Element) -> dict:
    return {t.get("k"): t.get("v") for t in element.findall("tag")}


def discover() -> None:
    for folder, (w, s, e, n) in BOXES.items():
        try:
            osm = fetch(f"{API}/map?bbox={w},{s},{e},{n}")
        except Exception as error:  # noqa: BLE001 - report and carry on with the next track
            print(f"{folder}: ERROR {error}")
            continue
        circuits = [r for r in osm.findall("relation") if tags(r).get("type") == "circuit"]
        print(f"{folder}: {len(circuits)} circuit relation(s)")
        for r in circuits:
            t = tags(r)
            print(f"   {r.get('id')}  {t.get('name', '?')}  |  {t.get('name:en', '')}  |  ways={len(r.findall('member'))}")


def trace(source) -> list[tuple[float, float]]:
    """The circuit as one polyline of (lon, lat): a relation's ways in member order, or a list of ways."""
    if isinstance(source, list):
        nodes, ways = {}, {}
        for way_id in source:
            osm = fetch(f"{API}/way/{way_id}/full")
            nodes.update({n.get("id"): (float(n.get("lon")), float(n.get("lat"))) for n in osm.findall("node")})
            ways.update({w.get("id"): [nd.get("ref") for nd in w.findall("nd")] for w in osm.findall("way")})
        members = [str(w) for w in source]
    else:
        osm = fetch(f"{API}/relation/{source}/full")
        nodes = {n.get("id"): (float(n.get("lon")), float(n.get("lat"))) for n in osm.findall("node")}
        ways = {w.get("id"): [nd.get("ref") for nd in w.findall("nd")] for w in osm.findall("way")}
        relation = next(r for r in osm.findall("relation") if r.get("id") == str(source))
        # Pit lanes and the like are members with a role; the lap itself has none (or "forward").
        members = [m.get("ref") for m in relation.findall("member")
                   if m.get("type") == "way" and m.get("ref") in ways and m.get("role", "") in ("", "forward", "main", "outer")]
        pits = {w.get("id") for w in osm.findall("way") if is_pit(tags(w))}
        members = [m for m in members if m not in pits] or members

    line = walk(members, ways, nodes)
    return [nodes[n] for n in line if n in nodes]


def is_pit(t: dict) -> bool:
    # Not just "pit" in the name: Silverstone's main straight is the National Pit Straight.
    name = t.get("name", "").lower()
    return (any(p in name for p in ("pit lane", "pitlane", "pit entry", "pit exit", "pit in", "pit out"))
            or t.get("raceway") == "pit_lane" or t.get("service") in ("pit_lane", "repair"))


def walk(members: list[str], ways: dict, nodes: dict) -> list[str]:
    """
    Drives the lap: from the first way, at every junction carry on along the unused way that turns
    least, until the line comes back on itself. Member order and drawing direction don't matter, and
    branches (an old chicane, a short-course link) are left behind.
    """
    def heading(a: str, b: str) -> float:
        (x1, y1), (x2, y2) = nodes[a], nodes[b]
        return math.atan2(y2 - y1, (x2 - x1) * math.cos(math.radians(y1)))

    at_node: dict[str, list[tuple[str, int]]] = {}
    for ref in members:
        for i, n in enumerate(ways[ref]):
            at_node.setdefault(n, []).append((ref, i))

    line = list(ways[members[0]])
    used = {members[0]}
    while True:
        end = line[-1]
        if end in line[:-1]:
            # Back on itself: the lap is the loop from the first visit of this node.
            return line[line.index(end):]
        options = []
        for ref, i in at_node.get(end, []):
            if ref in used:
                continue
            way = ways[ref]
            if i < len(way) - 1:
                options.append((ref, way[i:]))
            if i > 0:
                options.append((ref, way[i::-1]))
        if not options:
            return line
        here = heading(line[-2], end)
        def turn(option):
            delta = heading(option[1][0], option[1][1]) - here
            return abs((delta + math.pi) % (2 * math.pi) - math.pi)
        ref, path = min(options, key=turn)
        used.add(ref)
        line += path[1:]


def simplify(points: list[tuple[float, float]], tolerance: float) -> list[tuple[float, float]]:
    """Ramer-Douglas-Peucker. A closed loop is split at its far side first, or it would collapse to a point."""
    if len(points) < 3:
        return points
    if math.dist(points[0], points[-1]) < 1e-9:
        far = max(range(len(points)), key=lambda i: math.dist(points[0], points[i]))
        return simplify(points[: far + 1], tolerance)[:-1] + simplify(points[far:], tolerance)
    (x1, y1), (x2, y2) = points[0], points[-1]
    length = math.hypot(x2 - x1, y2 - y1) or 1e-9
    far, index = 0.0, 0
    for i, (x, y) in enumerate(points[1:-1], 1):
        d = abs((y2 - y1) * x - (x2 - x1) * y + x2 * y1 - y2 * x1) / length
        if d > far:
            far, index = d, i
    if far <= tolerance:
        return [points[0], points[-1]]
    return simplify(points[: index + 1], tolerance)[:-1] + simplify(points[index:], tolerance)


def to_svg(points: list[tuple[float, float]], size: float = 1000) -> dict:
    """Projects lon/lat flat (fine at circuit scale) into a viewBox whose longer side is `size`."""
    lat0 = sum(p[1] for p in points) / len(points)
    k = math.cos(math.radians(lat0))
    xy = [(lon * k, -lat) for lon, lat in points]
    xs, ys = [p[0] for p in xy], [p[1] for p in xy]
    span = max(max(xs) - min(xs), max(ys) - min(ys))
    scaled = [((x - min(xs)) / span * size, (y - min(ys)) / span * size) for x, y in xy]
    scaled = simplify(scaled, size / 600)
    width, height = max(p[0] for p in scaled), max(p[1] for p in scaled)
    d = "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in scaled)
    closed = math.dist(scaled[0], scaled[-1]) < size / 50
    return {"viewBox": f"0 0 {width:.0f} {height:.0f}", "d": d + (" Z" if closed else "")}


def build() -> None:
    sources = json.loads(SOURCES.read_text(encoding="utf-8"))
    maps, traced = {}, {}
    for key, source in sources["layouts"].items():
        id_ = json.dumps(source)
        if id_ not in traced:
            print(f"tracing {key}: {id_}")
            traced[id_] = to_svg(trace(source))
        maps[key] = traced[id_]
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"attribution": "Map data © OpenStreetMap contributors", "layouts": maps}, indent=1), encoding="utf-8")
    print(f"wrote {len(maps)} layouts to {OUT}")


if __name__ == "__main__":
    {"discover": discover, "build": build}[sys.argv[1] if len(sys.argv) > 1 else "build"]()
