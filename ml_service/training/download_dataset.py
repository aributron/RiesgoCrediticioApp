"""Descarga una copia pública versionada de GMSC y verifica su checksum."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
METADATA_URL = "https://www.openml.org/api/v1/json/data/45577"
EXPECTED_MD5 = "53fbc48b682ecfac16697dfea4cb3c39"


def download(destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(METADATA_URL, headers={"User-Agent": "AcademicCreditRisk/1.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        metadata = json.load(response)
    description = metadata["data_set_description"]
    if description["id"] != "45577" or description["version"] != "1":
        raise ValueError("Cambió la versión del dataset; revisar antes de entrenar.")
    if description["md5_checksum"] != EXPECTED_MD5:
        raise ValueError("Cambió el checksum publicado; revisar antes de entrenar.")
    target = destination / "Give-Me-Some-Credit.arff"
    if not target.exists() or hashlib.md5(target.read_bytes()).hexdigest() != EXPECTED_MD5:
        request = urllib.request.Request(description["url"], headers={"User-Agent": "AcademicCreditRisk/1.0"})
        with urllib.request.urlopen(request, timeout=120) as response:
            content = response.read()
        if hashlib.md5(content).hexdigest() != EXPECTED_MD5:
            raise ValueError("El archivo descargado no coincide con el checksum de OpenML.")
        temporary = target.with_suffix(".part")
        temporary.write_bytes(content)
        temporary.replace(target)
    (destination / "source_metadata.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Dataset verificado: {target} ({target.stat().st_size:,} bytes)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, default=ROOT / "data" / "raw")
    download(parser.parse_args().destination)

