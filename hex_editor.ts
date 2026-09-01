// hex_editor.ts
// Версия на TypeScript с классами, интерфейсами, декораторами

import { Command } from 'commander';
import * as fs from 'fs';
import chalk from 'chalk';
import * as crypto from 'crypto';
import crc32 from 'crc-32';

// Декоратор для логирования
function logMethod(target: any, propertyKey: string, descriptor: PropertyDescriptor) {
    const original = descriptor.value;
    descriptor.value = function (...args: any[]) {
        console.log(`[LOG] ${propertyKey} вызван с`, args);
        return original.apply(this, args);
    };
    return descriptor;
}

interface HexEditorOptions {
    cols?: number;
}

class HexEditor {
    private fd: number;
    private size: number;
    private cols: number;

    constructor(private filename: string, options: HexEditorOptions = {}) {
        this.cols = options.cols || 16;
        this.fd = fs.openSync(filename, 'r+');
        this.size = fs.statSync(filename).size;
    }

    @logMethod
    view(offset: number = 0, length: number | null = null): void {
        if (length === null) length = this.size - offset;
        const buffer = Buffer.alloc(length);
        fs.readSync(this.fd, buffer, 0, length, offset);
        this._dump(buffer, offset);
    }

    private _dump(buffer: Buffer, baseOffset: number): void {
        for (let i = 0; i < buffer.length; i += this.cols) {
            const chunk = buffer.slice(i, i + this.cols);
            const addr = baseOffset + i;
            const hex = chunk.toString('hex').toUpperCase().match(/.{1,2}/g)?.join(' ') || '';
            const ascii = chunk.map(b => (b >= 32 && b < 127) ? String.fromCharCode(b) : '.').join('');
            console.log(`${chalk.cyan(addr.toString(16).padStart(8, '0'))}  ${chalk.green(hex.padEnd(this.cols * 3))}  ${chalk.blue(ascii)}`);
        }
    }

    @logMethod
    editByte(offset: number, value: number): void {
        const buf = Buffer.from([value]);
        fs.writeSync(this.fd, buf, 0, 1, offset);
    }

    @logMethod
    findBytes(pattern: Buffer, start: number = 0): number[] {
        const buffer = Buffer.alloc(this.size - start);
        fs.readSync(this.fd, buffer, 0, buffer.length, start);
        const positions: number[] = [];
        let pos = buffer.indexOf(pattern, 0);
        while (pos !== -1) {
            positions.push(start + pos);
            pos = buffer.indexOf(pattern, pos + 1);
        }
        return positions;
    }

    @logMethod
    replaceBytes(pattern: Buffer, replacement: Buffer, start: number = 0): number {
        if (pattern.length !== replacement.length) throw new Error('Длина шаблона и замены должны совпадать');
        const positions = this.findBytes(pattern, start);
        for (const p of positions) {
            fs.writeSync(this.fd, replacement, 0, replacement.length, p);
        }
        return positions.length;
    }

    checksumCrc32(): number {
        const buffer = Buffer.alloc(this.size);
        fs.readSync(this.fd, buffer, 0, this.size, 0);
        return crc32.buf(buffer);
    }

    checksumMd5(): string {
        const buffer = Buffer.alloc(this.size);
        fs.readSync(this.fd, buffer, 0, this.size, 0);
        return crypto.createHash('md5').update(buffer).digest('hex');
    }

    checksumSha1(): string {
        const buffer = Buffer.alloc(this.size);
        fs.readSync(this.fd, buffer, 0, this.size, 0);
        return crypto.createHash('sha1').update(buffer).digest('hex');
    }

    close(): void {
        fs.closeSync(this.fd);
    }
}

const program = new Command();
program
    .name('hex_editor')
    .description('Hex Editor (TypeScript)')
    .argument('<file>', 'Файл для редактирования')
    .argument('[command]', 'Команда', 'view')
    .option('-o, --offset <number>', 'Смещение', parseInt)
    .option('-b, --bytes <hex>', 'Байты в HEX')
    .option('-s, --string <text>', 'Текстовая строка')
    .option('--replace-with <hex>', 'Байты для замены (HEX)')
    .option('--output <file>', 'Сохранить в другой файл')
    .option('--cols <number>', 'Количество байт в строке', parseInt, 16)
    .option('-c, --checksum <type>', 'Тип контрольной суммы (crc32, md5, sha1)')
    .parse(process.argv);

const options = program.opts();
const [file, command] = program.args;
const editor = new HexEditor(file, { cols: options.cols });

try {
    switch (command) {
        case 'view':
            editor.view(options.offset || 0);
            break;
        case 'edit':
            if (!options.bytes) { console.error('Укажите --bytes'); process.exit(1); }
            const val = parseInt(options.bytes.replace(/ /g, ''), 16);
            if (isNaN(val) || val > 255) { console.error('Неверный формат байта'); process.exit(1); }
            editor.editByte(options.offset || 0, val);
            console.log(`Байт по смещению ${options.offset} изменён на ${options.bytes}`);
            break;
        case 'find':
            let pattern: Buffer;
            if (options.bytes) {
                pattern = Buffer.from(options.bytes.replace(/ /g, ''), 'hex');
            } else if (options.string) {
                pattern = Buffer.from(options.string, 'utf-8');
            } else {
                console.error('Укажите --bytes или --string');
                process.exit(1);
            }
            const positions = editor.findBytes(pattern, options.offset || 0);
            if (positions.length) {
                console.log(`Найдено вхождений: ${positions.length}`);
                positions.forEach(p => console.log(`  ${p.toString(16).padStart(8, '0')}`));
            } else {
                console.log('Не найдено');
            }
            break;
        case 'replace':
            if (!options.bytes || !options.replaceWith) {
                console.error('Укажите --bytes и --replace-with');
                process.exit(1);
            }
            const pat = Buffer.from(options.bytes.replace(/ /g, ''), 'hex');
            const rep = Buffer.from(options.replaceWith.replace(/ /g, ''), 'hex');
            if (pat.length !== rep.length) {
                console.error('Длина шаблона и замены должны совпадать');
                process.exit(1);
            }
            const count = editor.replaceBytes(pat, rep, options.offset || 0);
            console.log(`Заменено: ${count}`);
            break;
        case 'checksum':
            if (options.checksum === 'crc32') {
                console.log(`CRC32: ${editor.checksumCrc32().toString(16).toUpperCase()}`);
            } else if (options.checksum === 'md5') {
                console.log(`MD5: ${editor.checksumMd5()}`);
            } else if (options.checksum === 'sha1') {
                console.log(`SHA1: ${editor.checksumSha1()}`);
            } else {
                console.error('Укажите --checksum с типом');
                process.exit(1);
            }
            break;
        default:
            console.error('Неизвестная команда');
    }
    if (options.output) {
        const out = fs.createWriteStream(options.output);
        const buf = Buffer.alloc(editor.size);
        fs.readSync(editor.fd, buf, 0, editor.size, 0);
        out.write(buf);
        out.end();
        console.log(`Сохранено в ${options.output}`);
    }
} finally {
    editor.close();
}
