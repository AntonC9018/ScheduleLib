#!/usr/bin/env python3
"""Verify the real formatter and distinguish package coverage from retained CleanupCode."""
from pathlib import Path
import runpy
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / 'build/tests/fixtures/Formatting'
OUTPUT = ROOT / 'tmp/formatting-verification'


def run(*command, expected=0):
    print('$ ' + ' '.join(command), flush=True)
    result = subprocess.run(command, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(result.stdout, flush=True)
    assert result.returncode == expected, (result.returncode, expected)
    return result.stdout


def main():
    source = FIXTURE / 'Probe.cs'
    if source.exists():
        raise ValueError('Refusing to overwrite an existing Probe.cs.')
    project = (FIXTURE / 'Formatting.csproj').relative_to(ROOT).as_posix()
    selected = source.relative_to(ROOT).as_posix()
    original = (FIXTURE / 'Input.cs.txt').read_bytes()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    try:
        source.write_bytes(original)
        run('dotnet', 'build', project, '--verbosity', 'quiet')
        behavior = run('dotnet', 'run', '--project', project, '--no-build')
        # Probe only the documented package workflow, using real project context.
        formatter = runpy.run_path(str(ROOT / 'build/format.py'))
        with tempfile.TemporaryDirectory(prefix='schedulelib-package-probe-') as directory:
            mirror = Path(directory)
            copied = mirror / selected
            copied.parent.mkdir(parents=True)
            copied.write_bytes(original)
            formatter['package_style'](mirror, FIXTURE / 'Formatting.csproj', [source])
            package_only = copied.read_bytes()
        (OUTPUT / 'package-only.cs.txt').write_bytes(package_only)
        package_text = package_only.decode()
        assert 'public int A { get; init; }' in package_text, 'A2 compaction'
        assert 'Keep(new Payload' in package_text, 'A1 constructor adjacency'
        assert '\n            && second' in package_text, 'A3 leading Boolean'
        assert 'Multiline(int value)\n    {' in package_text, 'A12 block conversion'
        assert 'if (Both(true, true))\n        {' not in package_text, 'Reassess package braces coverage'
        assert '\n            2,\n        ];' not in package_text, 'Reassess package trailing-comma coverage'
        assert 'Sum(Step(3), Step(4), Step(5))' in package_text, 'Reassess package wrapping coverage'
        assert '0123456789", "second-width-probe' in package_text, 'Reassess package width wrapping'
        assert 'trace = trace * 10 + value; return value;' in package_text, 'Reassess package statement layout'
        print('Package-only: A1/A2/A3/A12 covered; braces, trailing commas, statement splitting and argument wrapping still need CleanupCode.', flush=True)

        wrapper = ('build/format.sh', '--project', project, '--path', selected, '--style')
        run(*wrapper, '--check', expected=1)
        assert source.read_bytes() == original, 'Check wrote source'
        run(*wrapper)
        fixed = source.read_bytes()
        (OUTPUT / 'combined.cs.txt').write_bytes(fixed)
        text = fixed.decode()
        assert 'public int A { get; init; }' in text
        assert 'Keep(new Payload' in text
        assert '\n            A = Step(1),' in text
        assert 'if (Both(true, true))\n        {' in text, 'Braces'
        assert '\n            2,\n        ];' in text, 'Trailing comma'
        assert 'trace = trace * 10 + value;\n' in text, 'Statement layout'
        assert 'Sum(Step(3),\n            Step(4),' in text, 'Argument-count wrapping'
        assert '0123456789",\n' in text, 'Width wrapping'
        assert 'return Sum(\n            1,\n            2,\n            3);' in text, 'New block argument indentation'
        assert 'var text = "{ braces } // text, &&";' in text
        assert '// The literal and this comment must survive verbatim.' in text
        assert '/* Keep this block comment and both literal tokens. */' in text
        assert 'Console.WriteLine(text + ".lock");' in text, 'Unrelated CR1100 fix applied'
        assert (FIXTURE / 'Input.cs.txt').read_bytes() == original, 'Out-of-scope input changed'
        run('dotnet', 'build', project, '--no-restore', '--verbosity', 'quiet')
        assert run('dotnet', 'run', '--project', project, '--no-build') == behavior, 'Runtime behavior changed'
        run(*wrapper, '--check')
        run(*wrapper)
        assert source.read_bytes() == fixed, 'Second pass changed bytes'
        assert fixed == (FIXTURE / 'Expected.cs.txt').read_bytes(), 'Golden output differs; review tmp/formatting-verification/combined.cs.txt'
        assert package_only == (FIXTURE / 'PackageOnly.cs.txt').read_bytes(), 'Package-only golden differs'
        print('PASS: scoped apply/check, comments/literal and runtime preservation, unrelated-fix exclusion, '
              'retained CleanupCode coverage and byte-identical second pass.', flush=True)
    finally:
        source.unlink()


if __name__ == '__main__':
    main()
