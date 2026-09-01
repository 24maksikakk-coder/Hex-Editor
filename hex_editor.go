// hex_editor.go
// Версия на Go с использованием mmap, флагов, горутин для контрольных сумм

package main

import (
	"crypto/md5"
	"crypto/sha1"
	"encoding/binary"
	"flag"
	"fmt"
	"hash/crc32"
	"os"
	"syscall"
	"unsafe"
)

// ANSI-цвета (для простоты)
const (
	reset  = "\033[0m"
	red    = "\033[91m"
	green  = "\033[92m"
	blue   = "\033[94m"
	cyan   = "\033[96m"
	bold   = "\033[1m"
)

func colorize(text, color string) string {
	return color + text + reset
}

type HexEditor struct {
	filename string
	file     *os.File
	data     []byte
	size     int64
	cols     int
}

func NewHexEditor(filename string, cols int) (*HexEditor, error) {
	f, err := os.OpenFile(filename, os.O_RDWR, 0644)
	if err != nil {
		return nil, err
	}
	info, err := f.Stat()
	if err != nil {
		return nil, err
	}
	size := info.Size()
	// mmap
	data, err := syscall.Mmap(int(f.Fd()), 0, int(size), syscall.PROT_READ|syscall.PROT_WRITE, syscall.MAP_SHARED)
	if err != nil {
		return nil, err
	}
	return &HexEditor{filename: filename, file: f, data: data, size: size, cols: cols}, nil
}

func (h *HexEditor) Close() {
	syscall.Munmap(h.data)
	h.file.Close()
}

func (h *HexEditor) View(offset, length int64) {
	if length == 0 {
		length = h.size - offset
	}
	if offset < 0 || offset+length > h.size {
		fmt.Println("Смещение вне диапазона")
		return
	}
	// Используем срез
	chunk := h.data[offset : offset+length]
	h.dump(chunk, offset)
}

func (h *HexEditor) dump(data []byte, baseOffset int64) {
	for i := 0; i < len(data); i += h.cols {
		end := i + h.cols
		if end > len(data) {
			end = len(data)
		}
		chunk := data[i:end]
		addr := baseOffset + int64(i)
		fmt.Printf("%s  ", colorize(fmt.Sprintf("%08X", addr), cyan))
		// HEX
		hexStr := ""
		for _, b := range chunk {
			hexStr += fmt.Sprintf("%02X ", b)
		}
		hexStr = fmt.Sprintf("%-*s", h.cols*3, hexStr)
		fmt.Printf("%s  ", colorize(hexStr, green))
		// ASCII
		ascii := ""
		for _, b := range chunk {
			if b >= 32 && b < 127 {
				ascii += string(b)
			} else {
				ascii += "."
			}
		}
		fmt.Printf("%s\n", colorize(ascii, blue))
	}
}

func (h *HexEditor) EditByte(offset int64, value byte) {
	if offset < 0 || offset >= h.size {
		fmt.Println("Смещение вне диапазона")
		return
	}
	h.data[offset] = value
}

func (h *HexEditor) FindBytes(pattern []byte, start int64) []int64 {
	var positions []int64
	for i := start; i <= h.size-int64(len(pattern)); i++ {
		match := true
		for j := 0; j < len(pattern); j++ {
			if h.data[i+int64(j)] != pattern[j] {
				match = false
				break
			}
		}
		if match {
			positions = append(positions, i)
		}
	}
	return positions
}

func (h *HexEditor) ReplaceBytes(pattern, replacement []byte, start int64) int {
	if len(pattern) != len(replacement) {
		fmt.Println("Длина шаблона и замены должны совпадать")
		return 0
	}
	positions := h.FindBytes(pattern, start)
	for _, p := range positions {
		for j := 0; j < len(replacement); j++ {
			h.data[p+int64(j)] = replacement[j]
		}
	}
	return len(positions)
}

func (h *HexEditor) ChecksumCRC32() uint32 {
	return crc32.ChecksumIEEE(h.data)
}

func (h *HexEditor) ChecksumMD5() string {
	sum := md5.Sum(h.data)
	return fmt.Sprintf("%x", sum)
}

func (h *HexEditor) ChecksumSHA1() string {
	sum := sha1.Sum(h.data)
	return fmt.Sprintf("%x", sum)
}

