from pathlib import Path
import re
import struct
import sys


def cstr(data, pos):
    end = data.index(0, pos)
    return data[pos:end].decode('utf-8', 'replace'), end + 1


def align4(pos):
    return (pos + 3) & ~3


def read_file(path):
    data = Path(path).read_bytes()
    assert data[:13] == b'BLENDER17-01v'
    pos = 17
    blocks = []
    while pos < len(data):
        code = data[pos:pos + 4].rstrip(b'\0').decode('ascii')
        sdna = struct.unpack_from('<I', data, pos + 4)[0]
        old = struct.unpack_from('<Q', data, pos + 8)[0]
        length = struct.unpack_from('<Q', data, pos + 16)[0]
        count = struct.unpack_from('<I', data, pos + 24)[0]
        blocks.append((code, sdna, old, length, count, pos + 32))
        pos += 32 + length
        if code == 'ENDB':
            break
    assert pos == len(data)
    dna_block = next(b for b in blocks if b[0] == 'DNA1')
    dna = data[dna_block[-1]:dna_block[-1] + dna_block[3]]
    assert dna[:8] == b'SDNANAME'
    p = 8
    n_names = struct.unpack_from('<I', dna, p)[0]; p += 4
    names = []
    for _ in range(n_names):
        name, p = cstr(dna, p); names.append(name)
    p = align4(p)
    assert dna[p:p + 4] == b'TYPE'; p += 4
    n_types = struct.unpack_from('<I', dna, p)[0]; p += 4
    types = []
    for _ in range(n_types):
        typ, p = cstr(dna, p); types.append(typ)
    p = align4(p)
    assert dna[p:p + 4] == b'TLEN'; p += 4
    lengths = struct.unpack_from('<' + 'H' * n_types, dna, p); p += 2 * n_types
    p = align4(p)
    assert dna[p:p + 4] == b'STRC'; p += 4
    n_structs = struct.unpack_from('<I', dna, p)[0]; p += 4
    structs = []
    for _ in range(n_structs):
        type_idx, n_fields = struct.unpack_from('<HH', dna, p); p += 4
        fields = []
        for _ in range(n_fields):
            field_type, field_name = struct.unpack_from('<HH', dna, p); p += 4
            fields.append((types[field_type], names[field_name]))
        structs.append((types[type_idx], fields, lengths[type_idx]))
    return data, blocks, structs, dict(zip(types, lengths))


def field_size(typ, name, lengths):
    dim = 1
    for n in re.findall(r'\[(\d+)\]', name):
        dim *= int(n)
    return (8 if '*' in name else lengths[typ]) * dim


def show_struct(name, structs, lengths):
    match = [(i, s) for i, s in enumerate(structs) if s[0] == name]
    if not match:
        print('No struct', name); return
    i, (_, fields, size) = match[0]
    print('\nSTRUCT', name, 'index', i, 'size', size)
    off = 0
    for typ, field in fields:
        fs = field_size(typ, field, lengths)
        print(f'{off:5d} {typ:28s} {field:40s} {fs}')
        off += fs
    print('SUM', off)


if __name__ == '__main__':
    data, blocks, structs, lengths = read_file(sys.argv[1])
    print('BLOCKS', len(blocks), 'STRUCTS', len(structs))
    for name in sys.argv[2:]:
        show_struct(name, structs, lengths)
