#!/usr/bin/env python3
"""Generate the missing .help pages for [AssimpImport]/[AssimpExport].
Replicates ExportWikiDocumentation.cs:WriteDocFile, driven by the .t3/.t3ui sources."""
import json, re, os
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HELP = os.path.join(ROOT, '.help', 'docs', 'operators')

def strip(p):
    return re.sub(r'/\*[^*]*\*/', '', open(p, encoding='utf-8').read())

def load_op(base, ns):
    ui = json.loads(strip(base + '.t3ui'))
    sym = json.loads(strip(base + '.t3'))
    cs = open(base + '.cs', encoding='utf-8').read()
    names = {g.lower(): (n, t) for g, t, n in re.findall(
        r'Input\(Guid = "([0-9a-fA-F-]+)"[^\]]*\]\s*\n\s*public readonly InputSlot<([^>]+)>\s*(\w+)', cs)}
    if len(names) != len(sym['Inputs']):
        raise SystemExit(f'input name mismatch in {base}: {len(names)} vs {len(sym["Inputs"])}')
    inputs = []
    for idef in sym['Inputs']:
        g = idef['Id'].lower()
        nm, cs_type = names[g]
        tn = type(idef.get('DefaultValue')).__name__
        tn = {'bool': 'Boolean', 'int': 'Int32', 'float': 'Single', 'str': 'String'}.get(tn, tn)
        if tn == 'NoneType' and cs_type:
            tn = cs_type.split('.')[-1]
        u = next(u for u in ui['InputUis'] if u['InputId'].lower() == g)
        rel = u.get('Relevancy') or ''
        inputs.append((nm, tn, rel, (u.get('Description') or '—').replace('\n', '<br/>')))
    desc = ui.get('Description', '')
    return {'ns': ns, 'name': os.path.basename(base), 'desc': desc, 'inputs': inputs}

ASSIMP_IMPORT_OUTPUTS = [
    ('MeshData', 'T3.Core.DataTypes.MeshBuffers'),
    ('Geometry', 'T3.Core.DataTypes.MeshGeometry'),
    ('Points', 'T3.Core.DataTypes.BufferWithViews'),
    ('Setup', 'T3.Core.DataTypes.SceneSetup'),
    ('Material', 'T3.Core.Rendering.Material.PbrMaterial'),
    ('SkinWeights', 'T3.Core.DataTypes.BufferWithViews'),
    ('SkeletonPoints', 'T3.Core.DataTypes.BufferWithViews'),
    ('Metadata', 'System.String'),
    ('VertexCount', 'System.Int32'),
    ('FaceCount', 'System.Int32'),
    ('MeshCount', 'System.Int32'),
    ('BoundsMin', 'System.Numerics.Vector3'),
    ('BoundsMax', 'System.Numerics.Vector3'),
    ('MeshNames', 'System.String'),
    ('BoneCount', 'System.Int32'),
    ('AnimationCount', 'System.Int32'),
    ('AnimationNames', 'System.String'),
]
ASSIMP_EXPORT_OUTPUTS = [
    ('Success', 'System.Boolean'),
    ('StatusMessage', 'System.String'),
    ('LastFilePath', 'System.String'),
    ('SupportedFormats', 'System.String'),
]

def write_doc_file(op, outputs, dirpath):
    s = []
    s += ['# ' + op['name'], '', '*in [' + op['ns'] + '](README.md)*', '']
    s += [op['desc'], '']
    s += ['## Input Parameters', '| Name (Relevancy & Type) | Description |', '|---|---|']
    for nm, tn, rel, d in op['inputs']:
        tag = ' ' + rel if rel else ''
        s.append('| **%s** (%s%s) | %s |' % (nm, tn, tag, d))
    s += ['', '## Outputs', '| Name | Type |', '|---|---|']
    for nm, t in outputs:
        s.append('| **%s** | %s |' % (nm, t))
    s.append('')
    open(os.path.join(dirpath, op['name'] + '.md'), 'w', encoding='utf-8', newline='\n').write('\n'.join(s) + '\n')

def summary(op):
    return op['desc'].split('\n')[0].strip()

def main():
    imp = load_op('Operators/Lib/Symbols/mesh/generate/AssimpImport', 'Lib.mesh.generate')
    exp = load_op('Operators/Lib/Symbols/io/file/AssimpExport', 'Lib.io.file')
    write_doc_file(imp, ASSIMP_IMPORT_OUTPUTS, os.path.join(HELP, 'lib', 'mesh', 'generate'))
    write_doc_file(exp, ASSIMP_EXPORT_OUTPUTS, os.path.join(HELP, 'lib', 'io', 'file'))
    return imp, exp

def update_readme(dirpath, op):
    path = os.path.join(dirpath, 'README.md')
    lines = open(path, encoding='utf-8').read().splitlines()
    entry = '- [**%s**](%s.md) — %s' % (op['name'], op['name'], summary(op))
    if entry in lines:
        return
    def summary_name(l):
        return l.split('[**')[1].split('**]')[0]
    op_lines = [(i, summary_name(l)) for i, l in enumerate(lines) if l.startswith('- [**')]
    insert_at = op_lines[-1][0] + 1
    for i, nm in op_lines:
        if nm > op['name']:
            insert_at = i
            break
    lines.insert(insert_at, entry)
    open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')

def update_index(ops):
    path = os.path.join(HELP, 'index.json')
    d = json.load(open(path, encoding='utf-8'))
    for op in ops:
        url = '/operators/' + op['ns'].lower().replace('.', '/') + '/' + op['name'] + '/'
        fp = op['ns'] + '.' + op['name']
        d['by_fullpath'][fp] = {'url': url, 'summary': summary(op)}
        d['by_shortname'].setdefault(op['name'], []).append(fp)
    d['by_fullpath'] = dict(sorted(d['by_fullpath'].items()))
    d['by_shortname'] = dict(sorted(d['by_shortname'].items()))
    open(path, 'w', encoding='utf-8', newline='\n').write(json.dumps(d, indent=2, ensure_ascii=False) + '\n')

if __name__ == '__main__':
    imp, exp = main()
    update_readme(os.path.join(HELP, 'lib', 'mesh', 'generate'), imp)
    update_readme(os.path.join(HELP, 'lib', 'io', 'file'), exp)
    update_index([imp, exp])
    print('pages written; summaries:')
    print(' ', imp['name'], '->', summary(imp))
    print(' ', exp['name'], '->', summary(exp))
