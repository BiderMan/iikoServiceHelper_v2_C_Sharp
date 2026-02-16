using Microsoft.Extensions.Logging;
using Xunit;

namespace iikoServiceHelper.Tests
{
    public class TestBase
    {
        protected ILogger<T> CreateLogger<T>()
        {
            var logger = new LoggerFactory().CreateLogger<T>();
            return logger;
        }
    }
}
