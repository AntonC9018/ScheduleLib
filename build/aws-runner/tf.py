#!/usr/bin/env python3
"""Use short-lived AWS CLI login credentials without persisting or printing them."""
import json
import os
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parent
credentials = json.loads(subprocess.check_output([
    'aws', 'configure', 'export-credentials', '--format', 'process'
]))
env = dict(os.environ, AWS_ACCESS_KEY_ID=credentials['AccessKeyId'],
           AWS_SECRET_ACCESS_KEY=credentials['SecretAccessKey'],
           AWS_SESSION_TOKEN=credentials['SessionToken'])
raise SystemExit(subprocess.call(['terraform', f'-chdir={root}', *sys.argv[1:]], env=env))
