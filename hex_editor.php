<?php
// hex_editor.php
// Версия на PHP с использованием функций для работы с файлами, контрольные суммы через hash

class HexEditor {
    private $filename;
    private $cols;
    private $handle;
    private $size;

    public function __construct($filename, $cols = 16) {
        $this->filename = $filename;
        $this->cols = $cols;
        $this->handle = fopen($filename, 'r+');
        if (!$this->handle) throw new Exception("Не удалось открыть файл");
        $this->size = filesize($filename);
    }

    public function __destruct() {
        if ($this->handle) fclose($this->handle);
    }

    public function view($offset = 0, $length = null) {
        if ($length === null) $length = $this->size - $offset;
        if ($offset < 0 || $offset + $length > $this->size) throw new Exception("Смещение вне диапазона");
        fseek($this->handle, $offset);
        $data = fread($this->handle, $length);
        $this->dump($data, $offset);
    }

    private function dump($data, $baseOffset) {
        $len = strlen($data);
        for ($i = 0; $i < $len; $i += $this->cols) {
            $chunk = substr($data, $i, $this->cols);
            $addr = $baseOffset + $i;
            $hex = implode(' ', array_map(function($b) { return sprintf("%02X", ord($b)); }, str_split($chunk)));
            $hex = str_pad($hex, $this->cols * 3);
            $ascii = implode('', array_map(function($b) { $c = ord($b); return ($c >= 32 && $c < 127) ? $b : '.'; }, str_split($chunk)));
            echo "\033[96m" . sprintf("%08X", $addr) . "\033[0m  \033[92m" . $hex . "\033[0m  \033[94m" . $ascii . "\033[0m\n";
        }
    }

    public function editByte($offset, $value) {
        if ($offset < 0 || $offset >= $this->size) throw new Exception("Смещение вне диапазона");
        fseek($this->handle, $offset);
        fwrite($this->handle, chr($value));
    }

    public function findBytes($pattern, $start = 0) {
        fseek($this->handle, $start);
        $data = fread($this->handle, $this->size - $start);
        $positions = [];
        $pos = strpos($data, $pattern);
        while ($pos !== false) {
            $positions[] = $start + $pos;
            $pos = strpos($data, $pattern, $pos + 1);
        }
        return $positions;
    }

    public function replaceBytes($pattern, $replacement, $start = 0) {
        if (strlen($pattern) != strlen($replacement)) throw new Exception("Длина шаблона и замены должны совпадать");
        $positions = $this->findBytes($pattern, $start);
        foreach ($positions as $p) {
            fseek($this->handle, $p);
            fwrite($this->handle, $replacement);
        }
        return count($positions);
    }

    public function checksumCRC32() {
        fseek($this->handle, 0);
        $data = fread($this->handle, $this->size);
        return hash('crc32', $data);
    }

    public function checksumMD5() {
        fseek($this->handle, 0);
        $data = fread($this->handle, $this->size);
        return md5($data);
    }

    public function checksumSHA1() {
        fseek($this->handle, 0);
        $data = fread($this->handle, $this->size);
        return sha1($data);
    }
}

// Парсинг аргументов
$options = getopt('o:b:s:c:', ['cols:', 'replace-with:', 'output:', 'checksum:']);
$args = array_slice($argv, 1);
$file = array_shift($args);
$command = 'view';
if (isset($args[0]) && in_array($args[0], ['view','edit','find','replace','checksum'])) {
    $command = array_shift($args);
}
$offset = isset($options['o']) ? (int)$options['o'] : 0;
$cols = isset($options['cols']) ? (int)$options['cols'] : 16;
$bytesHex = $options['b'] ?? null;
$replaceHex = $options['replace-with'] ?? null;
$checksumType = $options['checksum'] ?? $options['c'] ?? null;
$stringSearch = null;
$output = $options['output'] ?? null;

try {
    $editor = new HexEditor($file, $cols);
    switch ($command) {
        case 'view':
            $editor->view($offset);
            break;
        case 'edit':
            if (!$bytesHex) throw new Exception("Укажите --bytes");
            $val = hexdec(str_replace(' ', '', $bytesHex));
            $editor->editByte($offset, $val);
            echo "Байт по смещению $offset изменён на $bytesHex\n";
            break;
        case 'find':
            $pattern = '';
            if ($bytesHex) {
                $pattern = hex2bin(str_replace(' ', '', $bytesHex));
            } else {
                // ожидаем строку из аргументов (не реализовано в getopt, можно через $args)
                $stringSearch = implode(' ', $args); // грубо
                $pattern = $stringSearch;
            }
            if (!$pattern) throw new Exception("Укажите --bytes");
            $positions = $editor->findBytes($pattern, $offset);
            if ($positions) {
                echo "Найдено вхождений: " . count($positions) . "\n";
                foreach ($positions as $p) echo sprintf("  %08X\n", $p);
            } else echo "Не найдено\n";
            break;
        case 'replace':
            if (!$bytesHex || !$replaceHex) throw new Exception("Укажите --bytes и --replace-with");
            $pat = hex2bin(str_replace(' ', '', $bytesHex));
            $rep = hex2bin(str_replace(' ', '', $replaceHex));
            if (strlen($pat) != strlen($rep)) throw new Exception("Длина шаблона и замены должны совпадать");
            $count = $editor->replaceBytes($pat, $rep, $offset);
            echo "Заменено: $count\n";
            break;
        case 'checksum':
            if (!$checksumType) throw new Exception("Укажите тип контрольной суммы (--checksum)");
            switch ($checksumType) {
                case 'crc32': echo "CRC32: " . $editor->checksumCRC32() . "\n"; break;
                case 'md5': echo "MD5: " . $editor->checksumMD5() . "\n"; break;
                case 'sha1': echo "SHA1: " . $editor->checksumSHA1() . "\n"; break;
                default: throw new Exception("Неизвестный тип");
            }
            break;
        default: throw new Exception("Неизвестная команда");
    }
    if ($output) {
        copy($file, $output);
        echo "Сохранено в $output\n";
    }
} catch (Exception $e) {
    fwrite(STDERR, "Ошибка: " . $e->getMessage() . "\n");
    exit(1);
}
