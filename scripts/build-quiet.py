#!/usr/bin/env python3
"""Run build and delivery targets with quiet, cancellable logging."""
from datetime import datetime, timezone
import os
from pathlib import Path
import re
import signal
import subprocess
import sys

root = Path(__file__).resolve().parent.parent
log_dir = root / 'artifacts/logs'
log_dir.mkdir(parents=True, exist_ok=True)
log_path = log_dir / (datetime.now(timezone.utc).strftime('build-%Y%m%dT%H%M%S-') + str(os.getpid()) + '.log')
child = None
step = 'arguments'


def cancel(signum, _frame):
    if child is not None and child.poll() is None:
        try:
            os.killpg(child.pid, signum)
        except ProcessLookupError:
            pass
        try:
            child.wait(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(child.pid, signal.SIGKILL)
            child.wait()
    raise SystemExit(128 + signum)


def run(name, args, log):
    global child, step
    step = name
    log.write((name + '\n').encode())
    log.flush()
    child = subprocess.Popen(args, cwd=root, stdout=log, stderr=log, start_new_session=True)
    status = child.wait()
    child = None
    if status:
        raise RuntimeError(f'{name} exited with status {status}')


def deploy_root(args):
    values = []
    expect_list = False
    for arg in args:
        match = re.match(r'^(?:-|/)(?:p|property)(?::|=)(.*)$|^--property(?::|=)(.*)$', arg, re.I)
        if expect_list or match:
            assignments = arg if expect_list else next(v for v in match.groups() if v is not None)
            expect_list = False
            for assignment in assignments.split(';'):
                name, separator, value = assignment.partition('=')
                if separator and name.upper() == 'RIMWORLD_MOD_DIR':
                    values.append(value)
        elif arg.lower() in ('-p', '/p', '--property', '-property', '/property'):
            expect_list = True
    if len(set(values)) > 1:
        raise ValueError('RIMWORLD_MOD_DIR has conflicting property values.')
    environment = os.environ.get('RIMWORLD_MOD_DIR', '')
    if values and environment and values[-1] != environment:
        raise ValueError('RIMWORLD_MOD_DIR differs between environment and property. Unset the environment for a local build.')
    return values[-1] if values else environment


for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
    signal.signal(sig, cancel)

try:
    with log_path.open('wb') as log:
        mods = deploy_root(sys.argv[1:])
        if mods:
            run('deployment preflight', [str(root / 'scripts/rimworld-deploy-guard.sh'), 'check-stopped'], log)
        run('delivery contracts', ['python3', '-B', 'Tests/test_delivery.py'], log)
        run('build, companion, package' + (', install verification' if mods else ''),
            ['dotnet', 'build', 'Source/Clouds.csproj', '--nologo', *sys.argv[1:]], log)
except (OSError, RuntimeError, ValueError) as error:
    print(f'Failed step: {step}: {error}', file=sys.stderr)
    lines = log_path.read_text(errors='replace').splitlines()
    diagnostics = [line for line in lines if re.search(r'\berror\b|failed|refus|exception', line, re.I)]
    print('\n'.join((diagnostics or lines)[-12:]), file=sys.stderr)
    print(f'Full log: {log_path}', file=sys.stderr)
    sys.exit(1)
except SystemExit:
    print(f'Cancelled step: {step}. Full log: {log_path}', file=sys.stderr)
    raise
print('ok')
