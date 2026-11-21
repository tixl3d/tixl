"""Reads the operator classes of a package: guid, symbol name and the name, guid and type of every input and output."""
import re, os, json, sys


def scan(root):
    ops = {}
    for dirpath, _, files in os.walk(root):
        for f in files:
            if not f.endswith('.cs'):
                continue
            entry = read_operator(os.path.join(dirpath, f), root)
            if entry:
                ops[entry['symbol'].split('.')[-1]] = entry
    return ops


cls_re = re.compile(r'\[Guid\("([0-9a-fA-F-]+)"\)\]\s*(?:\[[^\]]*\]\s*)*(?:public|internal)?\s*(?:sealed\s+)?(?:partial\s+)?class\s+(\w+)\s*:\s*Instance<')
slot_re = re.compile(r'\[(Input|Output)\(Guid\s*=\s*"([0-9a-fA-F-]+)"[^\]]*\]\s*public\s+readonly\s+([\w\.]+)<(.+?)>\s+(\w+)\s*=', re.S)


def read_operator(path, root):
    s = open(path, encoding='utf-8-sig', errors='replace').read()
    m = cls_re.search(s)
    if not m:
        return None
    ns = re.search(r'namespace\s+([\w\.@]+)', s)
    entry = {'guid': m.group(1), 'symbol': (ns.group(1).replace('@', '') if ns else '') + '.' + m.group(2), 'inputs': {}, 'outputs': {},
             'path': os.path.relpath(path, root)}
    for kind, guid, slot, typ, sname in slot_re.findall(s):
        (entry['inputs'] if kind == 'Input' else entry['outputs'])[sname] = {'guid': guid, 'type': typ.strip(), 'multi': slot.startswith('Multi')}
    return entry


if __name__ == '__main__':
    operators = scan(sys.argv[1])
    json.dump(operators, open(sys.argv[2], 'w'), indent=1)
    print(len(operators))
