#!/usr/bin/env python3
"""Static UI contract checks; these do not prove WPF compilation or visual correctness."""
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
files = [ROOT / 'App.xaml', *sorted((ROOT / 'Styles').glob('*.xaml')),
         *sorted((ROOT / 'Views').glob('*.xaml')),
         *sorted((ROOT / 'Presentation/Components').glob('*.xaml'))]
errors = []
keys = set()
texts = {}
for path in files:
    text = path.read_text()
    texts[path] = text
    try:
        tree = ET.fromstring(text)
    except ET.ParseError as error:
        errors.append(f'{path.relative_to(ROOT)}: {error}')
        continue
    local_keys = set()
    for element in tree.iter():
        key = element.get(X + 'Key')
        if key:
            if key in local_keys:
                errors.append(f'{path.name}: duplicate resource key {key}')
            local_keys.add(key)
            keys.add(key)
    if path.parent.name == 'Styles' and re.search(r'Georgia|#934F37|#F4EEE8', text):
        errors.append(f'{path.name}: old visual identity remains')
for path, text in texts.items():
    for reference in re.findall(r'\{(?:StaticResource|DynamicResource)\s+([^}\s]+)\}', text):
        if reference not in keys:
            errors.append(f'{path.name}: unresolved resource {reference}')

baseline_design = subprocess.check_output(['git', 'show', '24a981628429969dbecff5aeaae91255bcdb4561:Styles/DesignSystem.xaml'], cwd=ROOT, text=True)
legacy_keys = set(re.findall(r'x:Key="([^"]+)"', baseline_design))
if legacy_keys - keys:
    errors.append('Missing legacy design resources: ' + ', '.join(sorted(legacy_keys - keys)))
for path, text in texts.items():
    if re.search(r'<HeaderedContentControl[^>]+Content="\{Binding[^\"]+StringFormat=', text):
        errors.append(f'{path.name}: use ContentStringFormat for content-control formatting')

# Compare all action-command names from the actual Phase 1 baseline. Shared components
# intentionally move order commands out of the two wrapper views.
shared = ''.join(p.read_text() for p in (ROOT / 'Presentation/Components').glob('*.xaml'))
baseline_views = set(subprocess.check_output(
    ['git', 'ls-tree', '-r', '--name-only', '24a981628429969dbecff5aeaae91255bcdb4561', '--', 'Views'], cwd=ROOT, text=True).splitlines())
for path in (ROOT / 'Views').glob('*.xaml'):
    relative = f'Views/{path.name}'
    if relative not in baseline_views:
        continue
    previous = subprocess.check_output(['git', 'show', f'24a981628429969dbecff5aeaae91255bcdb4561:{relative}'], cwd=ROOT, text=True)
    previous_commands = set(re.findall(r'\{Binding (?:DataContext\.)?(\w+Command)', previous))
    current_commands = set(re.findall(r'\{Binding (?:DataContext\.)?(\w+Command)', path.read_text() + shared))
    for command in previous_commands - current_commands:
        errors.append(f'{path.name}: missing Phase 1 action {command}')

shell = (ROOT / 'Views/MainWindow.xaml').read_text()
for destination in ['Dashboard', 'Menu', 'Tables', 'Parcel', 'Settings']:
    if shell.count('navigation:WorkspaceDestination.' + destination + '}') != 2:
        errors.append(f'MainWindow: command/selected trigger mismatch for {destination}')

# Verify the Phase 2 baseline did not change Phase 1 business logic. Later checklist work can extend it.
protected = ['Models', 'Migrations', 'Data', 'Services/BillingService.cs', 'Services/OrderService.cs',
             'Services/ReceiptPrintService.cs', 'Services/ApplicationAccessService.cs',
             'Services/LicenseService.cs', 'Services/DatabaseBackupService.cs',
             'Infrastructure/Persistence']
changed = subprocess.check_output(['git', 'diff', '--name-only', '24a981628429969dbecff5aeaae91255bcdb4561', '8ab85f761e26142d72c2ecb3fedb705c82a431af', '--', *protected], cwd=ROOT, text=True)
if changed.strip():
    errors.append('Protected Phase 1 files changed: ' + changed.strip())
tracked_local_data = subprocess.check_output(
    ['git', 'ls-files', '--', '*.db', '*.db-wal', '*.db-shm', '*.log', '*.log.1'],
    cwd=ROOT, text=True)
if tracked_local_data.strip():
    errors.append('Local databases or logs are tracked: ' + tracked_local_data.strip())
if errors:
    print('\n'.join(errors), file=sys.stderr)
    sys.exit(1)
print(f'PASS: {len(files)} XAML files parse; resource keys resolve in the inspected set; action and navigation contracts retained; Phase 2 baseline business logic unchanged; local data files are untracked.')
print('Scope/order of resource lookup, runtime bindings, templates, accessibility and DPI still require Windows checks.')
