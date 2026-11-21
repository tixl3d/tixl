"""Builds TiXL example symbols (.cs/.t3/.t3ui) from a compact scene description.

Operator slots are looked up by name in ops.json (extracted from the operator sources), so a typo or a type mismatch
fails here instead of in the editor.
"""
import json, uuid, os

import opmeta

# Filled by load_operators() before any scene is built.
OPS = {}


# Operators from the TypeOperators package that the scenes use; the scan only covers Lib.
TYPE_OPERATORS = {
    'ComputeShader': {'guid': 'a256d70f-adb3-481d-a926-caf35bd3e64c', 'symbol': 'Types.Gfx.ComputeShader',
                      'inputs': {'Source': {'guid': 'AFB69C81-5063-4CB9-9D42-841B994B5EC0', 'type': 'string', 'multi': False},
                                 'EntryPoint': {'guid': '8AD9E58D-A767-4A5F-BFBF-D082B80901D6', 'type': 'string', 'multi': False}},
                      'outputs': {'Shader': {'guid': '6C118567-8827-4422-86CC-4D4D00762D87', 'type': 'ComputeShaderT3', 'multi': False}}},
}


def load_operators(repository_root):
    OPS.clear()
    OPS.update(opmeta.scan(os.path.join(repository_root, 'Operators', 'Lib', 'Symbols')))
    OPS.update(TYPE_OPERATORS)
NS = uuid.UUID('3b7c1d52-8e4a-4f6b-9c2d-71a5e0f4b913')

COL = 260.0
ROW = 110.0

TYPE_NAMES = {
    'float': 'System.Single', 'int': 'System.Int32', 'bool': 'System.Boolean', 'string': 'System.String',
    'Vector2': 'System.Numerics.Vector2', 'Vector3': 'System.Numerics.Vector3', 'Vector4': 'System.Numerics.Vector4',
    'Int2': 'T3.Core.DataTypes.Vector.Int2', 'Int3': 'T3.Core.DataTypes.Vector.Int3',
}


def short(t):
    t = t.strip().rstrip('?')
    if t.startswith('System.Numerics.'):
        t = t[len('System.Numerics.'):]
    t = t.split('.')[-1]
    return {'Single': 'float', 'Int32': 'int', 'Boolean': 'bool', 'String': 'string'}.get(t, t)


def convert(typ, value):
    t = short(typ)
    if t == 'float':
        return float(value)
    if t == 'int':
        return int(value)
    if t == 'bool':
        return bool(value)
    if t == 'string':
        return str(value)
    if t in ('Vector2', 'Vector3', 'Vector4'):
        keys = 'XYZW'[:int(t[-1])]
        assert len(value) == len(keys), (typ, value)
        return {k: float(v) for k, v in zip(keys, value)}
    if t in ('Int2', 'Int3'):
        keys = 'XYZ'[:int(t[-1])]
        assert len(value) == len(keys), (typ, value)
        return {k: int(v) for k, v in zip(keys, value)}
    raise ValueError(f'unsupported value type {typ}')


# Examples that replace an earlier graph of the same name keep its ids, so links to the symbol stay valid.
KEPT_IDS = {
    'PhysicsSdfFieldExample': ('ae28dc10-527b-5b7e-8f97-099aacc5aa79', '91f35899-6116-5702-b61b-d310de0bce90'),
    'PhysicsVolumeGravityExample': ('0f12b1a9-7c69-51d1-ab4a-ad4778d97aeb', '707067e2-92d8-53b7-b124-13a032211f4b'),
    'PhysicsToVolumeExample': ('faa1fe57-3dd8-5136-93c3-fe9bdbeccd4d', '6aa0bc05-b707-559a-8862-7ddc3b359fa0'),
    'VolumeCsgExample': ('379fc9b9-e255-5630-9254-3fad8b5cb8fa', 'b5c7f13e-5e01-5753-9fea-027da689e7fd'),
    'VolumeToPointsExample': ('91bb0f8b-593c-557c-9da6-15005722b733', '267e084f-5393-5b46-83b0-c8c4c901120c'),
    'VolumeShakingWaterExample': ('37449751-31a8-5ead-9525-88759fdfd1b0', 'e29b2d3c-8e83-55d0-a789-f484a390fffc'),
}


