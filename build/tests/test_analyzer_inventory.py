import importlib.util
from pathlib import Path
import tempfile
import unittest


spec = importlib.util.spec_from_file_location(
    'analyzer_inventory', Path(__file__).resolve().parents[1] / 'analyzer-inventory.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class AnalyzerInventoryTests(unittest.TestCase):
    def inventory(self, content):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'build.log'
            log.write_text(content)
            return module.inventory([log], '/repo')

    def test_advisories_with_and_without_project_suffix_are_one_finding(self):
        findings = self.inventory(
            '/repo/A.csproj : warning NU1903: Package has a vulnerability\n'
            '/repo/A.csproj : warning NU1903: Package has a vulnerability [/repo/All.sln]\n'
            '/repo/A.csproj : warning NU1903: Package has a vulnerability [/repo/A.csproj]\n')
        self.assertEqual(1, len(findings))
        self.assertEqual('A.csproj', findings[0]['path'])
        self.assertEqual('A.csproj', findings[0]['project'])
        self.assertEqual('Package has a vulnerability', findings[0]['message'])

    def test_shared_source_summary_is_deduplicated_but_locations_remain_distinct(self):
        findings = self.inventory(
            '/repo/A.cs(4,5): warning CR0300: Calculate [value] [/repo/One.csproj]\n'
            '/repo/A.cs(4,5): warning CR0300: Calculate [value] [/repo/Two.csproj]\n'
            '/repo/A.cs(4,6): error CR0300: Calculate [value] [/repo/One.csproj]\n')
        self.assertEqual(2, len(findings))
        self.assertEqual([5, 6], [item['column'] for item in findings])
        self.assertEqual(['warning', 'error'], [item['severity'] for item in findings])
        self.assertEqual(['Calculate [value]'] * 2, [item['message'] for item in findings])
