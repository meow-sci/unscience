"""Validate a freshly built distribution before release packaging."""
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
repo = Path(__file__).resolve().parent.parent
package = root / "unscience"
assert sorted(p.name for p in root.iterdir()) == ["unscience"], "Expected only unscience/ in dist"
assert list(root.rglob("mod.toml")) == [package / "mod.toml"], "Expected one StarMap manifest"
assert 'EntryAssembly = "MeowSci.Unscience"' in (package / "mod.toml").read_text()
assert 'assets = [ "WingWalker.xml", "BeverageCooler.xml" ]' in (package / "mod.toml").read_text()
for name in ("MeowSci.Unscience.dll", "MeowSci.Unscience.deps.json", "Tomlyn.dll", "LICENSE", "WingWalker.xml", "BeverageCooler.xml"):
    assert (package / name).is_file(), f"Missing runtime file: {name}"
for folder, xml_name in (("wing-walker", "WingWalker.xml"), ("beverage-cooler", "BeverageCooler.xml")):
    assert (package / xml_name).read_bytes() == (repo / folder / xml_name).read_bytes(), f"{xml_name} differs from source"
    for asset in ET.parse(repo / folder / xml_name).getroot():
        if "Path" in asset.attrib:
            relative = asset.attrib["Path"]
            assert (package / relative).is_file(), f"Missing {folder} art: {relative}"
            assert (package / relative).read_bytes() == (repo / folder / relative).read_bytes(), f"Stale {folder} art: {relative}"
assert not (package / "Art").exists(), "Editable Blender art must not be distributed"
subprocess.run([sys.executable, str(repo / "scripts/check-cooler-assets.py"), str(package)], check=True)
subprocess.run([sys.executable, str(repo / "scripts/check-chair-assets.py"), str(package)], check=True)
expected = set()
pending = [repo / "unscience/unscience.csproj"]
visited = set()
while pending:
    path = pending.pop().resolve()
    if path in visited:
        continue
    visited.add(path)
    project = ET.parse(path).getroot()
    assembly = project.findtext(".//AssemblyName") or path.stem
    expected.add(f"{assembly}.dll")
    pending.extend(path.parent / reference.attrib["Include"].replace("\\", "/")
                   for reference in project.findall(".//ProjectReference"))
actual = {p.name for p in package.glob("MeowSci.*.dll")}
assert actual == expected, f"Feature assemblies: missing {expected - actual}; unexpected {actual - expected}"
assert not list(package.glob("KSA.dll")), "Do not distribute proprietary game assemblies"
print(f"PASS: one Unscience mod with {len(expected) - 1} explicitly referenced feature assemblies")