class Scene:
    def __init__(self, name, summary, description, folder='physics'):
        self.name = name
        self.summary = summary
        self.description = description
        self.folder = folder
        self.id, self.output_id = KEPT_IDS.get(name, (self.gid('symbol'), self.gid('output')))
        self.children = []
        self.child_uis = []
        self.ops = {}
        self.links = []
        self.sections = []
        self.tours = []
        self.output_source = None
        self.leaves = set()

    def gid(self, key):
        return str(uuid.uuid5(NS, f'{self.name}/{key}'))

    def op(self, key, opname, col, row, name=None, **values):
        assert key not in self.ops, key
        meta = OPS[opname]
        self.ops[key] = (opname, col, row)
        input_values = []
        for input_name, value in values.items():
            slot = meta['inputs'].get(input_name)
            assert slot, f'{opname} has no input {input_name}'
            typ = short(slot['type'])
            assert typ in TYPE_NAMES, f'{opname}.{input_name}: cannot set a {slot["type"]}'
            input_values.append({'Id': slot['guid'], 'Type': TYPE_NAMES[typ], 'Value': convert(typ, value)})
        child = {'Id': self.gid(key), 'SymbolId': meta['guid'], 'SymbolName': meta['symbol'], 'InputValues': input_values, 'Outputs': []}
        if name:
            child['Name'] = name
        self.children.append(child)
        self.child_uis.append({'ChildId': self.gid(key), 'SymbolName': meta['symbol'], 'Position': {'X': col * COL, 'Y': row * ROW}})
        return key

    def link(self, source, target):
        """source 'key.Output', target 'key.Input'."""
        src_key, src_slot = source.split('.')
        dst_key, dst_slot = target.split('.')
        src_meta = OPS[self.ops[src_key][0]]
        dst_meta = OPS[self.ops[dst_key][0]]
        out = src_meta['outputs'].get(src_slot)
        inp = dst_meta['inputs'].get(dst_slot)
        assert out, f'{src_key}: {self.ops[src_key][0]} has no output {src_slot}'
        assert inp, f'{dst_key}: {self.ops[dst_key][0]} has no input {dst_slot}'
        assert short(out['type']) == short(inp['type']), f'{source} ({out["type"]}) -> {target} ({inp["type"]})'
        if not inp['multi']:
            for existing in self.links:
                assert existing['TargetParentOrChildId'] != self.gid(dst_key) or existing['TargetSlotId'] != inp['guid'], f'{target} already connected'
        self.links.append({'SourceParentOrChildId': self.gid(src_key), 'SourceSlotId': out['guid'],
                           'TargetParentOrChildId': self.gid(dst_key), 'TargetSlotId': inp['guid']})

    def output(self, source):
        src_key, src_slot = source.split('.')
        out = OPS[self.ops[src_key][0]]['outputs'][src_slot]
        assert short(out['type']) == 'Texture2D'
        self.links.append({'SourceParentOrChildId': self.gid(src_key), 'SourceSlotId': out['guid'],
                           'TargetParentOrChildId': '00000000-0000-0000-0000-000000000000', 'TargetSlotId': self.output_id})
        self.output_source = src_key

    def leaf(self, key):
        """Marks an operator that is useful on its own, such as an info readout, so it may feed nothing."""
        self.leaves.add(key)

    def section(self, title, keys, color, label=''):
        cols = [self.ops[k][1] for k in keys]
        rows = [self.ops[k][2] for k in keys]
        pad = 40.0
        x0, y0 = min(cols) * COL - pad, min(rows) * ROW - pad - 30
        x1, y1 = max(cols) * COL + 200 + pad, max(rows) * ROW + 60 + pad
        r, g, b = color
        entry = {'Id': self.gid('section/' + title), 'Title': title, 'Color': {'X': r, 'Y': g, 'Z': b, 'W': 1.0},
                 'Position': {'X': x0, 'Y': y0}, 'Size': {'X': x1 - x0, 'Y': y1 - y0}}
        if label:
            entry['Label'] = label
        self.sections.append(entry)

    def tour(self, key, text, style='InfoFor', input_name=None):
        entry = {'Id': self.gid(f'tour/{len(self.tours)}'), 'ChildId': self.gid(key), 'Description': text, 'Style': style}
        if input_name:
            entry['InputId'] = OPS[self.ops[key][0]]['inputs'][input_name]['guid']
        self.tours.append(entry)

    def validate(self):
        assert self.output_source, 'the scene has no output'
        connected = {(l['TargetParentOrChildId'], l['TargetSlotId']) for l in self.links}
        sources = {l['SourceParentOrChildId'] for l in self.links}
        for key, (opname, _, _) in self.ops.items():
            assert self.gid(key) in sources or key == self.output_source or key in self.leaves, f'{key} ({opname}) feeds nothing'

    def write(self, root):
        self.validate()
        folder = os.path.join(root, 'Operators', 'Examples', 'Symbols', 'Lib', self.folder)
        os.makedirs(folder, exist_ok=True)
        t3 = {'FormatVersion': 3, 'Id': self.id, 'Inputs': [], 'Children': self.children, 'Connections': self.links}
        t3ui = {'FormatVersion': 3, 'Id': self.id, 'Description': self.description, 'InputUis': [], 'SymbolChildUis': self.child_uis,
                'OutputUis': [{'OutputId': self.output_id, 'Position': {'X': (max(c for _, c, _ in self.ops.values()) + 1) * COL, 'Y': 0.0}}]}
        if self.sections:
            t3ui['Sections'] = sorted(self.sections, key=lambda s: s['Id'])
        if self.tours:
            t3ui['TourPoints'] = self.tours
        with open(os.path.join(folder, self.name + '.t3'), 'w', newline='\n') as f:
            json.dump(t3, f, indent=2)
        with open(os.path.join(folder, self.name + '.t3ui'), 'w', newline='\n') as f:
            json.dump(t3ui, f, indent=2)
        with open(os.path.join(folder, self.name + '.cs'), 'w', newline='\n') as f:
            f.write(f'''namespace Examples.Lib.{self.folder};

/// <summary>
/// {self.summary}
/// </summary>
[Guid("{self.id}")]
internal sealed class {self.name} : Instance<{self.name}>
{{
    [Output(Guid = "{self.output_id}")]
    public readonly Slot<Texture2D> Output = new();
}}
''')
        return folder


