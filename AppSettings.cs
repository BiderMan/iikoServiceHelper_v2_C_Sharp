using System;

namespace iikoServiceHelper.Models
{
    public class AppSettings
    {
        public double NotesFontSize { get; set; } = 14;
        public string CrmLogin { get; set; } = "";
        public string CrmPassword { get; set; } = "";
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
    }
}