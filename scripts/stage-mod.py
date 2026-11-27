#!/usr/bin/env python3
"""Stage current content beside the checksum-pinned published payload."""
import hashlib
from pathlib import Path
import shutil
import sys
import urllib.request
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / 'artifacts'
STAGE = ARTIFACTS / 'package/Clouds'
BASELINE = ARTIFACTS / 'frozen/Clouds.zip'
BASELINE_URL = 'https://github.com/pardeike/RimWorld-Clouds/releases/download/v2.4.0.0/Clouds.zip'
BASELINE_SHA256 = '4408984cdc50f5821e173d8c51b9e2bc81eeeef18759dc1e03a1e46f84c76507'
MUTABLE = {'About/About.xml', 'About/Manifest.xml', 'LoadFolders.xml', 'README.md'}
PUBLIC_ASSEMBLIES = {Path('Assemblies/Clouds.dll'), Path('Assemblies/CrossPromotion.dll')}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def files(folder):
    return {p.relative_to(folder): digest(p) for p in folder.rglob('*') if p.is_file()}


def baseline_files():
    BASELINE.parent.mkdir(parents=True, exist_ok=True)
    if not BASELINE.exists():
        download = BASELINE.with_suffix('.download')
        try:
            with urllib.request.urlopen(BASELINE_URL, timeout=30) as response, download.open('wb') as output:
                shutil.copyfileobj(response, output)
            if digest(download) != BASELINE_SHA256:
                raise ValueError('Published baseline download has an unexpected SHA-256.')
            download.replace(BASELINE)
        finally:
            download.unlink(missing_ok=True)
    if digest(BASELINE) != BASELINE_SHA256:
        raise ValueError(f'Frozen payload checksum mismatch: {BASELINE}. Restore the published v2.4.0.0 ZIP.')
    with zipfile.ZipFile(BASELINE) as archive:
        result = {}
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            path = Path(entry.filename)
            if path.parts[0] != 'Clouds' or '..' in path.parts:
                raise ValueError(f'Unexpected baseline archive path: {entry.filename}')
            result[Path(*path.parts[1:])] = archive.read(entry)
        return result


def stage(assembly):
    baseline = baseline_files()
    if not assembly.is_file() or assembly.resolve() != (ROOT / '1.7/Assemblies/Clouds.dll').resolve():
        raise ValueError('The active build must produce 1.7/Assemblies/Clouds.dll.')
    shutil.rmtree(STAGE, ignore_errors=True)
    STAGE.mkdir(parents=True)
    for path, data in baseline.items():
        target = STAGE / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    for name in MUTABLE:
        shutil.copyfile(ROOT / name, STAGE / name)
    version = ET.parse(ROOT / 'Directory.Build.props').findtext('.//ModVersion')
    if not version:
        raise ValueError('Directory.Build.props must define ModVersion.')
    for name, query in [('About/About.xml', 'modVersion'), ('About/Manifest.xml', 'version')]:
        document = ET.parse(STAGE / name)
        element = document.getroot().find(query)
        if element is None:
            raise ValueError(f'Missing version element in {name}.')
        element.text = version
        document.write(STAGE / name, encoding='utf-8', xml_declaration=True)
    for path in (ROOT / '1.7').rglob('*'):
        if not path.is_file() or path.name == '.DS_Store' or path.name.startswith('._'):
            continue
        relative = path.relative_to(ROOT)
        if 'Assemblies' in relative.parts and path.relative_to(ROOT / '1.7') not in PUBLIC_ASSEMBLIES:
            continue
        target = STAGE / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
    for path, data in baseline.items():
        if path.as_posix() not in MUTABLE and (STAGE / path).read_bytes() != data:
            raise ValueError(f'Frozen published payload changed during staging: {path}')
    current = files(STAGE / '1.7')
    if {p for p in current if p.parts[0] == 'Assemblies'} != PUBLIC_ASSEMBLIES:
        raise ValueError('Active public assemblies must contain Clouds.dll and CrossPromotion.dll only.')
    for platform in ('win', 'linux', 'mac'):
        if not (STAGE / '1.7/Resources' / ('clouds_' + platform)).is_file():
            raise ValueError(f'The active 1.7 bundle is missing: {platform}')
    for path in current:
        if ('BridgeTools' in path.parts or path.suffix.lower() in ('.pdb', '.mdb')
                or (path.suffix.lower() == '.dll' and path not in PUBLIC_ASSEMBLIES)):
            raise ValueError(f'Development assembly or debug file in the active public payload: {path}')
    package = ARTIFACTS / 'Clouds.zip'
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(STAGE.rglob('*')):
            if path.is_file():
                archive.write(path, path.relative_to(STAGE).as_posix())
    with zipfile.ZipFile(package) as archive:
        packaged = {Path(n): hashlib.sha256(archive.read(n)).hexdigest() for n in archive.namelist()}
    if packaged != files(STAGE):
        raise ValueError('The public ZIP differs from the staged payload.')


