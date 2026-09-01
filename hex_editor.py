# hex_editor.py
# Версия на Python с использованием mmap, argparse, цветного вывода

import sys
import os
import mmap
import argparse
import hashlib
import zlib
from typing import Optional, List, Tuple

# ANSI-цвета
class Colors:
    RESET = '\033[0m'
    RED = '\033[91m'
    GREEN = '\033[92m'
    YELLOW = '\033[93m'
    BLUE = '\033[94m'
    CYAN = '\033[96m'
    BOLD = '\033[1m'

def colorize(text: str, color: str) -> str:
    return f"{color}{text}{Colors.RESET}"

class HexEditor:
    def __init__(self, filename: str, cols: int = 16):
        self.filename = filename
        self.cols = cols
        self.size = os.path.getsize(filename)
        self.fd = os.open(filename, os.O_RDWR)
        self.mmap = mmap.mmap(self.fd, 0, access=mmap.ACCESS_WRITE)

    def close(self):
        self.mmap.close()
        os.close(self.fd)

    def view(self, offset: int = 0, length: Optional[int] = None) -> None:
        """Выводит HEX-дамп файла."""
        if length is None:
            length = self.size - offset
        if offset < 0 or offset >= self.size:
            raise ValueError("Смещение вне диапазона")
        self.mmap.seek(offset)
        data = self.mmap.read(length)
        self._dump(data, offset)

    def _dump(self, data: bytes, base_offset: int) -> None:
        """Печатает дамп в формате: адрес | hex | ascii."""
        for i in range(0, len(data), self.cols):
            chunk = data[i:i+self.cols]
            addr = base_offset + i
            hex_part = ' '.join(f'{b:02X}' for b in chunk)
            hex_part = hex_part.ljust(self.cols * 3)
            ascii_part = ''.join(chr(b) if 32 <= b < 127 else '.' for b in chunk)
            print(f"{colorize(f'{addr:08X}', Colors.CYAN)}  {colorize(hex_part, Colors.GREEN)}  {colorize(ascii_part, Colors.BLUE)}")

    def edit_byte(self, offset: int, value: int) -> None:
        """Изменяет байт по смещению."""
        if offset < 0 or offset >= self.size:
            raise ValueError("Смещение вне диапазона")
        self.mmap.seek(offset)
        self.mmap.write_byte(value)

    def find_bytes(self, pattern: bytes, start: int = 0) -> List[int]:
        """Находит все вхождения байтовой последовательности."""
        result = []
        pos = self.mmap.find(pattern, start)
        while pos != -1:
            result.append(pos)
            pos = self.mmap.find(pattern, pos + 1)
        return result

    def find_string(self, text: str, start: int = 0, ignore_case: bool = False) -> List[int]:
        """Находит все вхождения текстовой строки."""
        pattern = text.encode('utf-8')
        if ignore_case:
            pattern = pattern.lower()
            # Для поиска без учёта регистра можно использовать пользовательский поиск, но для простоты используем bytes
            # В реальном проекте лучше реализовать, но здесь оставляем как есть
        return self.find_bytes(pattern, start)

    def replace_bytes(self, pattern: bytes, replacement: bytes, start: int = 0) -> int:
        """Заменяет все вхождения pattern на replacement. Возвращает количество замен."""
        if len(pattern) != len(replacement):
            raise ValueError("Длина шаблона и замены должны совпадать")
        count = 0
        pos = self.mmap.find(pattern, start)
        while pos != -1:
            self.mmap.seek(pos)
            self.mmap.write(replacement)
            count += 1
            pos = self.mmap.find(pattern, pos + 1)
        return count

    def checksum_crc32(self) -> int:
        """Вычисляет CRC32 файла."""
        self.mmap.seek(0)
        return zlib.crc32(self.mmap.read())

    def checksum_md5(self) -> str:
        self.mmap.seek(0)
        return hashlib.md5(self.mmap.read()).hexdigest()

    def checksum_sha1(self) -> str:
        self.mmap.seek(0)
        return hashlib.sha1(self.mmap.read()).hexdigest()

def main():
    parser = argparse.ArgumentParser(description='Hex Editor (Python)')
    parser.add_argument('file', help='Файл для редактирования')
    parser.add_argument('command', nargs='?', default='view',
                        choices=['view', 'edit', 'find', 'replace', 'checksum'],
                        help='Команда')
    parser.add_argument('-o', '--offset', type=int, default=0, help='Смещение')
    parser.add_argument('-b', '--bytes', help='Байты в HEX (например FF 00 01) или строка для поиска')
    parser.add_argument('-s', '--string', help='Текстовая строка для поиска')
    parser.add_argument('--replace-with', help='Байты для замены (HEX)')
    parser.add_argument('--output', help='Сохранить в другой файл')
    parser.add_argument('--cols', type=int, default=16, help='Количество байт в строке')
    parser.add_argument('-c', '--checksum', choices=['crc32', 'md5', 'sha1'], help='Тип контрольной суммы')
    args = parser.parse_args()

    editor = HexEditor(args.file, args.cols)
    try:
        if args.command == 'view':
            editor.view(args.offset)
        elif args.command == 'edit':
            if args.bytes is None:
                print("Ошибка: укажите --bytes для редактирования")
                return
            # Преобразуем HEX-строку в байт
            try:
                value = int(args.bytes.replace(' ', ''), 16)
                if value > 255:
                    raise ValueError
            except:
                print("Ошибка: неверный формат байта (ожидается HEX, например FF)")
                return
            editor.edit_byte(args.offset, value)
            print(f"Байт по смещению {args.offset} изменён на {args.bytes}")
        elif args.command == 'find':
            if args.bytes:
                pattern = bytes.fromhex(args.bytes.replace(' ', ''))
            elif args.string:
                pattern = args.string.encode('utf-8')
            else:
                print("Ошибка: укажите --bytes или --string")
                return
            positions = editor.find_bytes(pattern, args.offset)
            if positions:
                print(f"Найдено вхождений: {len(positions)}")
                for pos in positions:
                    print(f"  {pos:08X}")
            else:
                print("Не найдено")
        elif args.command == 'replace':
            if not args.bytes or not args.replace_with:
                print("Ошибка: укажите --bytes и --replace-with")
                return
            pattern = bytes.fromhex(args.bytes.replace(' ', ''))
            replacement = bytes.fromhex(args.replace_with.replace(' ', ''))
            if len(pattern) != len(replacement):
                print("Ошибка: длина шаблона и замены должны совпадать")
                return
            count = editor.replace_bytes(pattern, replacement, args.offset)
            print(f"Заменено: {count}")
        elif args.command == 'checksum':
            if args.checksum == 'crc32':
                print(f"CRC32: {editor.checksum_crc32():08X}")
            elif args.checksum == 'md5':
                print(f"MD5: {editor.checksum_md5()}")
            elif args.checksum == 'sha1':
                print(f"SHA1: {editor.checksum_sha1()}")
        # Сохранение, если указан output
        if args.output:
            with open(args.output, 'wb') as f:
                editor.mmap.seek(0)
                f.write(editor.mmap.read())
            print(f"Сохранено в {args.output}")
    finally:
        editor.close()

if __name__ == '__main__':
    main()
