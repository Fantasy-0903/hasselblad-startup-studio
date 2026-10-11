"""本模块独立使用的原厂 4.2.0 ELF 布局；不读取设备，不依赖历史实验。"""
import struct

X2D_HASH = 'a0ac02a51d87d08fa61fd0d1e79af15db248af4b5b32603b9483d09f9b7f6217'
CAVE_OFFSET, CAVE_VA, CAVE_LIMIT = 0x1D9690, 0x1F0690, 0x1DC000


def read_va(elf, address, size):
    if size < 0:
        raise ValueError('Negative ELF read size')
    for segment in elf.iter_segments():
        if (segment['p_type'] == 'PT_LOAD'
                and segment['p_vaddr'] <= address
                and address + size <= segment['p_vaddr'] + segment['p_filesz']):
            start = address - segment['p_vaddr']
            return segment.data()[start:start + size]
    raise ValueError('Unmapped input VA')


def branch(source, target, link=False):
    distance = target - source
    if distance % 4 or not -(1 << 27) <= distance < (1 << 27):
        raise ValueError('AArch64 branch target is unaligned or out of range')
    opcode = 0x94000000 if link else 0x14000000
    return struct.pack('<I', opcode | ((distance // 4) & 0x3ffffff))
