"""Porte de couverture : lit un rapport Cobertura et le compare au seuil.

Seuil de lignes fixe au lot 8B : mesure reelle 88,71 % (1509/1701 lignes,
2026-10-22, Windows) moins 2 points = 86,7 %. Voir docs/passation.md.

Usage : python tools/check_coverage.py <coverage.cobertura.xml> <seuil_lignes_%> [--summary <fichier.md>]
Code de sortie : 0 si couverture >= seuil, 1 sinon (ou si le rapport est vide).
"""
import sys
import xml.etree.ElementTree as ET


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    report, threshold = argv[0], float(argv[1])
    summary_path = argv[argv.index("--summary") + 1] if "--summary" in argv else None

    root = ET.parse(report).getroot()
    covered = int(root.get("lines-covered", "0"))
    valid = int(root.get("lines-valid", "0"))
    branch_rate = float(root.get("branch-rate", "0")) * 100
    files = {c.get("filename") for c in root.iter("class")}

    if valid == 0:
        result = "ECHEC : rapport vide (0 ligne mesuree), la couverture n'est pas collectee"
        line_rate = 0.0
        ok = False
    else:
        line_rate = covered * 100 / valid
        ok = line_rate >= threshold
        result = "OK" if ok else f"ECHEC : {line_rate:.2f} % < {threshold:.2f} %"

    lines = [
        "## Couverture de code (app, hors fichiers de test)",
        f"- Lignes : **{line_rate:.2f} %** ({covered}/{valid}), seuil {threshold:.2f} %",
        f"- Branches : {branch_rate:.2f} % (informatif)",
        f"- Fichiers mesures : {len(files)}",
        f"- Resultat : **{result}**",
    ]
    text = "\n".join(lines)
    print(text)
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as f:
            f.write(text + "\n")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
