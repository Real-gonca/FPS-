#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Gera os arquivos .nip (NVIDIA Profile Inspector) para o BlueStacks 5 / Free Fire.

Formato .nip (XML serializado pela classe Profiles do NPI):
  NPI 2.3+ / 3.x  -> raiz <Profiles>
  NPI 2.1.x       -> raiz <ArrayOfProfile>

Os IDs (settingId) e os valores abaixo foram tirados do próprio arquivo
CustomSettingNames.xml / Reference.xml que acompanha o NVIDIA Profile Inspector,
para garantir que existem no banco de dados do driver.

Uso:  python3 gerar-nip.py
"""

import os
import xml.etree.ElementTree as ET

PROFILE_NAME = "BlueStacks 5 - Free Fire 90 FPS"
EXECUTAVEL = "HD-Player.exe"

# (nome amigável, settingId em hex, valor DWORD em decimal)
SETTINGS = [
    # Mantém o GPU no clock máximo enquanto o BlueStacks está aberto.
    ("Power Management - Mode", 0x1057EB71, 1),          # 1 = Prefer maximum performance
    # VSync desligado no driver: deixa o jogo passar de 60 FPS e reduz latência.
    ("Vertical Sync", 0x00A879CF, 0x08416747),           # 0x08416747 = Force off
    # Menos frames enfileirados = menos input lag.
    ("Maximum Pre-Rendered Frames", 0x007BA09E, 1),
    # Otimização multithread do driver ligada (o emulador é pesado em CPU).
    ("Threaded Optimization", 0x20C1221E, 1),            # 1 = On
    # Filtragem de textura em modo desempenho (reduz carga na GPU).
    ("Texture Filtering - Quality", 0x00CE2691, 0x14),   # 0x14 = High performance
    ("Triple Buffering", 0x20FDD1F9, 0),                 # 0 = Off (com VSync off não serve de nada)
    # Sem limite de FPS do driver: quem manda é o BlueStacks (90).
    ("Frame Rate Limiter V3", 0x10835002, 0),            # 0 = Off
    # Caminho de agendamento de baixa latência do driver.
    ("Ultra Low Latency - Enabled", 0x10835000, 1),      # 1 = On
    # Cache de shaders OpenGL (o BlueStacks renderiza via OpenGL).
    ("Shader Cache - Enabled (OpenGL)", 0x208F3F2E, 1),  # 1 = Enabled
    ("Shader Cache - Cache Size", 0x00AC8497, 0x400),    # 0x400 = 1 GB
]

# Campos na mesma ordem da classe Profile do NPI (XmlSerializer).
FIELDS = ("ProfileName", "Executeables", "Settings")


def build(root_tag: str) -> ET.ElementTree:
    root = ET.Element(root_tag)
    profile = ET.SubElement(root, "Profile")

    ET.SubElement(profile, "ProfileName").text = PROFILE_NAME

    exes = ET.SubElement(profile, "Executeables")
    ET.SubElement(exes, "string").text = EXECUTAVEL

    settings = ET.SubElement(profile, "Settings")
    for name, sid, value in SETTINGS:
        ps = ET.SubElement(settings, "ProfileSetting")
        ET.SubElement(ps, "SettingNameInfo").text = name
        ET.SubElement(ps, "SettingID").text = str(sid)
        ET.SubElement(ps, "SettingValue").text = str(value)
        ET.SubElement(ps, "ValueType").text = "Dword"

    return ET.ElementTree(root)


def write(tree: ET.ElementTree, path: str) -> None:
    # O NPI exporta em UTF-16 com BOM (Encoding.Unicode); mantemos o mesmo padrão.
    xml = ET.tostring(tree.getroot(), encoding="unicode")
    # XmlSerializer escreve cada elemento em uma linha; indentamos para leitura humana.
    import re
    xml = re.sub(r"><", ">\n<", xml)
    data = '<?xml version="1.0" encoding="utf-16"?>\r\n' + xml + "\r\n"
    with open(path, "wb") as fh:
        fh.write(b"\xff\xfe" + data.encode("utf-16-le"))
    print(f"gravado: {path} ({os.path.getsize(path)} bytes)")


def main() -> None:
    here = os.path.dirname(os.path.abspath(__file__))
    write(build("Profiles"), os.path.join(here, "BlueStacks-FreeFire-90FPS.nip"))
    write(build("ArrayOfProfile"), os.path.join(here, "BlueStacks-FreeFire-90FPS-NPI-2.1.nip"))


if __name__ == "__main__":
    main()
