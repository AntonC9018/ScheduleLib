#!/usr/bin/env python3
"""Run the restricted JetBrains formatting profile on selected C# source copies."""
import argparse
import fnmatch
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import quoteattr

ROOT = Path(__file__).resolve().parent.parent


def selected_files(target, paths):
    if target.suffix == '.csproj':
        projects = [target]
    elif target.suffix == '.sln':
        projects = [target.parent / value.replace('\\', '/') for value in
                    re.findall(r'^Project\([^\n]+?\) = "[^\n]+?", "([^"\n]+\.csproj)"',
                               target.read_text(), re.MULTILINE)]
    else:
        raise ValueError('Expected a .csproj or .sln target.')
    sources = set()
    for project in projects:
        result = subprocess.run(['dotnet', 'msbuild', str(project), '-getItem:Compile', '-nologo'],
                                cwd=ROOT, check=True, capture_output=True, text=True)
        for item in json.loads(result.stdout)['Items']['Compile']:
            source = Path(item['FullPath']).resolve()
            if source.suffix == '.cs' and source.is_file() and source.is_relative_to(ROOT):
                relative = source.relative_to(ROOT).as_posix()
                if not paths or any(matches(relative, path) for path in paths):
                    sources.add(source)
    if not sources:
        raise ValueError('No compiled C# files match the selected target and paths.')
    return sorted(sources)


def matches(relative, pattern):
    path = ROOT / pattern
    if path.is_dir():
        return Path(relative).is_relative_to(Path(pattern))
    if path.is_file():
        return relative == Path(pattern).as_posix()
    return fnmatch.fnmatchcase(relative, pattern)


def format_sources(sources, check):
    with tempfile.TemporaryDirectory(prefix='schedulelib-format-') as directory:
        mirror = Path(directory)
        original = {source: source.read_bytes() for source in sources}
        for source, content in original.items():
            destination = mirror / source.relative_to(ROOT)
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(content)
            for parent in [source.parent, *source.parent.parents]:
                if not parent.is_relative_to(ROOT):
                    break
                config = parent / '.editorconfig'
                if config.is_file():
                    copied = mirror / config.relative_to(ROOT)
                    copied.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(config, copied)
        # CleanupCode needs a solution for braces and commas. This formatting-only
        # project contains no repository dependencies, analyzers, or build outputs.
        (mirror / 'Formatting.csproj').write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<TargetFramework>netstandard2.1</TargetFramework><LangVersion>preview</LangVersion>'
            '<EnableNETAnalyzers>false</EnableNETAnalyzers>'
            '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' +
            ''.join('<Compile Include=' + quoteattr(source.relative_to(ROOT).as_posix()) + ' />'
                    for source in sources) + '</ItemGroup></Project>')
        solution = mirror / 'Formatting.sln'
        solution.write_text(
            'Microsoft Visual Studio Solution File, Format Version 12.00\n'
            'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Formatting", '
            '"Formatting.csproj", "{E483790C-2AE6-4F6B-9202-65E4AA7072F9}"\nEndProject\nGlobal\nEndGlobal\n')
        subprocess.run(['dotnet', 'jb', 'cleanupcode', str(solution),
                        '--profile=ScheduleLib Formatting', '--settings=' + str(ROOT / 'All.sln.DotSettings'),
                        '--no-buildin-settings', '--no-build', '--no-updates', '--verbosity=ERROR'],
                       cwd=ROOT, check=True)
        changed = {source: (mirror / source.relative_to(ROOT)).read_bytes() for source in sources
                   if (mirror / source.relative_to(ROOT)).read_bytes() != original[source]}
        if not check:
            for source in changed:
                if source.read_bytes() != original[source]:
                    raise ValueError(f'File changed during formatting: {source.relative_to(ROOT)}')
            for source, content in changed.items():
                if source.read_bytes() != original[source]:
                    raise ValueError(f'File changed during formatting: {source.relative_to(ROOT)}')
                source.write_bytes(content)
        for source in changed:
            print(('Would format: ' if check else 'Formatted: ') + source.relative_to(ROOT).as_posix())
        print(f'Formatting: {len(sources)} files checked, {len(changed)} changes.')
        return 1 if check and changed else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', default='All.sln', help='Repository-relative .csproj or .sln.')
    parser.add_argument('--path', action='append', default=[], help='Repository-relative file, directory or glob.')
    parser.add_argument('--check', action='store_true', help='Verify without writing source.')
    parser.add_argument('--yes', action='store_true', help='Allow an unscoped solution rewrite.')
    args = parser.parse_args()
    if not args.check and not args.yes and not args.path and args.project == 'All.sln':
        parser.error('Scope the rewrite with --project or --path, or explicitly pass --yes.')
    try:
        target = (ROOT / args.project).resolve()
        if not target.is_relative_to(ROOT):
            raise ValueError('The target must be inside the repository.')
        sources = selected_files(target, args.path)
        subprocess.run(['dotnet', 'tool', 'restore', '--verbosity', 'quiet'], cwd=ROOT, check=True)
        return format_sources(sources, args.check)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f'Formatting failed: {error}')
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
