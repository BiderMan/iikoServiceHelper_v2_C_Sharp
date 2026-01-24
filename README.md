# iikoService Helper (C# WPF Edition)

Приложение для автоматизации ответов с GUI и системным треем, переписанное на .NET 6 (WPF).

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

## Исходный код интерфейса (XAML)

Если файлы `App.xaml` или `MainWindow.xaml` отсутствуют, создайте их с следующим содержимым:

### App.xaml

```xml
<Application x:Class="iikoServiceHelper.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

### MainWindow.xaml

```xml
<Window x:Class="iikoServiceHelper.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="iikoService Helper" Height="500" Width="800"
        Background="#2D2D30" Foreground="White">
    <Window.Resources>
        <Style TargetType="TabItem">
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="Padding" Value="10,5"/>
        </Style>
        <Style TargetType="Button">
            <Setter Property="Padding" Value="10,5"/>
            <Setter Property="Margin" Value="5"/>
            <Setter Property="Background" Value="#3E3E42"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#1E1E1E"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="BorderBrush" Value="#3E3E42"/>
            <Setter Property="Padding" Value="5"/>
        </Style>
        <Style TargetType="DataGrid">
            <Setter Property="Background" Value="#1E1E1E"/>
            <Setter Property="RowBackground" Value="#252526"/>
            <Setter Property="AlternatingRowBackground" Value="#2D2D30"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="HeadersVisibility" Value="Column"/>
        </Style>
    </Window.Resources>
    
    <Grid>
        <TabControl Background="Transparent" BorderThickness="0">
            <!-- Tab 1: Settings -->
            <TabItem Header="Настройки">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    
                    <DataGrid x:Name="SettingsGrid" Grid.Row="0" AutoGenerateColumns="False" 
                              SelectionChanged="SettingsGrid_SelectionChanged" IsReadOnly="True" Margin="5">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Клавиши" Binding="{Binding KeysDisplay}" Width="100"/>
                            <DataGridTextColumn Header="Описание" Binding="{Binding Desc}" Width="200"/>
                            <DataGridTextColumn Header="Значение" Binding="{Binding Value}" Width="*"/>
                        </DataGrid.Columns>
                    </DataGrid>
                    
                    <StackPanel Grid.Row="1" Orientation="Vertical" Margin="5">
                        <Grid Margin="0,5">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/> <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/> <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/> <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Кнопка:" Foreground="White" VerticalAlignment="Center" Margin="5"/>
                            <TextBox x:Name="txtKey" Grid.Column="1" Margin="2"/>
                            <TextBlock Text="Описание:" Foreground="White" Grid.Column="2" VerticalAlignment="Center" Margin="5"/>
                            <TextBox x:Name="txtDesc" Grid.Column="3" Margin="2"/>
                            <TextBlock Text="Значение:" Foreground="White" Grid.Column="4" VerticalAlignment="Center" Margin="5"/>
                            <TextBox x:Name="txtValue" Grid.Column="5" Margin="2"/>
                        </Grid>
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="Добавить" Click="AddItem_Click"/>
                            <Button Content="Сохранить" Click="SaveItem_Click"/>
                            <Button Content="Удалить" Click="DeleteItem_Click"/>
                            <Button Content="Сброс" Click="ResetDefaults_Click" Background="#7E3E3E"/>
                        </StackPanel>
                    </StackPanel>
                </Grid>
            </TabItem>

            <!-- Tab 2: Quick Replies -->
            <TabItem Header="Быстрые ответы">
                <Grid>
                    <Grid.RowDefinitions>
                       <RowDefinition Height="Auto"/>
                       <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    <TextBlock Text="Нажмите на кнопку для отправки текста" Foreground="#AAAAAA" Margin="10"/>
                    <ScrollViewer Grid.Row="1">
                        <ItemsControl x:Name="QuickRepliesList">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Button Content="{Binding Desc}" Tag="{Binding Value}" 
                                            Click="ExecuteQuickReply_Click" HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"/>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Grid>
            </TabItem>

            <!-- Tab 3: Notes -->
            <TabItem Header="Заметки">
                <TextBox x:Name="txtNotes" AcceptsReturn="True" TextWrapping="Wrap" 
                         VerticalScrollBarVisibility="Auto" LostFocus="txtNotes_LostFocus"
                         FontFamily="Consolas" FontSize="14"/>
            </TabItem>
            
             <!-- Tab 4: Tools -->
            <TabItem Header="Инструменты">
                <StackPanel>
                     <Button Content="Скачать OrderCheck" Click="OpenOrderCheck_Click" HorizontalAlignment="Left"/>
                </StackPanel>
            </TabItem>
        </TabControl>
    </Grid>
</Window>
```