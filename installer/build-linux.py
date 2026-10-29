"""Builds the Linux release: publishes the app for linux-x64 and packs it, with its icon, menu entry
and install script, into publish/linux/FactorySeat-<version>-linux-x64.tar.gz. Runs on Windows;
the tarball carries the Unix file modes the app and script need. The version comes from
Directory.Build.props.

    python installer/build-linux.py
"""
import io
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "publish" / "linux"
APP = OUT / "app"

version = ET.parse(ROOT / "Directory.Build.props").find("./PropertyGroup/Version").text

shutil.rmtree(OUT, ignore_errors=True)
subprocess.run(["dotnet", "publish", "src/LmuCareer.Linux", "-c", "Release", "-r", "linux-x64", "--self-contained",
                "-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true", "-o", str(APP)],
               cwd=ROOT, check=True)

# The menu icon: the Windows icon's largest image.
icon = OUT / "factory-seat.png"
with Image.open(ROOT / "src/LmuCareer.App/app.ico") as ico:
    ico.size = max(ico.info["sizes"])
    ico.convert("RGBA").resize((256, 256), Image.LANCZOS).save(icon)

name = f"FactorySeat-{version}-linux-x64"
files = {
    APP / "factory-seat": 0o755,
    APP / "Photino.Native.so": 0o644,
    ROOT / "installer/linux/install.sh": 0o755,
    ROOT / "installer/linux/factory-seat.desktop": 0o644,
    icon: 0o644,
    ROOT / "LICENSE": 0o644,
    ROOT / "NOTICE": 0o644,
}


def owned(mode):
    def apply(info):
        info.uid = info.gid = 1000
        info.uname = info.gname = ""
        info.mode = mode
        return info
    return apply


tarball = OUT / f"{name}.tar.gz"
with tarfile.open(tarball, "w:gz") as tar:
    for path, mode in files.items():
        if path.suffix in (".sh", ".desktop"):
            # Shell and desktop files must have Unix line endings, whatever git checked out.
            data = path.read_bytes().replace(b"\r\n", b"\n")
            info = owned(mode)(tarfile.TarInfo(f"{name}/{path.name}"))
            info.size = len(data)
            tar.addfile(info, io.BytesIO(data))
        else:
            tar.add(path, arcname=f"{name}/{path.name}", filter=owned(mode))

print(f"{tarball.relative_to(ROOT)}: {tarball.stat().st_size / 1024 / 1024:.1f} MB")
