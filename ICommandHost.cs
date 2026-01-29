using System;
using System.Threading.Tasks;

namespace iikoServiceHelper.Services
{
    public interface ICommandHost
    {
        void UpdateOverlay(string message);
        void HideOverlay();
        void LogDetailed(string message);
        void IncrementCommandCount();
        bool IsInputFocused();
        void RunOnUIThread(Action action);

        // Clipboard
        void ClipboardClear();
        void ClipboardSetText(string text);
        string? ClipboardGetText();
        bool ClipboardContainsText();

        // Input
        void SendKeysWait(string keys);
        Task CleanClipboardHistoryAsync(int items);
    }
}