func main() {
	var offset, cols int64
	var bytesHex, replaceHex, output, checksumType, str string
	var edit bool
	flag.StringVar(&bytesHex, "b", "", "Байты в HEX (FF 00 01)")
	flag.StringVar(&replaceHex, "replace-with", "", "Байты для замены (HEX)")
	flag.StringVar(&output, "output", "", "Сохранить в другой файл")
	flag.Int64Var(&offset, "o", 0, "Смещение")
	flag.Int64Var(&cols, "cols", 16, "Количество байт в строке")
	flag.StringVar(&checksumType, "c", "", "Тип контрольной суммы (crc32, md5, sha1)")
	flag.StringVar(&str, "s", "", "Текстовая строка для поиска")
	flag.BoolVar(&edit, "edit", false, "Редактировать байт")
	flag.Usage = func() {
		fmt.Println("Использование: go run hex_editor.go <file> [команда] [опции]")
		fmt.Println("Команды: view (по умолчанию), edit, find, replace, checksum")
	}
	flag.Parse()
	if flag.NArg() < 1 {
		flag.Usage()
		return
	}
	filename := flag.Arg(0)
	cmd := "view"
	if flag.NArg() > 1 {
		cmd = flag.Arg(1)
	}

	editor, err := NewHexEditor(filename, int(cols))
	if err != nil {
		fmt.Println("Ошибка открытия:", err)
		return
	}
	defer editor.Close()

	switch cmd {
	case "view":
		editor.View(offset, 0)
	case "edit":
		if bytesHex == "" {
			fmt.Println("Укажите --bytes для редактирования")
			return
		}
		var val byte
		_, err := fmt.Sscanf(bytesHex, "%x", &val)
		if err != nil {
			fmt.Println("Неверный формат байта")
			return
		}
		editor.EditByte(offset, val)
		fmt.Printf("Байт по смещению %d изменён на %02X\n", offset, val)
	case "find":
		var pattern []byte
		if bytesHex != "" {
			// парсим hex
			pattern = []byte{}
			var b byte
			for _, token := range strings.Fields(bytesHex) {
				fmt.Sscanf(token, "%x", &b)
				pattern = append(pattern, b)
			}
		} else if str != "" {
			pattern = []byte(str)
		} else {
			fmt.Println("Укажите --bytes или --string")
			return
		}
		positions := editor.FindBytes(pattern, offset)
		if len(positions) > 0 {
			fmt.Printf("Найдено вхождений: %d\n", len(positions))
			for _, p := range positions {
				fmt.Printf("  %08X\n", p)
			}
		} else {
			fmt.Println("Не найдено")
		}
	case "replace":
		if bytesHex == "" || replaceHex == "" {
			fmt.Println("Укажите --bytes и --replace-with")
			return
		}
		pattern := []byte{}
		repl := []byte{}
		for _, token := range strings.Fields(bytesHex) {
			var b byte
			fmt.Sscanf(token, "%x", &b)
			pattern = append(pattern, b)
		}
		for _, token := range strings.Fields(replaceHex) {
			var b byte
			fmt.Sscanf(token, "%x", &b)
			repl = append(repl, b)
		}
		if len(pattern) != len(repl) {
			fmt.Println("Длина шаблона и замены должны совпадать")
			return
		}
		count := editor.ReplaceBytes(pattern, repl, offset)
		fmt.Printf("Заменено: %d\n", count)
	case "checksum":
		switch checksumType {
		case "crc32":
			fmt.Printf("CRC32: %08X\n", editor.ChecksumCRC32())
		case "md5":
			fmt.Printf("MD5: %s\n", editor.ChecksumMD5())
		case "sha1":
			fmt.Printf("SHA1: %s\n", editor.ChecksumSHA1())
		default:
			fmt.Println("Укажите тип контрольной суммы: crc32, md5, sha1")
		}
	default:
		fmt.Println("Неизвестная команда")
	}
	if output != "" {
		// Запись в файл
		err = os.WriteFile(output, editor.data, 0644)
		if err != nil {
			fmt.Println("Ошибка сохранения:", err)
		} else {
			fmt.Printf("Сохранено в %s\n", output)
		}
	}
}
