# PyInstaller specification for the asset-free legal-content preparation tool.
from pathlib import Path

import pyffi

content_root = Path(SPECPATH)
tools = content_root / "tools"
pyffi_root = Path(pyffi.__file__).resolve().parent
pyffi_data = [(str(pyffi_root / "VERSION"), "pyffi")]
pyffi_data.extend(
    (str(source), str(Path("pyffi") / source.parent.relative_to(pyffi_root)))
    for source in pyffi_root.rglob("*.xml")
)

analysis = Analysis(
    [str(tools / "prepare_legal_assets.py")],
    pathex=[str(tools)],
    binaries=[],
    datas=pyffi_data,
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
python_archive = PYZ(analysis.pure)
executable = EXE(
    python_archive,
    analysis.scripts,
    analysis.binaries,
    analysis.datas,
    [],
    name="OpenNV.Content",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
