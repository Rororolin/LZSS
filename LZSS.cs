using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class LZSSEncoder
{
    const int MAX_DICT_SIZE = 64;
    const int MAX_BUFFER_SIZE = 64;
    const int MIN_SIZE = 2;
    const int DEFAULT_DICT_SIZE = 16;
    const int DEFAULT_BUFFER_SIZE = 16;
    const char EMPTY_CHAR = 'ε';

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        if (args.Length > 0 && args[0] == "--selftest")
        {
            SelfTest();
            return;
        }
        if (args.Length > 0 && args[0] == "--demo")
        {
            DemoEncode("ткачева_полина_в", 16, 16);
            return;
        }

        Console.WriteLine("=== LZSS Кодер/Декодер (макс. словарь и буфер = 64) ===\n");

        while (true)
        {
            Console.WriteLine("Выберите режим:");
            Console.WriteLine("1 - Кодирование");
            Console.WriteLine("2 - Декодирование");
            Console.WriteLine("0 - Выход");
            Console.Write("Ваш выбор (0/1/2): ");
            string mode = Console.ReadLine()?.Trim();

            if (mode == "1")
                EncodeMode();
            else if (mode == "2")
                DecodeMode();
            else if (mode == "0" || mode == null)
                break;
            else
                Console.WriteLine("Ошибка: неверный выбор режима!\n");
        }
    }

    static void EncodeMode()
    {
        Console.Write("\nВведите строку для кодирования: ");
        string input = Console.ReadLine();

        if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine("Ошибка: строка не может быть пустой!\n");
            return;
        }

        if (input.Contains(EMPTY_CHAR))
        {
            Console.WriteLine($"Ошибка: строка не должна содержать служебный символ '{EMPTY_CHAR}'!\n");
            return;
        }

        int dictSize = ReadPositiveInt($"Размер словаря ({MIN_SIZE}-{MAX_DICT_SIZE}, по умолчанию {DEFAULT_DICT_SIZE}): ",
            DEFAULT_DICT_SIZE, MIN_SIZE, MAX_DICT_SIZE);
        int bufferSize = ReadPositiveInt($"Размер буфера ({MIN_SIZE}-{MAX_BUFFER_SIZE}, по умолчанию {DEFAULT_BUFFER_SIZE}): ",
            DEFAULT_BUFFER_SIZE, MIN_SIZE, MAX_BUFFER_SIZE);

        int dictBits = (int)Math.Ceiling(Math.Log2(dictSize));
        int lengthBits = (int)Math.Ceiling(Math.Log2(bufferSize));
        int maxCodeBits = Math.Max(dictBits, lengthBits);
        int totalUniqueChars = input.Distinct().Count();
        int charBits = (int)Math.Ceiling(Math.Log2(totalUniqueChars));
        if (charBits < 1) charBits = 1;

        Console.WriteLine($"\nПараметры: словарь={dictSize}, буфер={bufferSize}, бит на символ={charBits}, бит на смещение={dictBits}, бит на длину={lengthBits}");
        Console.WriteLine($"Уникальных символов: {totalUniqueChars}");

        var result = Encode(input, dictSize, bufferSize, dictBits, lengthBits, charBits);

        Console.WriteLine("\n=== Результат кодирования ===");
        Console.WriteLine($"Битовая строка ({result.TotalBits} бит):");
        Console.WriteLine(result.BitString);
        Console.WriteLine($"\nСтатистика:");
        Console.WriteLine($"  Литералов: {result.LiteralCount} x (1+{charBits}) = {result.LiteralCount * (1 + charBits)} бит");
        Console.WriteLine($"  Совпадений: {result.MatchCount} x (1+{lengthBits}+{dictBits}) = {result.MatchCount * (1 + lengthBits + dictBits)} бит");
        Console.WriteLine($"  Всего бит: {result.TotalBits}");
        Console.WriteLine($"  Исходный размер: {input.Length * 8} бит");
        Console.WriteLine($"  Степень сжатия: {100.0 * (1 - (double)result.TotalBits / (input.Length * 8)):F1}%");
        Console.WriteLine();
    }

    static void DecodeMode()
    {
        Console.Write("\nВведите битовую строку для декодирования: ");
        string bitString = Console.ReadLine()?.Replace(" ", "");

        if (string.IsNullOrEmpty(bitString))
        {
            Console.WriteLine("Ошибка: битовая строка не может быть пустой!\n");
            return;
        }

        if (!bitString.All(c => c == '0' || c == '1'))
        {
            Console.WriteLine("Ошибка: строка должна содержать только символы '0' и '1'!\n");
            return;
        }

        int dictSize = ReadPositiveInt($"Размер словаря ({MIN_SIZE}-{MAX_DICT_SIZE}, по умолчанию {DEFAULT_DICT_SIZE}): ",
            DEFAULT_DICT_SIZE, MIN_SIZE, MAX_DICT_SIZE);
        int bufferSize = ReadPositiveInt($"Размер буфера ({MIN_SIZE}-{MAX_BUFFER_SIZE}, по умолчанию {DEFAULT_BUFFER_SIZE}): ",
            DEFAULT_BUFFER_SIZE, MIN_SIZE, MAX_BUFFER_SIZE);

        Console.WriteLine("\nВведите коды символов в формате 'символ=код' через запятую");
        Console.WriteLine("Пример: а=0000,б=0001,в=0010");
        Console.Write("Коды: ");
        string codesInput = Console.ReadLine();

        var codeToChar = new Dictionary<string, char>();
        if (!string.IsNullOrEmpty(codesInput))
        {
            var pairs = codesInput.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Trim().Split('=');
                if (parts.Length == 2)
                {
                    string code = parts[1].Trim();
                    string symbolStr = parts[0].Trim();
                    if (code.All(c => c == '0' || c == '1') && symbolStr.Length > 0)
                    {
                        char symbol = symbolStr == "пробел" ? ' ' : symbolStr[0];
                        if (codeToChar.ContainsKey(code))
                        {
                            Console.WriteLine($"Ошибка: код {code} указан для нескольких символов!\n");
                            return;
                        }
                        codeToChar[code] = symbol;
                    }
                }
            }
        }

        if (codeToChar.Count == 0)
        {
            Console.WriteLine("Ошибка: не указаны коды символов!\n");
            return;
        }

        int charBits = codeToChar.Keys.First().Length;
        if (codeToChar.Keys.Any(k => k.Length != charBits))
        {
            Console.WriteLine("Ошибка: все коды символов должны быть одинаковой длины!\n");
            return;
        }

        int dictBits = (int)Math.Ceiling(Math.Log2(dictSize));
        int lengthBits = (int)Math.Ceiling(Math.Log2(bufferSize));

        Console.WriteLine($"\nПараметры: словарь={dictSize}, буфер={bufferSize}, бит на символ={charBits}, бит на смещение={dictBits}, бит на длину={lengthBits}");

        try
        {
            string decoded = Decode(bitString, dictSize, bufferSize, dictBits, lengthBits, charBits, codeToChar);
            Console.WriteLine($"\n=== Результат декодирования ===");
            Console.WriteLine($"Декодированная строка: \"{decoded}\"");
            Console.WriteLine($"Длина строки: {decoded.Length} символов\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка декодирования: {ex.Message}\n");
        }
    }

    static int ReadPositiveInt(string prompt, int defaultValue, int min, int max)
    {
        Console.Write(prompt);
        string input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
            return defaultValue;

        if (int.TryParse(input, out int value) && value >= min && value <= max)
            return value;

        Console.WriteLine($"Некорректный ввод. Использую значение по умолчанию: {defaultValue}");
        return defaultValue;
    }

    static (string BitString, int TotalBits, int LiteralCount, int MatchCount) Encode(
        string input, int dictSize, int bufferSize, int dictBits, int lengthBits, int charBits)
    {
        var uniqueChars = input.Distinct()
            .OrderBy(c => c == ' ' ? 1 : 0)
            .ThenBy(c => c)
            .ToList();

        var charToCode = new Dictionary<char, string>();

        Console.WriteLine("\nТаблица кодов символов:");
        for (int i = 0; i < uniqueChars.Count; i++)
        {
            string code = Convert.ToString(i, 2).PadLeft(charBits, '0');
            charToCode[uniqueChars[i]] = code;
            string display = uniqueChars[i] == ' ' ? "пробел" : uniqueChars[i].ToString();
            Console.WriteLine($"  {display} = {code}");
        }

        char[] dictionary = new char[dictSize];
        for (int i = 0; i < dictSize; i++)
            dictionary[i] = EMPTY_CHAR;

        int pos = 0;
        int step = 0;
        int literalCount = 0;
        int matchCount = 0;
        var resultCodes = new List<string>();

        string headerFormat = $"{{0,-4}} | {{1,-{Math.Min(dictSize, 20)}}} | {{2,-{Math.Min(bufferSize, 20)}}} | Код";
        Console.WriteLine($"\n{"j",-4} | {"Словарь",-20} | {"Буфер",-20} | Код");
        Console.WriteLine(new string('-', 60));

        while (pos < input.Length)
        {
            char[] buffer = new char[bufferSize];
            int bufLen = Math.Min(bufferSize, input.Length - pos);
            for (int i = 0; i < bufferSize; i++)
                buffer[i] = i < bufLen ? input[pos + i] : EMPTY_CHAR;

            int maxLength = 0;
            int maxOffset = 0;
            string dictStr = new string(dictionary);

            for (int length = 2; length <= bufLen; length++)
            {
                string pattern = new string(buffer, 0, length);
                bool found = false;

                for (int offset = 0; offset <= dictSize - length; offset++)
                {
                    if (dictStr.Substring(offset, length) == pattern)
                    {
                        maxLength = length;
                        maxOffset = offset;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    break;
            }

            string code;
            if (maxLength >= 2)
            {
                string lengthCode = Convert.ToString(maxLength - 1, 2).PadLeft(lengthBits, '0');
                string offsetCode = Convert.ToString(maxOffset, 2).PadLeft(dictBits, '0');
                code = $"1{lengthCode}{offsetCode}";
                matchCount++;
            }
            else
            {
                if (!charToCode.ContainsKey(buffer[0]))
                    throw new Exception($"Внутренняя ошибка: нет кода для символа '{buffer[0]}'");
                code = $"0{charToCode[buffer[0]]}";
                maxLength = 1;
                literalCount++;
            }

            resultCodes.Add(code);

            string dictDisplay = TruncateString(new string(dictionary).Replace("ε", "·"), 20);
            string bufDisplay = TruncateString(new string(buffer).Replace("ε", "·"), 20);
            Console.WriteLine($"{step,-4} | {dictDisplay,-20} | {bufDisplay,-20} | {code}");

            for (int i = 0; i < maxLength && pos < input.Length; i++)
            {
                for (int j = 0; j < dictSize - 1; j++)
                    dictionary[j] = dictionary[j + 1];
                dictionary[dictSize - 1] = input[pos];
                pos++;
            }

            step++;
        }

        string bitString = string.Concat(resultCodes);
        return (bitString, bitString.Length, literalCount, matchCount);
    }

    static string Decode(string bitString, int dictSize, int bufferSize,
        int dictBits, int lengthBits, int charBits, Dictionary<string, char> codeToChar)
    {
        char[] dictionary = new char[dictSize];
        for (int i = 0; i < dictSize; i++)
            dictionary[i] = EMPTY_CHAR;

        var output = new StringBuilder();
        int pos = 0;
        int step = 0;

        Console.WriteLine($"\n{"j",-4} | {"Код",-15} | {"Словарь",-20} | {"Добавлено",-15} | Выход");
        Console.WriteLine(new string('-', 75));

        while (pos < bitString.Length)
        {
            if (pos >= bitString.Length) break;

            char flag = bitString[pos];
            pos++;

            string added;
            if (flag == '0')
            {
                if (pos + charBits > bitString.Length)
                    throw new Exception($"Недостаточно бит для кода символа на позиции {pos}");

                string charCode = bitString.Substring(pos, charBits);
                pos += charBits;

                if (!codeToChar.TryGetValue(charCode, out char symbol))
                    throw new Exception($"Неизвестный код символа: {charCode}");

                added = symbol.ToString();
                output.Append(symbol);

                for (int j = 0; j < dictSize - 1; j++)
                    dictionary[j] = dictionary[j + 1];
                dictionary[dictSize - 1] = symbol;

                Console.WriteLine($"{step,-4} | 0{charCode,-14} | {DictStr(dictionary),-20} | {added,-15} | {output}");
            }
            else if (flag == '1')
            {
                if (pos + lengthBits + dictBits > bitString.Length)
                    throw new Exception($"Недостаточно бит для кода совпадения на позиции {pos}");

                string lengthCode = bitString.Substring(pos, lengthBits);
                pos += lengthBits;
                string offsetCode = bitString.Substring(pos, dictBits);
                pos += dictBits;

                int length = Convert.ToInt32(lengthCode, 2) + 1;
                int offset = Convert.ToInt32(offsetCode, 2);

                if (length < 2 || length > bufferSize)
                    throw new Exception($"Ошибка: длина совпадения {length} вне диапазона [2..{bufferSize}]");
                if (offset < 0 || offset >= dictSize)
                    throw new Exception($"Ошибка: смещение {offset} вне диапазона [0..{dictSize - 1}]");
                if (offset + length > dictSize)
                    throw new Exception($"Ошибка: смещение {offset} + длина {length} выходит за пределы словаря");
                for (int i = 0; i < length; i++)
                {
                    if (dictionary[offset + i] == EMPTY_CHAR)
                        throw new Exception($"Ошибка: попытка совпадения с пустой ячейкой словаря (offset={offset}, length={length})");
                }

                char[] matchedChars = new char[length];
                for (int i = 0; i < length; i++)
                    matchedChars[i] = dictionary[offset + i];

                added = "";
                for (int i = 0; i < length; i++)
                {
                    char symbol = matchedChars[i];
                    added += symbol;
                    output.Append(symbol);

                    for (int j = 0; j < dictSize - 1; j++)
                        dictionary[j] = dictionary[j + 1];
                    dictionary[dictSize - 1] = symbol;
                }

                Console.WriteLine($"{step,-4} | 1{lengthCode}{offsetCode,-10} | {DictStr(dictionary),-20} | {added,-15} | {output}");
            }
            else
            {
                throw new Exception($"Неверный флаг: {flag} на позиции {pos - 1}");
            }

            step++;
        }

        return output.ToString();
    }

    static string TruncateString(string str, int maxLen)
    {
        if (str.Length <= maxLen) return str;
        return str.Substring(str.Length - maxLen);
    }

    static void DemoEncode(string input, int dictSize, int bufferSize)
    {
        int dictBits = (int)Math.Ceiling(Math.Log2(dictSize));
        int lengthBits = (int)Math.Ceiling(Math.Log2(bufferSize));
        int unique = input.Distinct().Count();
        int charBits = (int)Math.Ceiling(Math.Log2(unique));
        if (charBits < 1) charBits = 1;
        var r = Encode(input, dictSize, bufferSize, dictBits, lengthBits, charBits);
        Console.WriteLine($"\nИтоговая битовая строка ({r.TotalBits} бит): {r.BitString}");
    }

    static void SelfTest()
    {
        string[] tests = { "ткачева_полина_в", "abracadabra", "aaaaaaaa", "hello world hello", "a", "ab" };
        int[] sizes = { 16, 8, 32, 64 };
        int passed = 0, failed = 0;

        foreach (string input in tests)
        {
            foreach (int sz in sizes)
            {
                int dictSize = sz, bufferSize = sz;
                int dictBits = (int)Math.Ceiling(Math.Log2(dictSize));
                int lengthBits = (int)Math.Ceiling(Math.Log2(bufferSize));
                int unique = input.Distinct().Count();
                int charBits = (int)Math.Ceiling(Math.Log2(unique));
                if (charBits < 1) charBits = 1;

                var enc = Encode(input, dictSize, bufferSize, dictBits, lengthBits, charBits);

                var uniqueChars = input.Distinct()
                    .OrderBy(c => c == ' ' ? 1 : 0)
                    .ThenBy(c => c)
                    .ToList();
                var codeToChar = new Dictionary<string, char>();
                for (int i = 0; i < uniqueChars.Count; i++)
                {
                    string code = Convert.ToString(i, 2).PadLeft(charBits, '0');
                    codeToChar[code] = uniqueChars[i];
                }

                string decoded;
                try { decoded = Decode(enc.BitString, dictSize, bufferSize, dictBits, lengthBits, charBits, codeToChar); }
                catch (Exception ex) { decoded = "<EX: " + ex.Message + ">"; }

                bool ok = decoded == input;
                if (ok) passed++; else failed++;
                Console.WriteLine($"[{(ok ? "OK" : "FAIL")}] dict={sz} buf={sz} input=\"{input}\" -> bits={enc.TotalBits}, decoded=\"{decoded}\"");
            }
        }
        Console.WriteLine($"\nИтого: passed={passed}, failed={failed}");
    }

    static string DictStr(char[] arr)
    {
        string s = new string(arr).Replace(EMPTY_CHAR.ToString(), "·");
        return TruncateString(s, 20);
    }
}