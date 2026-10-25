"""Verifie que l'auto-test embarque (SB_SELFTEST) est absent d'un build.

Cherche le nom de type `SelfTestRunner` (UTF-8, table #Strings) et la chaine
`SB_SELFTEST` (UTF-8 et UTF-16LE, table #US) dans des assemblies .NET.
Accepte des .dll ou des archives (.apk/.aab/.zip) : les .dll et le magasin
d'assemblies (libassembly-store.so) contenus dans l'archive sont analyses.
Si les assemblies du magasin sont compressees, seul le temoin positif
(--expect-present sur un build EnableSelfTest=true) dit si l'analyse est probante.

Usage :
  python tools/check_selftest_absent.py <fichier> [<fichier> ...]
  python tools/check_selftest_absent.py --expect-present <fichier>   (temoin positif)
Code de sortie : 0 si le resultat est celui attendu, 1 sinon.
"""
import sys
import zipfile

MARKERS = [
    b"SelfTestRunner",
    b"SelfTestHost",
    b"SB_SELFTEST",
    "SB_SELFTEST".encode("utf-16-le"),
]


def found_markers(data):
    return [m for m in MARKERS if m in data]


def scan(path):
    """Retourne la liste (nom, marqueurs trouves) des elements contenant un marqueur."""
    hits = []
    if zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as z:
            for info in z.infolist():
                name = info.filename
                if not (name.endswith(".dll") or "assembly-store" in name or name.endswith(".blob")):
                    continue
                markers = found_markers(z.read(info.filename))
                if markers:
                    hits.append((f"{path}!{info.filename}", markers))
    else:
        with open(path, "rb") as f:
            markers = found_markers(f.read())
        if markers:
            hits.append((path, markers))
    return hits


def main(argv):
    expect_present = "--expect-present" in argv
    files = [a for a in argv if a != "--expect-present"]
    if not files:
        print(__doc__)
        return 2

    all_hits = []
    for path in files:
        hits = scan(path)
        all_hits.extend(hits)
        print(f"{'PRESENT' if hits else 'ABSENT '} {path}")
        for name, markers in hits:
            print(f"    {name}: {[m.decode('latin-1') for m in markers]}")

    if expect_present:
        return 0 if all_hits else 1
    return 1 if all_hits else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
