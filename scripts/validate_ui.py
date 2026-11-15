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

# Compare all action-command names from the actual Phase 1 baseline. Shared components
# intentionally move order commands out of the two wrapper views.
shared = ''.join(p.read_text() for p in (ROOT / 'Presentation/Components').glob('*.xaml'))
for path in (ROOT / 'Views').glob('*.xaml'):
    previous = subprocess.check_output(['git', 'show', f'f013ba5:Views/{path.name}'], cwd=ROOT, text=True)
    previous_commands = set(re.findall(r'\{Binding (?:DataContext\.)?(\w+Command)', previous))
    current_commands = set(re.findall(r'\{Binding (?:DataContext\.)?(\w+Command)', path.read_text() + shared))
    for command in previous_commands - current_commands:
        errors.append(f'{path.name}: missing Phase 1 action {command}')

shell = (ROOT / 'Views/MainWindow.xaml').read_text()
for destination in ['Dashboard', 'Menu', 'Tables', 'Parcel', 'Settings']:
    if shell.count('navigation:WorkspaceDestination.' + destination + '}') != 2:
        errors.append(f'MainWindow: command/selected trigger mismatch for {destination}')

# Schema, financial services, credentials, printing and storage are outside this UI phase.
protected = ['Models', 'Migrations', 'Data', 'Services/BillingService.cs', 'Services/OrderService.cs',
             'Services/ReceiptPrintService.cs', 'Services/ApplicationAccessService.cs',
             'Services/LicenseService.cs', 'Services/DatabaseBackupService.cs',
             'Infrastructure/Persistence', 'bin/Debug/net8.0-windows/Data/servex.db']
changed = subprocess.check_output(['git', 'diff', '--name-only', 'f013ba5', '--', *protected], cwd=ROOT, text=True)
if changed.strip():
    errors.append('Protected Phase 1 files changed: ' + changed.strip())
if errors:
    print('\n'.join(errors), file=sys.stderr)
    sys.exit(1)
print(f'PASS: {len(files)} XAML files parse; resource keys resolve in the inspected set; action and navigation contracts retained; protected files unchanged.')
print('Scope/order of resource lookup, runtime bindings, templates, accessibility and DPI still require Windows checks.')
