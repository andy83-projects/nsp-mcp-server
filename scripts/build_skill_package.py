"""Build the architecture-independent companion skill ZIP using Python's standard library."""

import argparse
import hashlib
from pathlib import Path
import re
import zipfile


def build(version: str, output: Path) -> Path:
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?", version):
        raise ValueError("Use a release version without the leading v.")
    root = Path(__file__).resolve().parent.parent
    source = root / ".agents/skills/haltech-nsp"
    if not (source / "SKILL.md").is_file():
        raise FileNotFoundError("Companion SKILL.md is missing.")
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"haltech-nsp-skill-{version}.zip"
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(source.rglob("*")):
            if path.is_symlink():
                raise ValueError(f"Do not package skill symlinks: {path}")
            if path.is_file():
                bundle.write(path, (Path("haltech-nsp") / path.relative_to(source)).as_posix())
        bundle.write(root / "docs/SKILL.md", "INSTALL.md")
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix(".zip.sha256").write_text(f"{checksum}  {archive.name}\n", encoding="ascii")
    return archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--output-directory", type=Path, default=Path(__file__).resolve().parent.parent / "out/releases")
    args = parser.parse_args()
    print(build(args.version, args.output_directory))
