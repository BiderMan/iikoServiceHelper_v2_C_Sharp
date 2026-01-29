namespace iikoServiceHelper.Utils
{
    public static class StringUtils
    {
        public static string FormatKeyCombo(string keyCombo)
        {
            return keyCombo.Replace("NumPad", "Num ")
                     .Replace("D0", "0").Replace("D1", "1").Replace("D2", "2").Replace("D3", "3")
                     .Replace("D4", "4").Replace("D5", "5").Replace("D6", "6").Replace("D7", "7")
                     .Replace("D8", "8").Replace("D9", "9").Replace("Multiply", "Num*")
                     .Replace("Add", "Num+").Replace("Divide", "Num/");
        }
    }
}