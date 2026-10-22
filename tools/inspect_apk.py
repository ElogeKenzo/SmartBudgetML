"""Inspecte un APK avec aapt2 et applique les portes Google Play du projet.

Echec si :
  - targetSdkVersion < 36 (exigence Play :
    https://support.google.com/googleplay/android-developer/answer/11926878)
  - une permission utilisee n'est pas autorisee. Autorisees :
      * android.permission.USE_BIOMETRIC (seule permission systeme, CLAUDE.md regle 7)
      * <package>.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION, uniquement si l'APK la
        declare lui-meme avec protectionLevel=signature (ajoutee par AndroidX core,
        jamais demandee a l'utilisateur).

Usage : python tools/inspect_apk.py <aapt2> <fichier.apk> [--summary <fichier.md>]
"""
import re
import subprocess
import sys

MIN_TARGET_SDK = 36
ALLOWED_SYSTEM_PERMISSIONS = {"android.permission.USE_BIOMETRIC"}
INTERNAL_SUFFIX = ".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
PROTECTION_SIGNATURE = 0x2


def run(aapt2, *args):
    return subprocess.run([aapt2, *args], check=True, capture_output=True, text=True).stdout


def signature_permissions(manifest_tree):
    """Noms des <permission> declarees avec protectionLevel signature."""
    declared = set()
    blocks = re.split(r"\n\s*E: ", manifest_tree)
    for block in blocks:
        if not block.startswith("permission "):
            continue
        name = re.search(r':name\(0x01010003\)="([^"]+)"', block)
        level = re.search(r":protectionLevel\(0x01010009\)=0x([0-9a-fA-F]+)", block)
        if name and level and (int(level.group(1), 16) & 0xF) == PROTECTION_SIGNATURE:
            declared.add(name.group(1))
    return declared


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    aapt2, apk = argv[0], argv[1]
    summary_path = argv[argv.index("--summary") + 1] if "--summary" in argv else None

    badging = run(aapt2, "dump", "badging", apk)
    tree = run(aapt2, "dump", "xmltree", "--file", "AndroidManifest.xml", apk)

    package = re.search(r"^package: name='([^']+)'", badging, re.M).group(1)
    min_sdk = int(re.search(r"^minSdkVersion:'(\d+)'", badging, re.M).group(1))
    target_sdk = int(re.search(r"^targetSdkVersion:'(\d+)'", badging, re.M).group(1))
    permissions = sorted(set(re.findall(r"^uses-permission: name='([^']+)'", badging, re.M)))
    own_signature = signature_permissions(tree)

    errors = []
    if target_sdk < MIN_TARGET_SDK:
        errors.append(f"targetSdkVersion {target_sdk} < {MIN_TARGET_SDK}")

    rows = []
    for perm in permissions:
        if perm in ALLOWED_SYSTEM_PERMISSIONS:
            verdict = "autorisee"
        elif perm == package + INTERNAL_SUFFIX and perm in own_signature:
            verdict = "interne (signature, AndroidX)"
        else:
            verdict = "INTERDITE"
            errors.append(f"permission non autorisee : {perm}")
        rows.append(f"| `{perm}` | {verdict} |")

    lines = [
        "## Inspection de l'APK",
        f"- Paquet : `{package}`",
        f"- minSdkVersion : `{min_sdk}`",
        f"- targetSdkVersion : `{target_sdk}` (minimum exige : {MIN_TARGET_SDK})",
        "",
        "| Permission | Verdict |",
        "|---|---|",
        *rows,
        "",
        "**Resultat : " + ("ECHEC** : " + "; ".join(errors) if errors else "OK**"),
    ]
    report = "\n".join(lines)
    print(report)
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as f:
            f.write(report + "\n")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
