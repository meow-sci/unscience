"""Write the native KSA actuation track without exporter-specific animation baking.

KSA skips animated nodes when building PartLookup. The unanimated Lid and
LidCollider children inherit the animated hinge's world transform instead.
This tiny GLB is original data; it includes no game assets.
"""
from pathlib import Path
import json
import math
import struct

ROOT = Path(__file__).resolve().parents[1]


def write_animation():
    angle = math.radians(100) / 2
    values = [0, 1.2, 0, 0, 0, 1, 0, math.sin(angle), 0, math.cos(angle)]
    binary = struct.pack('<10f', *values)
    doc = {
        'asset': {'version': '2.0', 'generator': 'Unscience cooler native actuation authoring'},
        'scene': 0, 'scenes': [{'nodes': [0]}],
        'nodes': [
            {'name': 'Hinge', 'translation': [-.19, 0, -.409], 'children': [1, 2]},
            {'name': 'Lid'}, {'name': 'LidCollider'}],
        'buffers': [{'byteLength': len(binary)}],
        'bufferViews': [{'buffer': 0, 'byteOffset': 0, 'byteLength': 8},
                        {'buffer': 0, 'byteOffset': 8, 'byteLength': 32}],
        'accessors': [
            {'bufferView': 0, 'componentType': 5126, 'count': 2, 'type': 'SCALAR',
             'min': [0], 'max': [1.2]},
            {'bufferView': 1, 'componentType': 5126, 'count': 2, 'type': 'VEC4'}],
        'animations': [{'name': 'Actuation',
            'samplers': [{'input': 0, 'output': 1, 'interpolation': 'LINEAR'}],
            'channels': [{'sampler': 0, 'target': {'node': 0, 'path': 'rotation'}}]}]}
    encoded = json.dumps(doc, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    body = struct.pack('<I4s', len(encoded), b'JSON') + encoded
    body += struct.pack('<I4s', len(binary), b'BIN\0') + binary
    path = ROOT / 'Animations/BeverageCooler_Lid.glb'
    path.write_bytes(struct.pack('<4sII', b'glTF', 2, len(body)+12) + body)
    return path


if __name__ == '__main__':
    print(write_animation())
