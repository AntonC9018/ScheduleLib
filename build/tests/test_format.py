"""Regression checks for the wrapper's source-writing boundary, without tool downloads."""
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch

FORMAT = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'format.py'))


class FormattingWriteTests(unittest.TestCase):
    def test_nonconvergent_style_never_writes_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'Source.cs'
            source.write_bytes(b'original')

            def toggle(content):
                return {source: b'other' if content[source] == b'original' else b'original'}

            function = FORMAT['format_sources']
            with patch.dict(function.__globals__, ROOT=root, layout_sources=toggle,
                            package_style=lambda *args: None):
                with self.assertRaisesRegex(ValueError, 'did not stabilize'):
                    function([source], check=False, target=root / 'App.csproj', style=True)
            self.assertEqual(source.read_bytes(), b'original')

    def test_check_never_writes_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'Source.cs'
            source.write_bytes(b'// comment\noriginal')

            def format_copies(command, **options):
                mirror = Path(command[3]).parent
                (mirror / 'Source.cs').write_bytes(b'// comment\nformatted')

            function = FORMAT['format_sources']
            with patch.dict(function.__globals__, ROOT=root), patch('subprocess.run', side_effect=format_copies):
                self.assertEqual(function([source], check=True), 1)
            self.assertEqual(source.read_bytes(), b'// comment\noriginal')

    def test_preflight_detects_edits_before_any_write(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first, second = root / 'First.cs', root / 'Second.cs'
            first.write_bytes(b'first')
            second.write_bytes(b'second')

            def format_copies(command, **options):
                mirror = Path(command[3]).parent
                (mirror / 'First.cs').write_bytes(b'formatted')
                (mirror / 'Second.cs').write_bytes(b'formatted')
                second.write_bytes(b'user edit')

            function = FORMAT['format_sources']
            with patch.dict(function.__globals__, ROOT=root), patch('subprocess.run', side_effect=format_copies):
                with self.assertRaisesRegex(ValueError, 'Second.cs'):
                    function([first, second], check=False)
            self.assertEqual(first.read_bytes(), b'first')
            self.assertEqual(second.read_bytes(), b'user edit')

    def test_package_style_keeps_context_and_uses_only_layout_fixes(self):
        with tempfile.TemporaryDirectory() as directory, tempfile.TemporaryDirectory() as output:
            root, mirror = Path(directory), Path(output)
            names = ['App.csproj', 'Directory.Build.props', '.editorconfig', 'NuGet.Config',
                     'Source.cs', 'Other.cs', 'obj/Generated.cs', 'tmp/Scratch.cs',
                     '.registry-history/Private.cs', 'data/private.json', 'secrets.json']
            for name in names:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(name)
            (mirror / 'Source.cs').write_text('already cleaned')
            function = FORMAT['package_style']
            with patch.dict(function.__globals__, ROOT=root), patch('subprocess.run') as run:
                function(mirror, root / 'App.csproj', [root / 'Source.cs'])
            self.assertEqual((mirror / 'Source.cs').read_text(), 'already cleaned')
            self.assertEqual({p.relative_to(mirror).as_posix() for p in mirror.rglob('*') if p.is_file()},
                             {'App.csproj', 'Directory.Build.props', '.editorconfig', 'NuGet.Config', 'Source.cs', 'Other.cs'})
            commands = [call.args[0] for call in run.call_args_list]
            self.assertEqual(commands[0][2], str(mirror / 'App.csproj'))
            self.assertEqual(commands[1][commands[1].index('--diagnostics') + 1:commands[1].index('--include')],
                             ['RCS0042', 'RCS1016', 'CR0800', 'CR0801', 'CR0802'])
            self.assertEqual(commands[1][-1], 'Source.cs')
            self.assertEqual(commands[2][2], 'whitespace')

    def test_edit_during_copy_back_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first, second = root / 'First.cs', root / 'Second.cs'
            first.write_bytes(b'original first')
            second.write_bytes(b'original second')
            write = Path.write_bytes

            def format_copies(command, **options):
                mirror = Path(command[3]).parent
                write(mirror / 'First.cs', b'formatted first')
                write(mirror / 'Second.cs', b'formatted second')

            def copy_back(path, content):
                if path == first:
                    write(second, b'user edit')
                return write(path, content)

            function = FORMAT['format_sources']
            with patch.dict(function.__globals__, ROOT=root), \
                    patch('subprocess.run', side_effect=format_copies), \
                    patch.object(Path, 'write_bytes', copy_back):
                with self.assertRaisesRegex(ValueError, 'Second.cs'):
                    function([first, second], check=False)
            self.assertEqual(first.read_bytes(), b'formatted first')
            self.assertEqual(second.read_bytes(), b'user edit')


if __name__ == '__main__':
    unittest.main()
