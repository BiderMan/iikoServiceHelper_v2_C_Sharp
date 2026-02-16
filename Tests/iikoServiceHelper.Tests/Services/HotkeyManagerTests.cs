using FluentAssertions;
using iikoServiceHelper;
using Moq;
using Xunit;

namespace iikoServiceHelper.Tests.Services
{
    /// <summary>
    /// Тесты для HotkeyManager.
    /// Используем мок IKeyboardHook для тестирования без Windows API.
    /// </summary>
    public class HotkeyManagerTests
    {
        [Fact]
        public void IHotkeyManager_ShouldHaveRequiredProperties()
        {
            // Arrange & Act
            var type = typeof(IHotkeyManager);
            
            // Assert - проверяем что интерфейс содержит необходимые свойства
            var properties = type.GetProperties();
            var propertyNames = properties.Select(p => p.Name).ToList();
            
            propertyNames.Should().Contain(nameof(IHotkeyManager.HotkeyHandler));
            propertyNames.Should().Contain(nameof(IHotkeyManager.IsInputBlocked));
            propertyNames.Should().Contain(nameof(IHotkeyManager.IsAltPhysicallyDown));
            propertyNames.Should().Contain(nameof(IHotkeyManager.IsCtrlPhysicallyDown));
            propertyNames.Should().Contain(nameof(IHotkeyManager.IsShiftPhysicallyDown));
        }

        [Fact]
        public void IHotkeyManager_ShouldInheritFromIDisposable()
        {
            // Arrange & Act
            var type = typeof(IHotkeyManager);
            
            // Assert
            type.Should().Implement<IDisposable>();
        }

        [Fact]
        public void HotkeyManager_ShouldImplementIHotkeyManager()
        {
            // Arrange & Act
            var type = typeof(HotkeyManager);
            
            // Assert
            type.Should().Implement<IHotkeyManager>();
        }

        [Fact]
        public void HotkeyManager_WithMockHook_ShouldInitializeWithoutRealHook()
        {
            // Arrange
            var mockKeyboardHook = new Mock<IKeyboardHook>();
            
            // Настраиваем мок для возврата валидных значений
            mockKeyboardHook.Setup(h => h.GetAsyncKeyState(It.IsAny<int>())).Returns((short)0);
            mockKeyboardHook.Setup(h => h.SetHook(It.IsAny<NativeMethods.LowLevelKeyboardProc>())).Returns(new IntPtr(1));
            
            // Act & Assert - создаем HotkeyManager с моком - не должен выбросить исключение
            var action = () => new HotkeyManager(mockKeyboardHook.Object);
            action.Should().NotThrow();
            
            // Verify мок был вызван
            mockKeyboardHook.Verify(h => h.SetHook(It.IsAny<NativeMethods.LowLevelKeyboardProc>()), Times.Once);
        }

        [Fact]
        public void HotkeyManager_ShouldCallUnhookOnDispose()
        {
            // Arrange
            var mockKeyboardHook = new Mock<IKeyboardHook>();
            mockKeyboardHook.Setup(h => h.GetAsyncKeyState(It.IsAny<int>())).Returns((short)0);
            mockKeyboardHook.Setup(h => h.SetHook(It.IsAny<NativeMethods.LowLevelKeyboardProc>())).Returns(new IntPtr(1));
            
            // Act
            var manager = new HotkeyManager(mockKeyboardHook.Object);
            manager.Dispose();
            
            // Assert
            mockKeyboardHook.Verify(h => h.UnhookWindowsHookEx(new IntPtr(1)), Times.Once);
        }

        [Fact]
        public void HotkeyManager_ShouldCallGetAsyncKeyStateOnConstruction()
        {
            // Arrange
            var mockKeyboardHook = new Mock<IKeyboardHook>();
            mockKeyboardHook.Setup(h => h.GetAsyncKeyState(It.IsAny<int>())).Returns((short)0);
            mockKeyboardHook.Setup(h => h.SetHook(It.IsAny<NativeMethods.LowLevelKeyboardProc>())).Returns(new IntPtr(1));
            
            // Act
            var manager = new HotkeyManager(mockKeyboardHook.Object);
            
            // Assert - GetAsyncKeyState должен быть вызван для 3 клавиш (Alt=18, Ctrl=17, Shift=16)
            mockKeyboardHook.Verify(h => h.GetAsyncKeyState(18), Times.Once);
            mockKeyboardHook.Verify(h => h.GetAsyncKeyState(17), Times.Once);
            mockKeyboardHook.Verify(h => h.GetAsyncKeyState(16), Times.Once);
        }

        [Fact]
        public void KeyboardHook_ShouldImplementIKeyboardHook()
        {
            // Arrange & Act
            var type = typeof(KeyboardHook);
            
            // Assert
            type.Should().Implement<IKeyboardHook>();
        }

        [Fact]
        public void IKeyboardHook_ShouldHaveRequiredMethods()
        {
            // Arrange & Act
            var type = typeof(IKeyboardHook);
            var methods = type.GetMethods().Select(m => m.Name).ToList();
            
            // Assert
            methods.Should().Contain(nameof(IKeyboardHook.SetHook));
            methods.Should().Contain(nameof(IKeyboardHook.CallNextHookEx));
            methods.Should().Contain(nameof(IKeyboardHook.UnhookWindowsHookEx));
            methods.Should().Contain(nameof(IKeyboardHook.GetAsyncKeyState));
            methods.Should().Contain(nameof(IKeyboardHook.GetModuleHandle));
        }
    }
}