# ---------------------------------------------------------------- shared building blocks

PALETTE = {
    'ground': (0.55, 0.56, 0.6, 1.0),
    'static': (0.72, 0.74, 0.78, 1.0),
    'orange': (0.95, 0.52, 0.22, 1.0),
    'teal': (0.18, 0.66, 0.68, 1.0),
    'yellow': (0.96, 0.78, 0.28, 1.0),
    'red': (0.86, 0.26, 0.24, 1.0),
    'blue': (0.28, 0.48, 0.9, 1.0),
    'violet': (0.62, 0.42, 0.88, 1.0),
    'white': (0.92, 0.92, 0.9, 1.0),
}

SECTION_COLORS = {
    'source': (0.35, 0.5, 0.65),
    'bodies': (0.65, 0.45, 0.25),
    'world': (0.6, 0.3, 0.35),
    'draw': (0.3, 0.55, 0.4),
    'render': (0.4, 0.4, 0.45),
}

SKY = (0.62, 0.68, 0.76, 1.0)


def ground(scene, key, col, row, size=(60.0, 0.5, 60.0), top=0.0, friction=0.7):
    return scene.op(key, 'RigidBody', col, row, name='Ground', Shape=0, Kind=1,
                    Position=(0.0, top - size[1] / 2, 0.0), Size=size, Friction=friction)


def draw_bodies(scene, key, world, bodies, col, row, color, name=None, show_colliders=False):
    """RigidBodyGeometry -> GeometryToMesh -> DrawMeshWithShadow for one or more bodies. Returns the draw key."""
    label = name or key
    values = {'ShowColliders': True} if show_colliders else {}
    scene.op(key + 'Geometry', 'RigidBodyGeometry', col, row, name=label + 'Geometry', **values)
    scene.op(key + 'Mesh', 'GeometryToMesh', col + 1, row, name=label + 'Mesh')
    scene.op(key + 'Draw', 'DrawMeshWithShadow', col + 2, row, name='Draw' + label, Color=color)
    scene.link(world + '.OutBuffer', key + 'Geometry.World')
    for body in ([bodies] if isinstance(bodies, str) else bodies):
        scene.link(body + '.OutBody', key + 'Geometry.Body')
    scene.link(key + 'Geometry.Result', key + 'Mesh.Geometry')
    scene.link(key + 'Mesh.Buffers', key + 'Draw.Mesh')
    return key + 'Draw'


def draw_mesh_at_bodies(scene, key, world, body, mesh, col, row, color, name=None, apply_scale=False):
    """RigidBodyPoints -> RepeatMeshAtPoints(mesh) -> DrawMeshWithShadow: an original GPU mesh drawn at every body."""
    label = name or key
    scene.op(key + 'Points', 'RigidBodyPoints', col, row, name=label + 'Poses')
    scene.op(key + 'Mesh', 'RepeatMeshAtPoints', col + 1, row, name=label + 'Mesh', ApplyPointScale=apply_scale)
    scene.op(key + 'Draw', 'DrawMeshWithShadow', col + 2, row, name='Draw' + label, Color=color)
    scene.link(world + '.OutBuffer', key + 'Points.World')
    scene.link(body + '.OutBody', key + 'Points.Body')
    scene.link(key + 'Points.Points', key + 'Mesh.Points')
    scene.link(mesh, key + 'Mesh.InputMesh')
    scene.link(key + 'Mesh.Result', key + 'Draw.Mesh')
    return key + 'Draw'


