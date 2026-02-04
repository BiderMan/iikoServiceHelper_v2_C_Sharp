using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace iikoServiceHelper.Models
{
    public class AppSettings
    {
        public double NotesFontSize { get; set; } = 14;
        public string CrmLogin { get; set; } = "";

        [JsonIgnore]
        public string CrmPassword 
        { 
            get => DecryptPassword(CrmPasswordEncrypted);
            set => CrmPasswordEncrypted = EncryptPassword(value);
        }

        public string CrmPasswordEncrypted { get; set; } = "";

        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public double WindowWidth { get; set; } = 950;
        public double WindowHeight { get; set; } = 600;
        public int WindowState { get; set; } = 0;
        public string SelectedBrowser { get; set; } = "";
        public bool IsAltBlockerEnabled { get; set; } = true;
        public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
        public int CommandCount { get; set; } = 0;
        public bool IsLightTheme { get; set; } = false;
        public int NotificationDurationSeconds { get; set; } = 3;
        public DelaySettings Delays { get; set; } = new DelaySettings();

        public class DelaySettings
        {
            public int KeyPress { get; set; } = 50;
            public int ActionPause { get; set; } = 100;
            public int FocusWait { get; set; } = 500;
        }

        private static string EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;
            try {
                var data = Encoding.UTF8.GetBytes(password);
                var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            } catch (Exception ex) {
                // Логируем ошибку шифрования
                System.Diagnostics.Debug.WriteLine($"Encryption failed: {ex.Message}");
                return string.Empty;
            }
        }

        private static string DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrEmpty(encryptedPassword)) return string.Empty;
            try {
                var data = Convert.FromBase64String(encryptedPassword);
                var decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            } catch (Exception ex) {
                // Логируем ошибку дешифрования
                System.Diagnostics.Debug.WriteLine($"Decryption failed: {ex.Message}");
                return string.Empty;
            }
        }
    }
}