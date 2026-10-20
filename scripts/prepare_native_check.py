"""Generate exact inline variants and reproduce 1.0.4's namespace wrapping.

The native Roslyn rewrite retains root usings and moves root members inside a
generated namespace. Our controlled sources have only simple column-zero root
usings; namespace-local usings are indented and must remain with their members.
"""
from pathlib import Path
import re
import build


def prepare():
    work = build.ROOT / '.framework-build'
    work.mkdir(exist_ok=True)
    for phase in ('fetch', 'save'):
        source = build.inline_source(phase == 'fetch').decode('utf-8').replace('\r\n', '\n')
        (work / (phase + '.cs')).write_text(source, encoding='utf-8')
        directives = re.findall(r'^#define [^\n]+$', source, re.MULTILINE)
        usings = re.findall(r'^using [^\n]+;$', source, re.MULTILINE)
        members = re.sub(r'^(?:#define [^\n]+|using [^\n]+;)\n', '', source, flags=re.MULTILINE)
        wrapped = '\n'.join(directives + usings) + '\nnamespace NativeImportCheck {\n' + members + '\n}\n'
        (work / (phase + '-wrapped.cs')).write_text(wrapped, encoding='utf-8')
    shim = (build.ROOT / 'tests/FrameworkCompatibility/ApiShim.cs').read_text()
    (work / 'wrapped-shim.cs').write_text('namespace NativeImportCheck {\n' + shim + '\n}\n', encoding='utf-8')


if __name__ == '__main__':
    prepare()
