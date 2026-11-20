#!/usr/bin/env python3
"""Bundle restored package metadata and available license texts with the Linux app."""
import json
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
output = Path(sys.argv[1]).resolve() / "licenses"
output.mkdir(parents=True, exist_ok=True)
assets = json.loads((root / "linux/Nashi.Linux/obj/project.assets.json").read_text())
folders = [Path(folder) for folder in assets["packageFolders"]]
inventory = []
for identity, package in sorted(assets["libraries"].items()):
    if package["type"] != "package":
        continue
    directory = next((folder / package["path"] for folder in folders if (folder / package["path"]).is_dir()), None)
    if directory is None:
        raise SystemExit(f"Restored package missing: {identity}")
    target = output / directory.parent.name / directory.name
    target.mkdir(parents=True, exist_ok=True)
    spec = next(directory.glob("*.nuspec"))
    shutil.copy2(spec, target / spec.name)
    metadata = ET.parse(spec).getroot()
    license_text = next((node.text for node in metadata.iter() if node.tag.rsplit("}", 1)[-1] == "license"), "See upstream notices")
    for source in directory.iterdir():
        if source.is_file() and any(word in source.name.lower() for word in ("license", "licence", "notice", "copying")):
            shutil.copy2(source, target / source.name)
    inventory.append({"package": identity, "license": license_text, "sha512": package.get("sha512")})

for name in ("PDFsharp-MIT.txt", "BouncyCastle-MIT.txt", "Magick.NET-THIRD-PARTY-NOTICES.txt", "ZXing-Apache-2.0.txt", "SharpCompress-MIT.txt", "PdfPig-Apache-2.0.txt", "dotnet-MIT.txt", "dotnet-THIRD-PARTY-NOTICES.txt"):
    shutil.copy2(root / "windows/licenses" / name, output / name)
for source in (root / "linux/licenses").iterdir():
    shutil.copy2(source, output / source.name)
(output / "packages.json").write_text(json.dumps(inventory, indent=2) + "\n")
assert any(item["package"].startswith("Avalonia/") for item in inventory)
runtime = json.loads((output.parent / "Nashi.Linux.runtimeconfig.json").read_text())["runtimeOptions"]["includedFrameworks"][0]["version"]
runtime_folder = next((folder / "microsoft.netcore.app.runtime.linux-x64" / runtime for folder in folders if (folder / "microsoft.netcore.app.runtime.linux-x64" / runtime).is_dir()))
for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"):
    shutil.copy2(runtime_folder / name, output / ("runtime-" + name))
print(f"Bundled license metadata for {len(inventory)} restored packages.")
