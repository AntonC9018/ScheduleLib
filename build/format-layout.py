#!/usr/bin/env python3
"""Apply/check JetBrains layout in an isolated copy of selected repository C# files."""
import argparse
import fnmatch
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent


def projects(target):
    path = ROOT / target
    if path.suffix == '.csproj':
        return [path]
    if path.suffix == '.sln':
        return [path.parent / project.replace('\\', '/')
                for project in re.findall(r'^Project\([^\n]+?\) = "[^\n]+?", "([^"\n]+\.csproj)"',
                                          path.read_text(), re.MULTILINE)]
    raise ValueError('Layout formatting expects a .csproj or .sln target.')


def selected_files(target, paths):
    selected = set()
    for project in projects(target):
        result = subprocess.run(['dotnet', 'msbuild', str(project), '-getItem:Compile', '-nologo'],
                                cwd=ROOT, check=True, capture_output=True, text=True)
        for item in json.loads(result.stdout)['Items']['Compile']:
            source = Path(item['FullPath']).resolve()
            if source.suffix != '.cs' or not source.is_file() or not source.is_relative_to(ROOT):
                continue
            relative = source.relative_to(ROOT).as_posix()
            if paths and not any(matches(relative, path) for path in paths):
                continue
            selected.add(source)
    return sorted(selected)


def matches(relative, pattern):
    path = ROOT / pattern
    if path.is_dir():
        return Path(relative).is_relative_to(Path(pattern))
    if path.is_file():
        return relative == Path(pattern).as_posix()
    return fnmatch.fnmatchcase(relative, pattern)


def config_matches(relative, pattern):
    braces = re.search(r'\{([^{}]+)\}', pattern)
    if braces:
        return any(config_matches(relative, pattern[:braces.start()] + value + pattern[braces.end():])
                   for value in braces.group(1).split(','))
    candidate = relative if '/' in pattern else Path(relative).name
    return fnmatch.fnmatchcase(candidate, pattern)


def comma_preference(source):
    value = None
    parents = [source.parent, *source.parent.parents]
    for parent in reversed([parent for parent in parents if parent.is_relative_to(ROOT)]):
        config = parent / '.editorconfig'
        if not config.is_file():
            continue
        section = '*'
        for line in config.read_text().splitlines():
            line = line.strip()
            if line.startswith('[') and line.endswith(']'):
                section = line[1:-1]
            elif '=' in line and not line.startswith(('#', ';')):
                key, configured = (part.strip() for part in line.split('=', 1))
                relative = source.relative_to(parent).as_posix()
                if key == 'resharper_trailing_comma_in_multiline_lists' and config_matches(relative, section):
                    value = configured.lower()
    return value


def format_layout(target, paths, check):
    sources = selected_files(target, paths)
    if not sources:
        raise ValueError('No compiled C# files match the selected target and paths.')
    # Copy source and EditorConfig only. Neither credentials nor build outputs are mirrored.
    with tempfile.TemporaryDirectory(prefix='schedulelib-format-') as directory:
        mirror = Path(directory)
        original = {}
        for source in sources:
            relative = source.relative_to(ROOT)
            destination = mirror / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            original[source] = source.read_bytes()
            destination.write_bytes(original[source])
            for parent in [source.parent, *source.parent.parents]:
                if not parent.is_relative_to(ROOT):
                    break
                config = parent / '.editorconfig'
                if config.is_file():
                    copied = mirror / config.relative_to(ROOT)
                    copied.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(config, copied)
        # File mode performs formatting only: no semantic cleanup or analyzer fixes.
        destinations = [str(mirror / source.relative_to(ROOT)) for source in sources]
        for attempt in range(8):
            before = [Path(destination).read_bytes() for destination in destinations]
            for start in range(0, len(destinations), 48):
                subprocess.run(['dotnet', 'tool', 'run', 'jb', 'cleanupcode',
                                *destinations[start:start + 48],
                                '--settings=' + str(ROOT / 'All.sln.DotSettings'),
                                '--no-buildin-settings', '--no-updates', '--verbosity=ERROR'],
                               cwd=ROOT, check=True)
            # Commas are syntax style, not part of JetBrains' standalone file formatter.
            # A Roslyn syntax rewriter handles only this explicit EditorConfig preference.
            for preference, option in [('true', '--add'), ('false', '--remove')]:
                comma_files = [str(mirror / source.relative_to(ROOT)) for source in sources
                               if comma_preference(source) == preference]
                if comma_files:
                    subprocess.run(['dotnet', 'run', '--project', str(ROOT / 'build/Formatting/Formatting.csproj'),
                                    '--verbosity', 'quiet', '--', option, *comma_files], cwd=ROOT, check=True)
            after = [Path(destination).read_bytes() for destination in destinations]
            if before == after:
                break
        else:
            raise ValueError('Layout and comma formatting did not settle after eight passes.')
        changed = []
        for source in sources:
            formatted = (mirror / source.relative_to(ROOT)).read_bytes()
            if formatted == original[source]:
                continue
            changed.append(source.relative_to(ROOT).as_posix())
            if not check:
                if source.read_bytes() != original[source]:
                    raise ValueError(f'File changed during formatting: {source.relative_to(ROOT)}')
                source.write_bytes(formatted)
        for relative in changed:
            print(('Would format: ' if check else 'Formatted: ') + relative)
        print(f'Layout: {len(sources)} files checked, {len(changed)} changes.')
        return 1 if check and changed else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', required=True)
    parser.add_argument('--path', action='append', default=[])
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    try:
        return format_layout(args.target, args.path, args.check)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f'Layout formatting failed: {error}')
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
