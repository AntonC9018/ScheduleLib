"""Candidate adoption rollback checks using a synthetic archive and no external tools."""
from contextlib import redirect_stdout
import hashlib
import io
import itertools
import json
from pathlib import Path
import runpy
import subprocess
import tempfile
import traceback
import unittest
from unittest.mock import Mock, patch
import xml.etree.ElementTree as ET
from zipfile import ZipFile

CANDIDATE = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'use-codingrules-candidate.py'))


class CandidateAdoptionTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.project = self.root / 'src/App/App.csproj'
        self.project.parent.mkdir(parents=True)
        self.project.write_text('<Project />')
        self.config = self.root / 'NuGet.Config'
        self.guidance = self.root / '.codingrules/agent-coding-rules.md'
        self.guidance.parent.mkdir()
        self.old_config = (CANDIDATE['MARKER'] + '\r\n<configuration><packageSources>'
                           '<add key="candidate" value="old-feed" />'
                           '<add key="application-feed-0" value="https://example.invalid/nuget" />'
                           '</packageSources></configuration>\r\n').encode()
        self.old_guidance = b'Previous guidance\r\nwith exact bytes.\r\n'
        self.new_guidance = b'Candidate guidance\n'
        self.package = self.root / 'Anton.CodingRules.0.1.0.nupkg'
        self.archive_assets = {
            'agent-guidance/agent-coding-rules.md': self.new_guidance,
            'analyzers/dotnet/cs/Anton.CodingRules.dll': b'synthetic analyzer',
        }
        with ZipFile(self.package, 'w') as archive:
            archive.writestr('Anton.CodingRules.nuspec',
                             '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
                             '<metadata><id>Anton.CodingRules</id><version>0.1.0</version>'
                             '<repository type="git" commit="synthetic-commit" /></metadata></package>')
            for name, content in self.archive_assets.items():
                archive.writestr(name, content)
        self.digest = hashlib.sha256(self.package.read_bytes()).hexdigest()
        self.local = self.root / 'tmp' / ('codingrules-' + self.digest)
        self.record = self.local / 'candidate.json'
        self.restored = self.local / 'packages/anton.codingrules/0.1.0'
        self.failure = RuntimeError('injected adoption failure')

    def _set_previous_files(self, config_exists, guidance_exists):
        self.config.unlink(missing_ok=True)
        self.guidance.unlink(missing_ok=True)
        self.record.unlink(missing_ok=True)
        if config_exists:
            self.config.write_bytes(self.old_config)
        if guidance_exists:
            self.guidance.write_bytes(self.old_guidance)

    def _adopt(self, fail_at=None):
        function = CANDIDATE['main']
        verify_graph = CANDIDATE['verify_graph']
        write_text = Path.write_text

        def run(command, **options):
            self.assertEqual(options, dict(cwd=self.root, check=True))
            self.assertFalse(self.record.exists(), 'Record must only be written after validation/export.')
            self.assertIn(CANDIDATE['MARKER'], self.config.read_text())
            if command[1] == 'restore':
                self.assertEqual(command, ['dotnet', 'restore', str(self.project), '--force', '--no-http-cache'])
                if fail_at == 'restore':
                    raise self.failure
                self.restored.mkdir(parents=True, exist_ok=True)
                archive = self.restored / 'anton.codingrules.0.1.0.nupkg'
                archive.write_bytes(self.package.read_bytes() if fail_at != 'archive' else b'wrong archive')
                for name, content in self.archive_assets.items():
                    asset = self.restored / name
                    asset.parent.mkdir(parents=True, exist_ok=True)
                    asset.write_bytes(content if fail_at != 'asset' else b'wrong asset')
                assets = self.project.parent / 'obj/project.assets.json'
                assets.parent.mkdir(exist_ok=True)
                assets.write_text(json.dumps({
                    'libraries': {'Anton.CodingRules/0.1.0': {'path': 'anton.codingrules/0.1.0'}},
                    'packageFolders': {str(self.local / 'packages'): {}},
                    'project': {'restore': {'frameworks': {}}},
                }))
            else:
                self.assertEqual(command, [
                    'dotnet', 'msbuild', str(self.project), '-t:ExportCodingRulesAgentGuidance',
                    '-p:CodingRulesAgentGuidanceDirectory=' + str(self.guidance.parent),
                ])
                self.guidance.write_bytes(b'mismatched guidance' if fail_at == 'mismatch' else self.new_guidance)
                if fail_at == 'export':
                    raise self.failure

        def graph(package, project):
            self.assertFalse(self.record.exists())
            if fail_at == 'graph':
                raise self.failure
            return verify_graph(package, project)

        def write(path, content, *args, **options):
            if path == self.config and fail_at == 'config':
                path.write_bytes(b'partial config')
                raise self.failure
            if path == self.record:
                self.assertEqual(self.guidance.read_bytes(), self.new_guidance)
                self.assertEqual(graph_mock.call_count, 1)
                self.assertEqual(run_mock.call_count, 2)
                if fail_at == 'record':
                    path.write_bytes(b'partial record')
                    raise self.failure
            return write_text(path, content, *args, **options)

        graph_mock = Mock(side_effect=graph)
        with patch.dict(function.__globals__, ROOT=self.root, verify_graph=graph_mock), \
                patch('sys.argv', ['use-codingrules-candidate.py', str(self.package),
                                   '--project', str(self.project.relative_to(self.root))]), \
                patch('subprocess.check_output', return_value='E https://example.invalid/nuget\n'), \
                patch('subprocess.run', side_effect=run) as run_mock, \
                patch.object(Path, 'write_text', autospec=True, side_effect=write) as write_mock, \
                redirect_stdout(io.StringIO()):
            function()
        return write_mock

    def _assert_rollback(self, stage, message=None):
        if stage in ('restore', 'export'):
            self.failure = subprocess.CalledProcessError(1, ['dotnet', stage])
        for config_exists, guidance_exists in itertools.product((False, True), repeat=2):
            with self.subTest(config_exists=config_exists, guidance_exists=guidance_exists):
                self._set_previous_files(config_exists, guidance_exists)
                if message:
                    with self.assertRaisesRegex(ValueError, message):
                        self._adopt(stage)
                else:
                    with self.assertRaises(type(self.failure)) as raised:
                        self._adopt(stage)
                    self.assertIs(raised.exception, self.failure)
                self.assertEqual(self.config.exists(), config_exists)
                self.assertEqual(self.guidance.exists(), guidance_exists)
                if config_exists:
                    self.assertEqual(self.config.read_bytes(), self.old_config)
                if guidance_exists:
                    self.assertEqual(self.guidance.read_bytes(), self.old_guidance)
                if stage != 'record':
                    self.assertFalse(self.record.exists())

    def test_restore_failure_restores_config_and_guidance(self):
        self._assert_rollback('restore')

    def test_archive_verification_failure_restores_config_and_guidance(self):
        self._assert_rollback('archive', 'Restored archive differs')

    def test_asset_verification_failure_restores_config_and_guidance(self):
        self._assert_rollback('asset', 'Restored asset differs')

    def test_graph_verification_failure_restores_config_and_guidance(self):
        self._assert_rollback('graph')

    def test_export_failure_restores_config_and_replaced_guidance(self):
        self._assert_rollback('export')

    def test_guidance_mismatch_restores_config_and_guidance(self):
        self._assert_rollback('mismatch', 'Exported guidance differs')

    def test_record_write_failure_restores_config_and_guidance(self):
        self._assert_rollback('record')

    def test_partial_config_write_failure_restores_config_and_guidance(self):
        self._assert_rollback('config')

    def test_success_writes_config_guidance_and_only_the_final_record(self):
        for previous_files in (False, True):
            with self.subTest(previous_files=previous_files):
                self._set_previous_files(previous_files, previous_files)
                writes = self._adopt()
                self.assertEqual(self.guidance.read_bytes(), self.new_guidance)
                self.assertEqual(json.loads(self.record.read_text()), dict(
                    package=self.package.name, version='0.1.0', source_commit='synthetic-commit',
                    sha256=self.digest))
                self.assertEqual([call.args[0] for call in writes.call_args_list][-1], self.record)
                self.assertEqual(sum(call.args[0] == self.record for call in writes.call_args_list), 1)
                self.assertEqual((self.local / 'feed' / self.package.name).read_bytes(), self.package.read_bytes())
                settings = ET.parse(self.config)
                self.assertEqual(settings.find('config/add').get('value'), str(self.local / 'packages'))
                self.assertEqual(settings.find("packageSources/add[@key='candidate']").get('value'),
                                 str(self.local / 'feed'))
                self.assertEqual(settings.find("packageSourceMapping/packageSource[@key='candidate']/package").get('pattern'),
                                 'Anton.CodingRules')
                self.assertEqual(settings.find("packageSourceMapping/packageSource[@key='application-feed-0']/package").get('pattern'), '*')

    def test_hand_written_config_is_never_replaced(self):
        self.config.write_bytes(b'<configuration />\r\n')
        self.guidance.write_bytes(self.old_guidance)
        with redirect_stdout(io.StringIO()), patch('sys.stderr', new_callable=io.StringIO):
            with self.assertRaises(SystemExit) as raised:
                self._adopt()
        self.assertEqual(raised.exception.code, 2)
        self.assertEqual(self.config.read_bytes(), b'<configuration />\r\n')
        self.assertEqual(self.guidance.read_bytes(), self.old_guidance)
        self.assertFalse(self.local.exists())

    def test_rollback_failure_keeps_original_error_and_attempts_both_files(self):
        class LegacyRuntimeError(RuntimeError):
            @property
            def add_note(self):
                raise AttributeError('add_note is unavailable before Python 3.11')

        write_bytes = Path.write_bytes
        for failure_type, failed_paths in itertools.product(
                (RuntimeError, LegacyRuntimeError),
                ((), (self.config,), (self.config, self.guidance))):
            with self.subTest(failure_type=failure_type, failed_paths=failed_paths):
                self._set_previous_files(True, True)
                failure = failure_type('injected adoption failure')
                attempted = []

                def write(path, content):
                    attempted.append(path)
                    if path in failed_paths:
                        raise OSError(f'injected rollback failure for {path}')
                    return write_bytes(path, content)

                with patch.object(Path, 'write_bytes', write), self.assertRaises(failure_type) as raised:
                    with CANDIDATE['restore_files_on_failure'](self.config, self.guidance):
                        write_bytes(self.config, b'new config')
                        write_bytes(self.guidance, b'new guidance')
                        raise failure
                self.assertIs(raised.exception, failure)
                self.assertEqual(attempted, [self.config, self.guidance])
                self.assertEqual(self.config.read_bytes(),
                                 b'new config' if self.config in failed_paths else self.old_config)
                self.assertEqual(self.guidance.read_bytes(),
                                 b'new guidance' if self.guidance in failed_paths else self.old_guidance)
                reported = ''.join(traceback.format_exception(type(failure), failure, failure.__traceback__))
                for path in failed_paths:
                    self.assertIn(f'Could not restore {path}: injected rollback failure for {path}', reported)
                if failed_paths and not callable(getattr(failure, 'add_note', None)):
                    self.assertIsInstance(failure.__cause__, RuntimeError)


if __name__ == '__main__':
    unittest.main()
