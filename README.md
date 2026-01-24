# iikoService Helper (C# WPF Edition)

Приложение для автоматизации ответов с GUI и системным треем, переписанное на .NET 6 (WPF).
![alt text](image.png)

## Требования

- Windows 10/11
- .NET Desktop Runtime 6.0 (или новее)

## Разработка в VS Code

1. Установите расширение **C# Dev Kit**.
2. Нажмите `F5` для запуска отладки.

## Сборка

### 1. Portable (Автономная)
Размер: ~60 МБ. Работает сразу, ничего устанавливать не нужно.

   ```powershell
   dotnet publish iikoServiceHelper.csproj -c Release -p:EnableCompressionInSingleFile=true
   ```

### 2. Compact (Компактная)
Размер: **~3-5 МБ**. Требует установленного **.NET Desktop Runtime 6.0**.
*(Внимание: .NET Framework 4.8 не подходит)*

[Скачать .NET Desktop Runtime 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

**Авто-проверка:** Если у пользователя нет .NET 6, программа сама покажет окно с предложением скачать его.

   ```powershell
   dotnet publish iikoServiceHelper.csproj -c Release -p:SelfContained=false
   ```

### Где файл?
Готовые файлы будут лежать здесь:
`bin\Release\net6.0-windows\win-x64\publish\`
(`iikoServiceHelper.exe` или `iikoServiceHelper_Compact.exe`)

## Логирование и Данные

Настройки и заметки хранятся в:
`%LOCALAPPDATA%\iikoServiceHelper\`

## Функционал

- **GUI**: WPF интерфейс (Dark Theme).
- **Системный трей**: Сворачивание и фоновая работа.
- **Горячие клавиши**: Низкоуровневый перехват клавиатуры (работает поверх RDP/AnyDesk).
- **Макросы**: 
  - Обычный текст: Мгновенная вставка через буфер обмена.
  - Команды `@chat_bot`: Специальный алгоритм (вставка тега -> выбор -> аргументы).

---
