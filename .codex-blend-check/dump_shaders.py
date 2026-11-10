from collections import Counter
from pathlib import Path
import struct
import sys

from inspect import read_file, cstr


def u64(buf, off):
    return struct.unpack_from('<Q', buf, off)[0]


def i32(buf, off):
    return struct.unpack_from('<i', buf, off)[0]


def f32(buf, off):
    return struct.unpack_from('<f', buf, off)[0]


def name(buf, off, size=64):
    return buf[off:off + size].split(b'\0', 1)[0].decode('utf-8', 'replace')


def main(path):
    data, blocks, structs, lengths = read_file(path)
    by_ptr = {b[2]: b for b in blocks if b[2]}
    type_name = {i: s[0] for i, s in enumerate(structs)}
    print('\nFILE', Path(path).name)
    print('NODE DATA TYPES', Counter(type_name[b[1]] for b in blocks if b[0] == 'DATA' and b[1] in type_name and type_name[b[1]] in {'bNodeTree','bNode','bNodeSocket','bNodeLink','bNodeSocketValueRGBA','bNodeSocketValueFloat','bNodeSocketValueVector','bNodeSocketValueInt'}))

    def content(block):
        return data[block[5]:block[5] + block[3]]

    def ref(ptr):
        b = by_ptr.get(ptr)
        return (b, content(b)) if b else (None, None)

    def list_items(first):
        result = []
        seen = set()
        ptr = first
        while ptr and ptr not in seen:
            seen.add(ptr)
            b, buf = ref(ptr)
            if b is None:
                print('MISSING PTR', hex(ptr)); break
            result.append((ptr, b, buf))
            ptr = u64(buf, 0)
        return result

    def socket(ptr, buf):
        default = u64(buf, 240)
        default_block, val = ref(default)
        val_type = type_name[default_block[1]] if default_block else None
        default_value = None
        if val_type == 'bNodeSocketValueFloat':
            default_value = f32(val, 4)
        elif val_type == 'bNodeSocketValueRGBA':
            default_value = tuple(round(v, 6) for v in struct.unpack_from('<4f', val, 0))
        elif val_type == 'bNodeSocketValueVector':
            default_value = tuple(round(v, 6) for v in struct.unpack_from('<4f', val, 4))
        elif val_type == 'bNodeSocketValueInt':
            default_value = i32(val, 4)
        elif val_type == 'bNodeSocketValueBoolean':
            default_value = val[0]
        elif val_type == 'bNodeSocketValueString':
            default_value = name(val, 8, 1024)
        elif val_type:
            default_value = val_type
        return {'ptr': ptr, 'name': name(buf, 88), 'id': name(buf, 24), 'type': name(buf, 176), 'value': default_value}

    def tree(ptr):
        b, buf = ref(ptr)
        if b is None:
            print('MISSING TREE',hex(ptr)); return
        print('TREE', name(buf, 40, 258), 'idname', name(buf, 432), 'type', type_name[b[1]], 'ptr', hex(ptr))
        node_items = list_items(u64(buf, 528))
        node_by_ptr = {}
        socket_by_ptr = {}
        for n_ptr, n_block, n in node_items:
            n_name = name(n, 48)
            n_type = name(n, 120)
            node_by_ptr[n_ptr] = n_name
            print(' NODE', n_name, 'type', n_type, 'id', name(n, 288), 'storage',hex(u64(n,224)))
            if n_type in ('ShaderNodeMath', 'ShaderNodeVectorMath', 'ShaderNodeMixRGB'):
                print('   OP_CODE', struct.unpack_from('<h', n, 196)[0])
            storage_block, storage = ref(u64(n, 224))
            if storage_block and n_type == 'ShaderNodeValToRGB':
                count = struct.unpack_from('<h', storage, 0)[0]
                print('   COLOR_RAMP', [
                    (round(f32(storage, 8 + i * 24 + 16), 5),
                     tuple(round(v, 5) for v in struct.unpack_from('<4f', storage, 8 + i * 24)))
                    for i in range(count)
                ])
            if storage_block and n_type == 'ShaderNodeTexNoise':
                print('   NOISE_DIMENSIONS', i32(storage, 960))
            if storage_block and n_type == 'ShaderNodeMapRange':
                print('   MAP_RANGE_DATA', list(storage[:3]))
            for label, off in [('IN', 16), ('OUT', 32)]:
                for s_ptr, s_block, s in list_items(u64(n, off)):
                    so = socket(s_ptr, s)
                    socket_by_ptr[s_ptr] = so['name']
                    print('  ',label, so['name'], so['type'], 'default', so['value'])
        for _, _, link in list_items(u64(buf, 544)):
            print(' LINK', node_by_ptr.get(u64(link,16),hex(u64(link,16))), socket_by_ptr.get(u64(link,32),hex(u64(link,32))), '->', node_by_ptr.get(u64(link,24),hex(u64(link,24))), socket_by_ptr.get(u64(link,40),hex(u64(link,40))))

    for b in blocks:
        if b[0] == 'MA':
            buf = content(b)
            print('\nMATERIAL',name(buf, 40, 258),'base_RGBA',tuple(round(v,6) for v in struct.unpack_from('<4f',buf,420)),'metallic',f32(buf,468),'roughness',f32(buf,464))
            tree(u64(buf,480))
        elif b[0] == 'WO':
            buf = content(b)
            print('\nWORLD',name(buf,40,258),'horizon_RGB',tuple(round(v,6) for v in struct.unpack_from('<3f',buf,424)))
            tree(u64(buf,512))
    for b in blocks:
        if b[0] == 'NT':
            print('\nGROUP NODETREE')
            tree(b[2])


if __name__ == '__main__':
    for path in sys.argv[1:]:
        main(path)
