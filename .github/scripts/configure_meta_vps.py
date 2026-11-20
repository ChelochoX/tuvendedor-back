#!/usr/bin/env python3
"""Apply Meta tokens from GitHub Actions secrets to the VPS Compose via SSH stdin.

No access tokens are stored in the repository or passed via SSH command arguments.
"""

import json
import os
import re
import shlex
import subprocess
import sys


REMOTE_CODE = r'''
import json
import os
import pathlib
import re
import shutil
import subprocess
from datetime import datetime

config = pathlib.Path('/opt/Ecomerce/docker-compose-ecommerce.yml')
if not config.is_file():
    raise SystemExit('ERROR: Compose no encontrado en /opt/Ecomerce')

payload = json.load(__import__('sys').stdin)
values = {
    'MetaPublishing__FacebookPageToken': payload['facebook'],
    'MetaPublishing__InstagramToken': payload['instagram'],
    'MetaPublishing__Enabled': 'true',
}

for key in ('MetaPublishing__FacebookPageToken', 'MetaPublishing__InstagramToken'):
    if not values[key] or values[key].startswith(('PEGAR_', 'COLOCAR_', 'TOKEN_')):
        raise SystemExit('ERROR: falta un token Meta válido')
    if '\n' in values[key] or '\r' in values[key]:
        raise SystemExit('ERROR: token con saltos de línea')

content = config.read_text(encoding='utf-8')
original = content
for key, value in values.items():
    pattern = re.compile(r'(?m)^(\s*-\s*' + re.escape(key) + r'=)[^\r\n]*$')
    if len(pattern.findall(content)) != 1:
        raise SystemExit('ERROR: la variable ' + key + ' no aparece exactamente una vez en Compose')
    content = pattern.sub(lambda match: match.group(1) + value, content)

# Validate the candidate config before changing the live file.
backup = config.with_name(config.name + '.bak-meta-' + datetime.now().strftime('%Y%m%d_%H%M%S'))
shutil.copy2(config, backup)
os.chmod(backup, 0o600)
try:
    config.write_text(content, encoding='utf-8')
    os.chmod(config, 0o600)
    subprocess.run(['docker', 'compose', '-f', str(config), 'config', '--quiet'], cwd=str(config.parent), check=True)
except Exception:
    shutil.copy2(backup, config)
    os.chmod(config, 0o600)
    raise
print('OK: credenciales Meta cargadas y Compose validado (sin mostrar secretos)')
'''


def main():
    host = os.environ.get('VPS_SSH_HOST', '').strip()
    user = os.environ.get('VPS_SSH_USER', '').strip()
    port = os.environ.get('VPS_SSH_PORT', '').strip() or '22'
    fb = os.environ.get('META_FACEBOOK_PAGE_TOKEN', '').strip()
    ig = os.environ.get('META_INSTAGRAM_TOKEN', '').strip()

    if not host or not user or not port.isdigit():
        sys.exit('ERROR: falta configuración de conexión SSH')
    if not 1 <= int(port) <= 65535:
        sys.exit('ERROR: puerto SSH inválido')
    if not fb or not ig or fb.startswith(('PEGAR_', 'COLOCAR_', 'TOKEN_')) or ig.startswith(('PEGAR_', 'COLOCAR_', 'TOKEN_')):
        sys.exit('ERROR: configurá los Secrets META_FACEBOOK_PAGE_TOKEN y META_INSTAGRAM_TOKEN en GitHub Environment')

    payload = json.dumps({'facebook': fb, 'instagram': ig}, ensure_ascii=True)
    command = [
        'ssh', '-p', port, '-o', 'BatchMode=yes',
        '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=15',
        f'{user}@{host}', 'python3 -c ' + shlex.quote(REMOTE_CODE),
    ]
    subprocess.run(command, input=payload, text=True, check=True)


if __name__ == '__main__':
    main()
