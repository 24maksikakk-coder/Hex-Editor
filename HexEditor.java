// HexEditor.java
// Версия на Java с использованием NIO, MappedByteBuffer, MessageDigest

import java.io.*;
import java.nio.*;
import java.nio.channels.*;
import java.nio.file.*;
import java.security.*;
import java.util.zip.*;
import java.util.*;

public class HexEditor {
    private final Path path;
    private final FileChannel channel;
    private final MappedByteBuffer buffer;
    private final long size;
    private final int cols;

    public HexEditor(String filename, int cols) throws IOException {
        this.path = Paths.get(filename);
        this.cols = cols;
        this.channel = FileChannel.open(path, StandardOpenOption.READ, StandardOpenOption.WRITE);
        this.size = channel.size();
        this.buffer = channel.map(FileChannel.MapMode.READ_WRITE, 0, size);
    }

    public void view(long offset, long length) {
        if (length == 0) length = size - offset;
        if (offset < 0 || offset + length > size) {
            System.err.println("Смещение вне диапазона");
            return;
        }
        buffer.position((int) offset);
        byte[] data = new byte[(int) length];
        buffer.get(data);
        dump(data, offset);
    }

    private void dump(byte[] data, long baseOffset) {
        for (int i = 0; i < data.length; i += cols) {
            int end = Math.min(i + cols, data.length);
            long addr = baseOffset + i;
            System.out.printf("%s  ", colorize(String.format("%08X", addr), "\u001B[96m"));
            // HEX
            StringBuilder hex = new StringBuilder();
            for (int j = i; j < end; j++) {
                hex.append(String.format("%02X ", data[j]));
            }
            while (hex.length() < cols * 3) hex.append(' ');
            System.out.printf("%s  ", colorize(hex.toString(), "\u001B[92m"));
            // ASCII
            StringBuilder ascii = new StringBuilder();
            for (int j = i; j < end; j++) {
                byte b = data[j];
                ascii.append((b >= 32 && b < 127) ? (char) b : '.');
            }
            System.out.printf("%s\n", colorize(ascii.toString(), "\u001B[94m"));
        }
    }

    private String colorize(String text, String color) {
        return color + text + "\u001B[0m";
    }

    public void editByte(long offset, byte value) {
        if (offset < 0 || offset >= size) {
            System.err.println("Смещение вне диапазона");
            return;
        }
        buffer.put((int) offset, value);
    }

    public List<Long> findBytes(byte[] pattern, long start) {
        List<Long> positions = new ArrayList<>();
        if (start < 0) start = 0;
        if (start + pattern.length > size) return positions;
        for (long i = start; i <= size - pattern.length; i++) {
            boolean match = true;
            for (int j = 0; j < pattern.length; j++) {
                if (buffer.get((int) i + j) != pattern[j]) {
                    match = false;
                    break;
                }
            }
            if (match) positions.add(i);
        }
        return positions;
    }

    public int replaceBytes(byte[] pattern, byte[] replacement, long start) {
        if (pattern.length != replacement.length) {
            System.err.println("Длина шаблона и замены должны совпадать");
            return 0;
        }
        List<Long> positions = findBytes(pattern, start);
        for (long pos : positions) {
            for (int j = 0; j < replacement.length; j++) {
                buffer.put((int) pos + j, replacement[j]);
            }
        }
        return positions.size();
    }

    public long checksumCRC32() {
        CRC32 crc = new CRC32();
        crc.update(buffer);
        return crc.getValue();
    }

    public String checksumMD5() throws NoSuchAlgorithmException {
        MessageDigest md = MessageDigest.getInstance("MD5");
        md.update(buffer);
        return bytesToHex(md.digest());
    }

    public String checksumSHA1() throws NoSuchAlgorithmException {
        MessageDigest md = MessageDigest.getInstance("SHA-1");
        md.update(buffer);
        return bytesToHex(md.digest());
    }

    private String bytesToHex(byte[] bytes) {
        StringBuilder sb = new StringBuilder();
        for (byte b : bytes) sb.append(String.format("%02x", b));
        return sb.toString();
    }

