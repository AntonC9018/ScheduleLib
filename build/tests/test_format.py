"""Regression checks for the wrapper's source-writing boundary, without tool downloads."""
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch

FORMAT = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'format.py'))


class FormattingWriteTests(unittest.TestCase):
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
