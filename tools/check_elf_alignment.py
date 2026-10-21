"""Verifie l'alignement 16 Ko des bibliotheques natives d'un APK, AAB ou AAR.

Google Play exige des segments ELF LOAD alignes sur 16 Ko (p_align >= 0x4000)
pour les bibliotheques 64 bits (arm64-v8a, x86_64) des applications ciblant
Android 15+ : https://developer.android.com/guide/practices/page-sizes

Usage : python tools/check_elf_alignment.py <fichier.apk|.aab|.aar>
Code de sortie : 0 si toutes les .so 64 bits sont alignees, 1 sinon.
"""
import struct
import sys
import zipfile

PAGE_16K = 16384
ABIS_64 = ("arm64-v8a", "x86_64")


def min_load_alignment(data):
    if data[:4] != b"\x7fELF":
        raise ValueError("pas un fichier ELF")
    is64 = data[4] == 2
    if is64:
        phoff, = struct.unpack_from("<Q", data, 0x20)
        phentsize, phnum = struct.unpack_from("<HH", data, 0x36)
    else:
        phoff, = struct.unpack_from("<I", data, 0x1C)
        phentsize, phnum = struct.unpack_from("<HH", data, 0x2A)
    aligns = []
    for i in range(phnum):
        off = phoff + i * phentsize
        p_type, = struct.unpack_from("<I", data, off)
        if p_type != 1:  # PT_LOAD
            continue
        if is64:
            p_align, = struct.unpack_from("<Q", data, off + 0x30)
        else:
            p_align, = struct.unpack_from("<I", data, off + 0x1C)
        aligns.append(p_align)
    return min(aligns) if aligns else 0


def main(path):
    failures = 0
    checked = 0
    with zipfile.ZipFile(path) as z:
        for name in z.namelist():
            if not name.endswith(".so"):
                continue
            if not any("/" + abi + "/" in "/" + name for abi in ABIS_64):
                continue
            align = min_load_alignment(z.read(name))
            ok = align >= PAGE_16K
            checked += 1
            failures += 0 if ok else 1
            print(f"{'ALIGNED  ' if ok else 'UNALIGNED'} p_align={align:>8} {name}")
    if checked == 0:
        print("Aucune bibliotheque native 64 bits trouvee.")
        return 1
    print(f"{checked - failures}/{checked} bibliotheques alignees sur 16 Ko")
    return 1 if failures else 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
