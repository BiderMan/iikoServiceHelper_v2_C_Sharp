using System;

namespace iikoServiceHelper
{
    public interface IHotkeyManager : IDisposable
    {
        Func<string, bool>? HotkeyHandler { get; set; }
        bool IsInputBlocked { get; set; }
        bool IsAltPhysicallyDown { get; }
        bool IsCtrlPhysicallyDown { get; }
        bool IsShiftPhysicallyDown { get; }
    }
}
