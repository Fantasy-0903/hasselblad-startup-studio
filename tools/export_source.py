"""按源码白名单导出独立项目及源码 ZIP；不包含固件／App／相机备份。"""
import argparse
import hashlib
import json
import shutil
import zipfile
from pathlib import Path
from check_source import ROOT, audit, source_files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        raise ValueError('Output already exists; choose a new empty destination')
    result = audit(ROOT)
    output.mkdir(parents=True)
    for path in source_files(ROOT):
        destination = output / path.relative_to(ROOT)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, destination)
    manifest = dict(project='Hasselblad Startup Studio', version='0.1.4',
                    license='MIT', deviceAccessed=False, files=result['files'])
    (output / 'release-source.json').write_text(json.dumps(manifest, indent=2) + '\n')
    audit(output, verify_release=True)
    archive = Path(str(output) + '.zip')
    if archive.exists():
        raise ValueError('Archive already exists; choose a new destination')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as package:
        for path in sorted(output.rglob('*')):
            if path.is_file():
                entry = zipfile.ZipInfo(output.name + '/' + path.relative_to(output).as_posix(),
                                        date_time=(2026, 10, 11, 0, 0, 0))
                entry.external_attr = 0o100644 << 16
                entry.compress_type = zipfile.ZIP_DEFLATED
                package.writestr(entry, path.read_bytes())
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix('.zip.sha256').write_text(checksum + '  ' + archive.name + '\n')
    print(f"PASS: {result['fileCount']} source files exported\n{archive}")


if __name__ == '__main__':
    main()
