using System;
using System.Threading.Tasks;

namespace iikoServiceHelper.Services
{
    public interface ICommandExecutionService
    {
        void Enqueue(string command, object? parameter, string hotkeyName);
        void ClearQueue();
        void SetHost(ICommandHost host);
    }
}