namespace iikoServiceHelper.Utils
{
    public static class StringUtils
    {
        public static string FormatKeyCombo(string keyCombo)
        {
            // Проверяем, что входная строка содержит только допустимые символы для горячих клавиш
            if (string.IsNullOrEmpty(keyCombo))
            {
                return keyCombo;
            }

            // Ограничиваем длину строки для предотвращения атак переполнением
            if (keyCombo.Length > 100)
            {
                System.ArgumentException ex = new System.ArgumentException("Key combo string exceeds maximum allowed length", nameof(keyCombo));
                throw ex;
            }

            var result = keyCombo;
            result = result.Replace("NumPad", "Num ");
            result = result.Replace("D0", "0").Replace("D1", "1").Replace("D2", "2").Replace("D3", "3")
                          .Replace("D4", "4").Replace("D5", "5").Replace("D6", "6").Replace("D7", "7")
                          .Replace("D8", "8").Replace("D9", "9");
            result = result.Replace("Multiply", "Num*").Replace("Add", "Num+").Replace("Divide", "Num/");
            return result;
        }

        public static string? ConvertLayout(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new System.Text.StringBuilder(input.Length);
            var en = "qwertyuiop[]asdfghjkl;'zxcvbnm,.QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>";
            var ru = "йцукенгшщзхъфывапролджэячсмитьбюЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ";

            foreach (var c in input)
            {
                var index = en.IndexOf(c);
                if (index >= 0)
                {
                    result.Append(ru[index]);
                }
                else
                {
                    var indexRu = ru.IndexOf(c);
                    if (indexRu >= 0)
                    {
                        result.Append(en[indexRu]);
                    }
                    else
                    {
                        result.Append(c);
                    }
                }
            }
            return result.ToString();
        }
    }
}