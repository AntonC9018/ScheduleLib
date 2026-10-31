#!/usr/bin/env python3
"""Deduplicate MSBuild diagnostics (including its repeated summary) by source site."""
import argparse
from collections import Counter
import json
from pathlib import Path
import re


def inventory(logs, root):
    pattern = re.compile(r'^(.*?)(?:\((\d+),(\d+)\))?\s*:\s*(warning|error)\s+(\w+):\s*(.*?)(?:\s*\[([^\]]+\.(?:csproj|fsproj|vbproj|sln|slnx|proj))\])?\s*$')
    findings = {}
    for log in logs:
        for line in log.read_text(errors='replace').splitlines():
            match = pattern.match(line)
            if not match:
                continue
            path, row, column, severity, rule, message, project = match.groups()
            if project is None:
                project = path if path.endswith(('.csproj', '.fsproj', '.vbproj')) else ''
            path = path.removeprefix(root.rstrip('/') + '/')
            project = project.removeprefix(root.rstrip('/') + '/')
            item = dict(path=path, line=int(row or 0), column=int(column or 0),
                        severity=severity, rule=rule, message=message, project=project)
            # Shared source compiled into several projects is one source finding.
            key = (path, row, column, severity, rule, message)
            findings[key] = item
    return sorted(findings.values(), key=lambda x: (x['rule'], x['path'], x['line'], x['column']))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('logs', nargs='+', type=Path)
    parser.add_argument('--root', required=True)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    findings = inventory(args.logs, args.root)
    counts = dict(sorted(Counter(item['rule'] for item in findings).items()))
    args.output.write_text(json.dumps(dict(counts=counts, findings=findings), indent=2) + '\n')
    print(json.dumps(dict(total=len(findings), counts=counts), indent=2))


if __name__ == '__main__':
    main()
