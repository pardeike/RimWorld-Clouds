"""Verify packaging and paired installation only in disposable directories."""
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location('stage_mod', REPO / 'scripts/stage-mod.py')
stage_mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(stage_mod)


class DeliveryContracts(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(dir=REPO / 'artifacts')
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.artifacts = self.root / 'artifacts'
        self.stage = self.artifacts / 'package/Clouds'
        self.mods = self.root / 'game/Mods'
        self.assembly = self.root / '1.7/Assemblies/Clouds.dll'
        self.assembly.parent.mkdir(parents=True)
        self.assembly.write_bytes(b'current 1.7 build')
        (self.assembly.parent / 'CrossPromotion.dll').write_bytes(b'CrossPromotion fixture')
        shutil.copytree(REPO / '1.7/Resources', self.root / '1.7/Resources')
        for name in [*stage_mod.MUTABLE, 'Directory.Build.props']:
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(REPO / name, target)
        for name, value in [('ROOT', self.root), ('ARTIFACTS', self.artifacts), ('STAGE', self.stage)]:
            handle = patch.object(stage_mod, name, value)
            handle.start()
            self.addCleanup(handle.stop)
        stage_mod.stage(self.assembly)
        companion = self.artifacts / 'BridgeTools/Clouds/Clouds.BridgeTools.dll'
        companion.parent.mkdir(parents=True)
        companion.write_bytes(b'matching companion fixture')

    def test_published_baseline_and_shared_bundles_are_preserved(self):
        local_old = self.root / '1.6/Assemblies/Clouds.dll'
        local_old.parent.mkdir(parents=True)
        local_old.write_bytes(b'unpublished local DLL')
        stage_mod.stage(self.assembly)
        with zipfile.ZipFile(self.artifacts / 'Clouds.zip') as archive:
            for path, data in stage_mod.baseline_files().items():
                if path.as_posix() not in stage_mod.MUTABLE:
                    self.assertEqual(archive.read(path.as_posix()), data, str(path))
            self.assertFalse(any('BridgeTools' in n or 'RimBridgeServer.' in n for n in archive.namelist()))
        self.assertEqual(local_old.read_bytes(), b'unpublished local DLL')

    def frozen_identities(self):
        result = {}
        for p in stage_mod.baseline_files():
            if p.as_posix() not in stage_mod.MUTABLE:
                s = (self.mods / 'Clouds' / p).stat()
                result[p] = (s.st_dev, s.st_ino, s.st_mtime_ns)
        return result

    def test_repeated_paired_install_preserves_older_file_inodes_and_mtimes(self):
        stage_mod.install_unlocked(self.mods, 'true')
        for path in self.frozen_identities():
            os.utime(self.mods / 'Clouds' / path, ns=(1000000000000000000, 1000000000000000000))
        before = self.frozen_identities()
        with (self.mods / 'Clouds/1.6/Assemblies/Clouds.dll').open('rb'):
            stage_mod.install_unlocked(self.mods, 'true')
            stage_mod.install_unlocked(self.mods, 'true')
        self.assertEqual(self.frozen_identities(), before)
        stage_mod.verify_install(self.mods, 'true')
        self.assertEqual([p.name for p in (self.mods.parent / 'BridgeTools/Clouds').iterdir()], ['Clouds.BridgeTools.dll'])

    def test_mismatching_installed_legacy_fails_before_mutation(self):
        stage_mod.install_unlocked(self.mods, 'true')
        (self.mods / 'Clouds/1.6/Assemblies/Clouds.dll').write_bytes(b'unpublished installed bytes')
        before = stage_mod.files(self.mods)
        identities = self.frozen_identities()
        with self.assertRaisesRegex(ValueError, 'Existing older installed files'):
            stage_mod.install_unlocked(self.mods, 'true')
        self.assertEqual(stage_mod.files(self.mods), before)
        self.assertEqual(self.frozen_identities(), identities)

    def test_stale_package_refuses_before_mutation(self):
        stage_mod.install_unlocked(self.mods, 'true')
        before = stage_mod.files(self.mods)
        with zipfile.ZipFile(self.artifacts / 'Clouds.zip', 'a') as archive:
            archive.writestr('unexpected.txt', 'stale package')
        with self.assertRaisesRegex(ValueError, 'public ZIP'):
            stage_mod.install_unlocked(self.mods, 'true')
        self.assertEqual(stage_mod.files(self.mods), before)

    def project(self):
        source = self.root / 'Source'
        source.mkdir(exist_ok=True)
        project = source / 'Clouds.csproj'
        shutil.copyfile(REPO / 'Source/Clouds.csproj', project)
        return project

    def msbuild(self, *args):
        env = dict(os.environ)
        env.pop('RIMWORLD_MOD_DIR', None)
        result = subprocess.run(['dotnet', 'msbuild', str(self.project()), '-nologo', *args],
                                env=env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_normal_project_syncs_both_metadata_versions(self):
        for name, element in [('About/About.xml', 'modVersion'), ('About/Manifest.xml', 'version')]:
            document = ET.parse(self.root / name)
            document.find(element).text = '0.0.0.0'
            document.write(self.root / name, encoding='utf-8')
        self.msbuild('-t:SyncModMetadata')
        expected = ET.parse(self.root / 'Directory.Build.props').findtext('.//ModVersion')
        self.assertEqual(ET.parse(self.root / 'About/About.xml').findtext('modVersion'), expected)
        self.assertEqual(ET.parse(self.root / 'About/Manifest.xml').findtext('version'), expected)

    def test_normal_msbuild_copy_target_preserves_old_files(self):
        stage_mod.install_unlocked(self.mods, 'false')
        before = self.frozen_identities()
        (self.root / 'scripts').mkdir()
        shutil.copyfile(REPO / 'scripts/stage-mod.py', self.root / 'scripts/stage-mod.py')
        (self.artifacts / 'frozen').mkdir()
        (self.artifacts / 'frozen/Clouds.zip').symlink_to(stage_mod.BASELINE)
        self.msbuild('-t:_CopyToRimworldUnlocked', '-p:RimworldDeployGuardInner=true',
                     '-p:BuildBridgeTools=false', '-p:RIMWORLD_MOD_DIR=' + str(self.mods))
        self.assertEqual(self.frozen_identities(), before)
        stage_mod.verify_install(self.mods, 'false')


if __name__ == '__main__':
    unittest.main()
