# hex_editor.rb
# Версия на Ruby с метапрограммированием, цветным выводом, использованием IO

require 'optparse'
require 'digest'

# ANSI-цвета
class String
  def colorize(color_code)
    "\e[#{color_code}m#{self}\e[0m"
  end
  def cyan; colorize(96); end
  def green; colorize(92); end
  def blue; colorize(94); end
end

class HexEditor
  attr_reader :filename, :size, :cols

  def initialize(filename, cols = 16)
    @filename = filename
    @cols = cols
    @file = File.open(filename, 'r+')
    @size = File.size(filename)
  end

  def close
    @file.close
  end

  def view(offset = 0, length = nil)
    length ||= @size - offset
    raise "Смещение вне диапазона" if offset < 0 || offset + length > @size
    @file.seek(offset)
    data = @file.read(length)
    dump(data, offset)
  end

  def dump(data, base_offset)
    data.bytes.each_slice(@cols).with_index do |chunk, idx|
      addr = base_offset + idx * @cols
      hex = chunk.map { |b| b.to_s(16).upcase.rjust(2, '0') }.join(' ')
      hex = hex.ljust(@cols * 3)
      ascii = chunk.map { |b| (32..126).include?(b) ? b.chr : '.' }.join
      puts "#{addr.to_s(16).upcase.rjust(8, '0').cyan}  #{hex.green}  #{ascii.blue}"
    end
  end

  def edit_byte(offset, value)
    raise "Смещение вне диапазона" if offset < 0 || offset >= @size
    @file.seek(offset)
    @file.write(value.chr)
  end

  def find_bytes(pattern, start = 0)
    @file.seek(start)
    data = @file.read
    positions = []
    pos = data.index(pattern, 0)
    while pos
      positions << start + pos
      pos = data.index(pattern, pos + 1)
    end
    positions
  end

  def replace_bytes(pattern, replacement, start = 0)
    if pattern.bytesize != replacement.bytesize
      raise "Длина шаблона и замены должны совпадать"
    end
    positions = find_bytes(pattern, start)
    positions.each do |p|
      @file.seek(p)
      @file.write(replacement)
    end
    positions.size
  end

  def checksum_crc32
    @file.seek(0)
    data = @file.read
    Zlib.crc32(data).to_s(16).upcase
  end

  def checksum_md5
    @file.seek(0)
    Digest::MD5.hexdigest(@file.read)
  end

  def checksum_sha1
    @file.seek(0)
    Digest::SHA1.hexdigest(@file.read)
  end
end

# Парсинг опций
options = {}
OptionParser.new do |opts|
  opts.banner = "Использование: ruby hex_editor.rb <file> [команда] [опции]"
  opts.on('-o', '--offset N', Integer, 'Смещение') { |v| options[:offset] = v }
  opts.on('-b', '--bytes HEX', 'Байты в HEX') { |v| options[:bytes] = v }
  opts.on('-s', '--string STR', 'Текстовая строка') { |v| options[:string] = v }
  opts.on('--replace-with HEX', 'Байты для замены') { |v| options[:replace] = v }
  opts.on('--output FILE', 'Сохранить в другой файл') { |v| options[:output] = v }
  opts.on('--cols N', Integer, 'Количество байт в строке') { |v| options[:cols] = v }
  opts.on('-c', '--checksum TYPE', 'Тип контрольной суммы (crc32, md5, sha1)') { |v| options[:checksum] = v }
end.parse!

file = ARGV[0]
command = ARGV[1] || 'view'
offset = options[:offset] || 0
cols = options[:cols] || 16

editor = HexEditor.new(file, cols)
begin
  case command
  when 'view'
    editor.view(offset)
  when 'edit'
    if options[:bytes].nil?
      puts "Укажите --bytes"
      exit 1
    end
    val = options[:bytes].gsub(/\s/, '').to_i(16)
    editor.edit_byte(offset, val)
    puts "Байт по смещению #{offset} изменён на #{options[:bytes]}"
  when 'find'
    pattern = if options[:bytes]
                [options[:bytes].gsub(/\s/, '')].pack('H*')
              elsif options[:string]
                options[:string]
              else
                puts "Укажите --bytes или --string"
                exit 1
              end
    positions = editor.find_bytes(pattern, offset)
    if positions.any?
      puts "Найдено вхождений: #{positions.size}"
      positions.each { |p| puts "  #{p.to_s(16).upcase.rjust(8, '0')}" }
    else
      puts "Не найдено"
    end
  when 'replace'
    if options[:bytes].nil? || options[:replace].nil?
      puts "Укажите --bytes и --replace-with"
      exit 1
    end
    pat = [options[:bytes].gsub(/\s/, '')].pack('H*')
    rep = [options[:replace].gsub(/\s/, '')].pack('H*')
    if pat.bytesize != rep.bytesize
      puts "Длина шаблона и замены должны совпадать"
      exit 1
    end
    count = editor.replace_bytes(pat, rep, offset)
    puts "Заменено: #{count}"
  when 'checksum'
    case options[:checksum]
    when 'crc32' then puts "CRC32: #{editor.checksum_crc32}"
    when 'md5'   then puts "MD5: #{editor.checksum_md5}"
    when 'sha1'  then puts "SHA1: #{editor.checksum_sha1}"
    else
      puts "Укажите тип контрольной суммы: crc32, md5, sha1"
      exit 1
    end
  else
    puts "Неизвестная команда"
  end
  if options[:output]
    FileUtils.cp(file, options[:output])
    puts "Сохранено в #{options[:output]}"
  end
ensure
  editor.close
end
