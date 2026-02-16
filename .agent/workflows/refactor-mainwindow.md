---
description: Рефакторинг MainWindow в MVVM архитектуру
---

# Рефакторинг MainWindow в MVVM

## Описание

Этот workflow описывает процесс рефакторинга огромного файла MainWindow.xaml.cs (1621 строка) в чистую MVVM архитектуру с разделением ответственности.

## Предварительные требования

- Установлен пакет CommunityToolkit.Mvvm
- Создана резервная копия проекта
- Проект успешно компилируется

## Шаги выполнения

### 1. Установить CommunityToolkit.Mvvm

```powershell
dotnet add package CommunityToolkit.Mvvm
```

### 2. Создать структуру папок

Создайте следующие папки в проекте:

- `ViewModels/`
- `Views/`
- `Commands/`

### 3. Переместить MainWindow.xaml

Переместите существующий `MainWindow.xaml` и `MainWindow.xaml.cs` в папку `Views/`

### 4. Создать базовый ViewModel

Создайте файл `ViewModels/ViewModelBase.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace iikoServiceHelper.ViewModels
{
    public abstract class ViewModelBase : ObservableObject
    {
    }
}
```

### 5. Создать MainWindowViewModel

Создайте файл `ViewModels/MainWindowViewModel.cs`:

```csharp
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iikoServiceHelper.Models;
using iikoServiceHelper.Services;

namespace iikoServiceHelper.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ICommandExecutionService _commandExecutionService;
        private readonly AppSettings _settings;

        [ObservableProperty]
        private string _statusMessage = "Готов к работе";

        [ObservableProperty]
        private int _commandCount;

        public MainWindowViewModel(
            ICommandExecutionService commandExecutionService,
            AppSettings settings)
        {
            _commandExecutionService = commandExecutionService;
            _settings = settings;
            CommandCount = settings.CommandCount;
        }

        [RelayCommand]
        private void ResetCommandCount()
        {
            CommandCount = 0;
            _settings.CommandCount = 0;
        }
    }
}
```

### 6. Создать ViewModels для вкладок

Создайте отдельные ViewModels для каждой вкладки:

#### a. CrmViewModel.cs

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iikoServiceHelper.ViewModels
{
    public partial class CrmViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _crmLogin = string.Empty;

        [ObservableProperty]
        private string _crmPassword = string.Empty;

        [ObservableProperty]
        private string _crmStatus = "Не авторизован";

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task LoginAsync()
        {
            // Логика авторизации
        }

        private bool CanLogin() => 
            !string.IsNullOrEmpty(CrmLogin) && 
            !string.IsNullOrEmpty(CrmPassword);
    }
}
```

#### b. MacrosViewModel.cs

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iikoServiceHelper.ViewModels
{
    public partial class MacrosViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<CustomCommand> _commands = new();

        [ObservableProperty]
        private CustomCommand? _selectedCommand;

        [RelayCommand]
        private async Task ExecuteSelectedCommandAsync()
        {
            if (SelectedCommand != null)
            {
                // Выполнить команду
            }
        }
    }
}
```

#### c. ToolsViewModel.cs

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iikoServiceHelper.ViewModels
{
    public partial class ToolsViewModel : ViewModelBase
    {
        [RelayCommand]
        private void OpenOrderCheck()
        {
            // Логика открытия OrderCheck
        }

        [RelayCommand]
        private void OpenFtp()
        {
            // Логика открытия FTP
        }

        [RelayCommand]
        private void CopyPosLink()
        {
            // Логика копирования ссылки
        }
    }
}
```

#### d. SettingsViewModel.cs

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iikoServiceHelper.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _isLightTheme;

        [ObservableProperty]
        private bool _isAltBlockerEnabled;

        [ObservableProperty]
        private double _notesFontSize = 14;

        [RelayCommand]
        private void ZoomIn()
        {
            if (NotesFontSize < 72)
                NotesFontSize += 2;
        }

        [RelayCommand]
        private void ZoomOut()
        {
            if (NotesFontSize > 8)
                NotesFontSize -= 2;
        }
    }
}
```

### 7. Обновить MainWindowViewModel для композиции

Обновите `MainWindowViewModel` для включения дочерних ViewModels:

```csharp
public partial class MainWindowViewModel : ViewModelBase
{
    public CrmViewModel CrmViewModel { get; }
    public MacrosViewModel MacrosViewModel { get; }
    public ToolsViewModel ToolsViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainWindowViewModel(
        ICommandExecutionService commandExecutionService,
        AppSettings settings,
        CrmViewModel crmViewModel,
        MacrosViewModel macrosViewModel,
        ToolsViewModel toolsViewModel,
        SettingsViewModel settingsViewModel)
    {
        _commandExecutionService = commandExecutionService;
        _settings = settings;
        
        CrmViewModel = crmViewModel;
        MacrosViewModel = macrosViewModel;
        ToolsViewModel = toolsViewModel;
        SettingsViewModel = settingsViewModel;
    }
}
```

### 8. Обновить App.xaml.cs для регистрации ViewModels

```csharp
private void ConfigureServices(IServiceCollection services)
{
    // ... существующий код ...

    // ViewModels
    services.AddSingleton<MainWindowViewModel>();
    services.AddSingleton<CrmViewModel>();
    services.AddSingleton<MacrosViewModel>();
    services.AddSingleton<ToolsViewModel>();
    services.AddSingleton<SettingsViewModel>();
}
```

### 9. Обновить MainWindow.xaml.cs

Упростите code-behind до минимума:

```csharp
using iikoServiceHelper.ViewModels;

namespace iikoServiceHelper.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
```

### 10. Обновить MainWindow.xaml для Binding

Замените event handlers на Command bindings:

```xml
<!-- Было -->
<Button Content="Авторизация" Click="BtnCrmAutoLogin_Click"/>

<!-- Стало -->
<Button Content="Авторизация" 
        Command="{Binding CrmViewModel.LoginCommand}"/>
```

### 11. Переместить логику из code-behind в ViewModels

Постепенно переносите методы из MainWindow.xaml.cs в соответствующие ViewModels:

- Методы CRM → CrmViewModel
- Методы макросов → MacrosViewModel
- Методы инструментов → ToolsViewModel
- Методы настроек → SettingsViewModel

### 12. Тестирование

После каждого шага:

- Компилируйте проект
- Проверяйте работоспособность функций
- Фиксируйте изменения в Git

### 13. Финальная проверка

- Убедитесь, что MainWindow.xaml.cs содержит только конструктор
- Проверьте, что все функции работают
- Запустите приложение и протестируйте все вкладки

## Порядок миграции функционала

1. **Первая итерация** (самое простое):
   - Счетчик команд
   - Настройки темы
   - Масштабирование заметок

2. **Вторая итерация** (средняя сложность):
   - Управление CRM
   - Список браузеров
   - Заметки

3. **Третья итерация** (сложное):
   - Макросы и команды
   - Горячие клавиши
   - Инструменты

4. **Четвертая итерация** (интеграция):
   - Системный трей
   - Оверлеи
   - Сервисные события

## Критерии завершения

- [ ] MainWindow.xaml.cs содержит менее 50 строк кода
- [ ] Все ViewModels наследуются от ViewModelBase
- [ ] Все команды используют RelayCommand
- [ ] Все свойства используют ObservableProperty
- [ ] DataContext установлен в XAML или конструкторе
- [ ] Нет event handlers в XAML (только Commands)
- [ ] Приложение компилируется без ошибок
- [ ] Все функции работают корректно

## Полезные ссылки

- [CommunityToolkit.Mvvm Docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [MVVM Pattern](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm)
