"""固件地址／跳转边界及源码审计拒绝条件；仅标准库，不读取固件或设备。"""
import struct
import sys
import tempfile
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from firmware_layout import branch, read_va
from check_source import ROOT_FILES, audit


class Segment(dict):
    def data(self):
        return b'abcd'


class ELF:
    def iter_segments(self):
        return iter([Segment(p_type='PT_LOAD', p_vaddr=0x1000, p_filesz=4)])


class SourceTests(unittest.TestCase):
    def test_branch_bounds(self):
        for distance in (-(1 << 27), -4, 0, 4, (1 << 27) - 4):
            for link in (False, True):
                instruction = struct.unpack('<I', branch(0x10000000, 0x10000000 + distance, link))[0]
                immediate = instruction & 0x3ffffff
                signed = immediate - (1 << 26) if immediate & (1 << 25) else immediate
                self.assertEqual(signed * 4, distance)
                self.assertEqual(instruction & 0xfc000000, 0x94000000 if link else 0x14000000)
        for distance in (1, -2, -(1 << 27) - 4, 1 << 27):
            with self.assertRaises(ValueError):
                branch(0, distance)

    def test_file_backed_elf_bounds(self):
        self.assertEqual(read_va(ELF(), 0x1001, 3), b'bcd')
        for address, size in ((0xfff, 1), (0x1003, 2), (0x1000, -1)):
            with self.assertRaises(ValueError):
                read_va(ELF(), address, size)

    def test_source_rejection(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ROOT_FILES:
                (root / name).write_text('test\n')
            self.assertTrue(audit(root)['passed'])
            for name, content in [('private.json', '{}'), ('README.md', '/Users/' + 'someone/private/file'),
                                  ('README.md', '[broken](missing.md)'), ('README.md', '[escape](../other.md)')]:
                path = root / name
                original = path.read_text() if path.exists() else None
                path.write_text(content)
                with self.assertRaises(ValueError):
                    audit(root)
                if original is None:
                    path.unlink()
                else:
                    path.write_text(original)
            (root / 'tools').mkdir()
            (root / 'tools/input.so').write_bytes(b'ELF')
            with self.assertRaises(ValueError):
                audit(root)
            (root / 'tools/input.so').unlink()
            (root / 'tools/link.py').symlink_to(root / 'README.md')
            with self.assertRaises(ValueError):
                audit(root)


if __name__ == '__main__':
    unittest.main()