def verify_deploy_input(mods, companion):
    baseline = baseline_files()
    staged = files(STAGE)
    frozen = {p: hashlib.sha256(data).hexdigest() for p, data in baseline.items() if p.as_posix() not in MUTABLE}
    if {p: h for p, h in staged.items() if p.parts[0] != '1.7' and p.as_posix() not in MUTABLE} != frozen:
        raise ValueError('The staged frozen payload differs from the published baseline.')
    for path in PUBLIC_ASSEMBLIES:
        if staged.get(Path('1.7') / path) != digest(ROOT / '1.7' / path):
            raise ValueError(f'The staged mod DLL differs from the current build: {path}. Run the quiet build again.')
    with zipfile.ZipFile(ARTIFACTS / 'Clouds.zip') as archive:
        packaged = {Path(n): hashlib.sha256(archive.read(n)).hexdigest() for n in archive.namelist()}
    if staged != packaged:
        raise ValueError('The public ZIP differs from the staged payload. Run the quiet build again.')
    if companion != 'false' and not (ARTIFACTS / 'BridgeTools/Clouds/Clouds.BridgeTools.dll').is_file():
        raise ValueError('The companion build is missing.')
    installed = mods / 'Clouds'
    if installed.is_symlink():
        raise ValueError('The installed mod is a symlink. Preserve its target and use a normal mod directory.')
    if installed.exists():
        existing = {p: h for p, h in files(installed).items() if p.parts[0] != '1.7' and p.as_posix() not in MUTABLE}
        if existing != frozen:
            raise ValueError('Existing older installed files differ from the frozen published payload. Preserve them and resolve the difference before installation.')


def install_unlocked(mods, companion):
    # The repository deployment guard holds the physical-path lock and checks the game process.
    verify_deploy_input(mods, companion)
    installed = mods / 'Clouds'
    frozen = [p for p in baseline_files() if p.as_posix() not in MUTABLE]
    for path in frozen:
        target = installed / path
        if target.exists():
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(STAGE / path, target)
    # Existing frozen files and their directories are never removed, copied, or timestamped.
    shutil.rmtree(installed / '1.7', ignore_errors=True)
    shutil.copytree(STAGE / '1.7', installed / '1.7')
    for name in MUTABLE:
        target = installed / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(STAGE / name, target)
    if companion != 'false':
        bundle = mods.parent / 'BridgeTools/Clouds'
        shutil.rmtree(bundle, ignore_errors=True)
        bundle.mkdir(parents=True)
        shutil.copy2(ARTIFACTS / 'BridgeTools/Clouds/Clouds.BridgeTools.dll', bundle)
    shutil.copy2(ARTIFACTS / 'Clouds.zip', mods / 'Clouds.zip')
    verify_install(mods, companion)


def verify_install(mods, companion):
    if files(mods / 'Clouds') != files(STAGE):
        raise ValueError('The installed mod differs from the staged payload.')
    if digest(mods / 'Clouds.zip') != digest(ARTIFACTS / 'Clouds.zip'):
        raise ValueError('The installed ZIP differs from the local package.')
    if companion != 'false':
        name = Path('Clouds/Clouds.BridgeTools.dll')
        if files(mods.parent / 'BridgeTools/Clouds') != {Path(name.name): digest(ARTIFACTS / 'BridgeTools' / name)}:
            raise ValueError('The installed companion differs from the current build.')


if __name__ == '__main__':
    try:
        if len(sys.argv) == 4 and sys.argv[1] == '--verify-install':
            verify_install(Path(sys.argv[2]), sys.argv[3])
        elif len(sys.argv) == 4 and sys.argv[1] == '--verify-deploy-input':
            verify_deploy_input(Path(sys.argv[2]), sys.argv[3])
        elif len(sys.argv) == 4 and sys.argv[1] == '--install-unlocked':
            install_unlocked(Path(sys.argv[2]), sys.argv[3])
        elif len(sys.argv) == 2:
            stage(Path(sys.argv[1]).resolve())
        else:
            raise ValueError('Usage: stage-mod.py ASSEMBLY | --verify-deploy-input/--verify-install/--install-unlocked MODS BUILD_BRIDGE_TOOLS')
    except (OSError, ValueError, zipfile.BadZipFile, ET.ParseError) as error:
        sys.exit(f'Package verification failed: {error}')