    public void close() throws IOException {
        buffer.force();
        channel.close();
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 1) {
            System.out.println("Использование: java HexEditor <file> [команда] [опции]");
            return;
        }
        String filename = args[0];
        String command = "view";
        long offset = 0;
        int cols = 16;
        String bytesHex = null, replaceHex = null, checksumType = null, stringSearch = null, output = null;
        boolean edit = false;

        for (int i = 1; i < args.length; i++) {
            switch (args[i]) {
                case "-o": offset = Long.parseLong(args[++i]); break;
                case "--cols": cols = Integer.parseInt(args[++i]); break;
                case "-b": bytesHex = args[++i]; break;
                case "--replace-with": replaceHex = args[++i]; break;
                case "-s": stringSearch = args[++i]; break;
                case "-c": checksumType = args[++i]; break;
                case "--output": output = args[++i]; break;
                default:
                    if (args[i].matches("view|edit|find|replace|checksum")) {
                        command = args[i];
                    }
            }
        }

        HexEditor editor = new HexEditor(filename, cols);
        try {
            switch (command) {
                case "view":
                    editor.view(offset, 0);
                    break;
                case "edit":
                    if (bytesHex == null) {
                        System.err.println("Укажите --bytes");
                        return;
                    }
                    byte val = (byte) Integer.parseInt(bytesHex.trim(), 16);
                    editor.editByte(offset, val);
                    System.out.printf("Байт по смещению %d изменён на %02X\n", offset, val);
                    break;
                case "find":
                    byte[] pattern;
                    if (bytesHex != null) {
                        String[] tokens = bytesHex.split(" ");
                        pattern = new byte[tokens.length];
                        for (int i = 0; i < tokens.length; i++) {
                            pattern[i] = (byte) Integer.parseInt(tokens[i], 16);
                        }
                    } else if (stringSearch != null) {
                        pattern = stringSearch.getBytes();
                    } else {
                        System.err.println("Укажите --bytes или --string");
                        return;
                    }
                    List<Long> positions = editor.findBytes(pattern, offset);
                    if (!positions.isEmpty()) {
                        System.out.printf("Найдено вхождений: %d\n", positions.size());
                        for (long p : positions) System.out.printf("  %08X\n", p);
                    } else {
                        System.out.println("Не найдено");
                    }
                    break;
                case "replace":
                    if (bytesHex == null || replaceHex == null) {
                        System.err.println("Укажите --bytes и --replace-with");
                        return;
                    }
                    String[] patTokens = bytesHex.split(" ");
                    byte[] pat = new byte[patTokens.length];
                    for (int i = 0; i < patTokens.length; i++) pat[i] = (byte) Integer.parseInt(patTokens[i], 16);
                    String[] repTokens = replaceHex.split(" ");
                    byte[] rep = new byte[repTokens.length];
                    for (int i = 0; i < repTokens.length; i++) rep[i] = (byte) Integer.parseInt(repTokens[i], 16);
                    if (pat.length != rep.length) {
                        System.err.println("Длина шаблона и замены должны совпадать");
                        return;
                    }
                    int count = editor.replaceBytes(pat, rep, offset);
                    System.out.printf("Заменено: %d\n", count);
                    break;
                case "checksum":
                    if (checksumType == null) {
                        System.err.println("Укажите тип контрольной суммы: crc32, md5, sha1");
                        return;
                    }
                    switch (checksumType) {
                        case "crc32": System.out.printf("CRC32: %08X\n", editor.checksumCRC32()); break;
                        case "md5": System.out.printf("MD5: %s\n", editor.checksumMD5()); break;
                        case "sha1": System.out.printf("SHA1: %s\n", editor.checksumSHA1()); break;
                        default: System.err.println("Неизвестный тип");
                    }
                    break;
                default:
                    System.err.println("Неизвестная команда");
            }
            if (output != null) {
                // Сохранение через NIO
                try (FileChannel out = FileChannel.open(Paths.get(output), StandardOpenOption.CREATE, StandardOpenOption.WRITE)) {
                    buffer.position(0);
                    out.write(buffer);
                }
                System.out.printf("Сохранено в %s\n", output);
            }
        } finally {
            editor.close();
        }
    }
}