def mesh_view(scene, key, geometry, col, row, color, name=None, translation=None, rotation=None, scale=None):
    """[TransformGeometry] -> GeometryToMesh -> DrawMeshWithShadow for a geometry output. Returns the draw key."""
    label = name or key
    source = geometry
    if translation or rotation or scale:
        values = {}
        if translation:
            values['Translation'] = translation
        if rotation:
            values['Rotation'] = rotation
        if scale:
            values['UniformScale'] = scale
        scene.op(key + 'Place', 'TransformGeometry', col, row, name='Place' + label, **values)
        scene.link(geometry, key + 'Place.Geometry')
        source = key + 'Place.Result'
    scene.op(key + 'Mesh', 'GeometryToMesh', col + 1, row, name=label + 'Mesh')
    scene.op(key + 'Draw', 'DrawMeshWithShadow', col + 2, row, name='Draw' + label, Color=color)
    scene.link(source, key + 'Mesh.Geometry')
    scene.link(key + 'Mesh.Buffers', key + 'Draw.Mesh')
    return key + 'Draw'


def floor(scene, key, col, row, height=0.0, size=30.0, color=None):
    """A flat quad that receives the shadows. Returns the draw key."""
    scene.op(key, 'QuadMesh', col, row, name='Floor', Scale=size, Rotation=(-90.0, 0.0, 0.0), Center=(0.0, height, 0.0))
    scene.op(key + 'Draw', 'DrawMeshWithShadow', col + 2, row, name='DrawFloor', Color=color or PALETTE['ground'])
    scene.link(key + '.Data', key + 'Draw.Mesh')
    return key + 'Draw'


def cpu_points(scene, key, generator, col, row, name, **values):
    """A GPU point generator followed by ReadPointsFromGpu, read once: RigidBody wants a CPU point list."""
    scene.op(key, generator, col, row, name=name, **values)
    scene.op(key + 'Cpu', 'ReadPointsFromGpu', col + 1, row, name=name + 'ToCpu', UpdateContinuously=False, MaxCount=0)
    scene.link(key + '.OutBuffer', key + 'Cpu.PointBuffer')
    return key + 'Cpu.Output'


def render_rig(scene, draws, col, camera, target, light=(6.0, 14.0, 8.0), light_target=(0.0, 0.0, 0.0), fov=40.0,
               fog=60.0, shadow_range=60.0, row=0, prefix=''):
    """Execute -> SetShadow -> key light -> fill light -> fog -> camera -> render target -> SSAO. Returns the rig keys."""
    p = prefix
    keys = []
    keys.append(scene.op(p + 'scene', 'Execute', col, row, name='Scene'))
    for draw in draws:
        scene.link(draw if '.' in draw else draw + '.Output', p + 'scene.Command')
    keys.append(scene.op(p + 'shadow', 'SetShadow', col, row + 1, name='Shadow', LightPosition=light, LightTarget=light_target,
                         Resolution=2048, DepthRange=(0.5, shadow_range)))
    keys.append(scene.op(p + 'keyLight', 'PointLight', col, row + 2, name='KeyLight', Position=light, Intensity=2.2, Range=1.0, Decay=1.2,
                         Color=(1.0, 0.95, 0.88, 1.0)))
    keys.append(scene.op(p + 'fillLight', 'PointLight', col, row + 3, name='FillLight', Position=(-light[0] * 1.2, light[1] * 0.5, -light[2]),
                         Intensity=0.6, Range=1.0, Decay=1.0, Color=(0.7, 0.8, 1.0, 1.0)))
    keys.append(scene.op(p + 'fog', 'SetFog', col, row + 4, name='Haze', Distance=fog, Bias=2.0, Color=SKY))
    keys.append(scene.op(p + 'camera', 'Camera', col, row + 5, name='Camera', Position=camera, Target=target, FieldOfView=fov))
    keys.append(scene.op(p + 'target', 'RenderTarget', col + 1, row + 5, name='Render', Resolution=(1280, 720), ClearColor=SKY, Multisampling=4))
    keys.append(scene.op(p + 'ssao', 'SSAO', col + 2, row + 5, name='AmbientOcclusion'))
    scene.link(p + 'scene.Output', p + 'shadow.Command')
    scene.link(p + 'shadow.Output', p + 'keyLight.Command')
    scene.link(p + 'keyLight.Output', p + 'fillLight.Command')
    scene.link(p + 'fillLight.Output', p + 'fog.Command')
    scene.link(p + 'fog.Output', p + 'camera.Command')
    scene.link(p + 'camera.Output', p + 'target.Command')
    scene.link(p + 'target.ColorBuffer', p + 'ssao.Texture2d')
    scene.link(p + 'target.DepthBuffer', p + 'ssao.DepthBuffer')
    scene.output(p + 'ssao.Output')
    scene.section('Render: light, shadow and camera', keys, SECTION_COLORS['render'])
    return keys
