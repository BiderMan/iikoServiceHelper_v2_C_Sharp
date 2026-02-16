using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using iikoServiceHelper.Models;
using System.Threading.Tasks;
using System.Windows.Threading;
using iikoServiceHelper.Constants;

namespace iikoServiceHelper.Services
{
    public class CrmAutoLoginService : IDisposable
    {
        public event Action<string>? LogMessage;
        public event Action<string>? StatusUpdated;
        public event Action<string>? LastRunUpdated;

        private static readonly Random _random = new();
        public bool IsActive { get; private set; }

        private readonly DispatcherTimer _crmTimer;
        private CancellationTokenSource? _crmCts;
        private string _login = "";
        private string _password = "";
        private BrowserItem? _browser;

        public CrmAutoLoginService()
        {
            _crmTimer = new DispatcherTimer();
            _crmTimer.Interval = TimeSpan.FromMinutes(AppConstants.CrmAutoLoginInterval);
            _crmTimer.Tick += OnTimerTickAsync;
        }

        public void Start(string login, string password, BrowserItem browser)
        {
            if (IsActive) return;

            _login = login;
            _password = password;
            _browser = browser;

            IsActive = true;
            _crmCts = new CancellationTokenSource();

            LogMessage?.Invoke("Авто-вход включен.");
            StatusUpdated?.Invoke("Статус: Активно");

            _ = HandleTimerTickAsync(); // Запуск сразу
            _crmTimer.Start();
        }

        public void Stop()
        {
            if (!IsActive) return;

            IsActive = false;
            _crmTimer.Stop();
            _crmCts?.Cancel();
            _crmCts?.Dispose();
            _crmCts = null;

            LogMessage?.Invoke("Авто-вход остановлен пользователем.");
            StatusUpdated?.Invoke("Статус: Отключено");
        }

        // Вынесенный метод для обработки тика таймера
        private async void OnTimerTickAsync(object? sender, EventArgs? e)
        {
            await HandleTimerTickAsync().ConfigureAwait(false);
        }

        private async Task HandleTimerTickAsync()
        {
            if (_crmCts == null || _crmCts.IsCancellationRequested) return;
            LogMessage?.Invoke("Таймер сработал: Выполнение авто-входа...");
            LastRunUpdated?.Invoke($"Последний запуск: {DateTime.Now:HH:mm}");
            try
            {
                await RunBackgroundLogin(_crmCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[CrmAutoLoginService] Непредвиденная ошибка в CrmTimer_Tick: {ex.Message}");
                Debug.WriteLine($"[CrmAutoLoginService] Unhandled exception in timer tick: {ex}");
            }
        }

        private async Task RunBackgroundLogin(CancellationToken token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_login) || string.IsNullOrWhiteSpace(_password) || _browser == null)
                {
                    LogMessage?.Invoke("Ошибка: Логин, пароль или браузер не заданы.");
                    return;
                }

                LogMessage?.Invoke("=== START CRM AUTO-LOGIN ===");
                LogMessage?.Invoke("Запуск авто-входа CRM...");

                using var http = new HttpClient();

                // 1. Проверка порта Chrome Debug Protocol
                LogMessage?.Invoke($"Checking port {AppConstants.ChromeDebugPort}...");
                string versionJson = "";
                try
                {
                    versionJson = await http.GetStringAsync($"http://127.0.0.1:{AppConstants.ChromeDebugPort}/json/version", token).ConfigureAwait(false);
                    LogMessage?.Invoke($"Port {AppConstants.ChromeDebugPort} is open.");
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Порт {AppConstants.ChromeDebugPort} закрыт. Авто-вход отключен.");
                    LogMessage?.Invoke($"Требуется запуск браузера с параметром --remote-debugging-port={AppConstants.ChromeDebugPort}");
                    LogMessage?.Invoke($"Подробности: {ex.Message}");
                    Stop(); // Stop the service as it cannot continue
                    return;
                }

                // Проверка соответствия браузера
                if (_browser != null)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(versionJson);
                        if (doc.RootElement.TryGetProperty("Browser", out var browserEl))
                        {
                            string remoteBrowser = browserEl.GetString() ?? "";
                            LogMessage?.Invoke($"Connected to: {remoteBrowser}");

                            if (_browser.Name.Equals("Chrome", StringComparison.OrdinalIgnoreCase) &&
                                remoteBrowser.Contains("Edg", StringComparison.OrdinalIgnoreCase))
                            {
                                LogMessage?.Invoke($"WARN: Выбран Chrome, но порт {AppConstants.ChromeDebugPort} занят Edge.");
                            }
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        // Ignore if version json is malformed, not critical
                        LogMessage?.Invoke($"WARN: Не удалось прочитать JSON браузера: {jsonEx.Message}");
                    }
                }

                // 2. Создание новой вкладки
                LogMessage?.Invoke("Creating new tab (http://crm.iiko.ru/)...");
                string tabId = "";
                string wsUrl = "";

                try
                {
                    // Попытка создать вкладку в фоне (background: true) через Browser Target
                    try
                    {
                        string bgVersionJson = await http.GetStringAsync($"http://127.0.0.1:{AppConstants.ChromeDebugPort}/json/version", token).ConfigureAwait(false);
                        string browserWsUrl = "";
                        using (var doc = JsonDocument.Parse(bgVersionJson))
                        {
                            if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsEl))
                                browserWsUrl = wsEl.GetString() ?? "";
                        }

                        if (!string.IsNullOrEmpty(browserWsUrl))
                        {
                            using var wsBrowser = new ClientWebSocket();
                            await wsBrowser.ConnectAsync(new Uri(browserWsUrl), token).ConfigureAwait(false);

                            var createCmd = new { id = 1, method = "Target.createTarget", @params = new { url = "http://crm.iiko.ru/", background = true } };
                            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(createCmd));
                            await wsBrowser.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);

                            var buffer = new byte[4096];
                            var res = await wsBrowser.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                            string responseJson = Encoding.UTF8.GetString(buffer, 0, res.Count);

                            using var docResp = JsonDocument.Parse(responseJson);
                            if (docResp.RootElement.TryGetProperty("result", out var resEl) && resEl.TryGetProperty("targetId", out var tidEl))
                            {
                                tabId = tidEl.GetString() ?? "";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke($"INFO: Не удалось создать вкладку в фоне, пробуем стандартный метод. Ошибка: {ex.Message}");
                        /* Fallback to active tab creation */
                    }

                    // Если не вышло (или старый метод), пробуем стандартный /json/new (активная вкладка)
                    if (string.IsNullOrEmpty(tabId))
                    {
                        var response = await http.PutAsync($"http://127.0.0.1:{AppConstants.ChromeDebugPort}/json/new?http://crm.iiko.ru/", null, token).ConfigureAwait(false);
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("id", out var idEl)) tabId = idEl.GetString() ?? "";
                        if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsEl)) wsUrl = wsEl.GetString() ?? "";
                    }

                    // Если создали через Browser Target, нужно найти WS URL вкладки
                    if (!string.IsNullOrEmpty(tabId) && string.IsNullOrEmpty(wsUrl))
                    {
                        string jsonTargets = await http.GetStringAsync($"http://127.0.0.1:{AppConstants.ChromeDebugPort}/json", token).ConfigureAwait(false);
                        using var docTargets = JsonDocument.Parse(jsonTargets);
                        foreach (var el in docTargets.RootElement.EnumerateArray())
                        {
                            if (el.TryGetProperty("id", out var id) && id.GetString() == tabId)
                            {
                                if (el.TryGetProperty("webSocketDebuggerUrl", out var val)) wsUrl = val.GetString() ?? "";
                                break;
                            }
                        }
                    }

                    LogMessage?.Invoke($"Tab created. ID: {tabId}, WS: {wsUrl}");
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Ошибка создания вкладки: {ex.Message}");
                    StatusUpdated?.Invoke("Ошибка: вкладка");
                    return;
                }

                if (string.IsNullOrEmpty(wsUrl))
                {
                    LogMessage?.Invoke("Не удалось получить WebSocket URL.");
                    LogMessage?.Invoke("WebSocket URL is empty.");
                    StatusUpdated?.Invoke("Ошибка: WebSocket");
                    return;
                }

                // 3. Подключение WebSocket
                LogMessage?.Invoke($"Connecting WebSocket...");
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), token).ConfigureAwait(false);
                LogMessage?.Invoke("WebSocket connected.");

                // Локальная функция для выполнения JS
                async Task<string> Eval(string js)
                {
                    try
                    {
                        var reqId = _random.Next(10000, 99999);
                        var cmd = new
                        {
                            id = reqId,
                            method = "Runtime.evaluate",
                            @params = new { expression = js, returnByValue = true }
                        };
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cmd));
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);

                        var buffer = new byte[8192];
                        var sb = new StringBuilder();
                        var start = DateTime.Now;

                        while ((DateTime.Now - start).TotalSeconds < 5 && ws.State == WebSocketState.Open)
                        {
                            var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                            if (res.EndOfMessage)
                            {
                                var respText = sb.ToString();
                                if (respText.Contains($"\"id\":{reqId}")) return respText;
                                sb.Clear();
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke($"[Eval] Ошибка выполнения JavaScript: {ex.Message}");
                        Debug.WriteLine($"[Eval] Exception: {ex}");
                        // Добавляем более подробное логирование ошибки
                        LogMessage?.Invoke($"[Eval] Подробности ошибки: {ex.GetType().Name} в выражении {js}");
                    }
                    return "";
                }

                // 4. Ожидание загрузки
                LogMessage?.Invoke("Waiting for page load (3s)...");
                await Task.Delay(3000, token).ConfigureAwait(false);

                // 5. Проверка статуса входа
                LogMessage?.Invoke("Checking login status...");
                string checkJs = "document.querySelector('a[href*=\"action=Logout\"]') !== null";
                string resp = await Eval(checkJs).ConfigureAwait(false);

                if (resp.Contains("\"value\":true"))
                {
                    LogMessage?.Invoke("Уже авторизован.");
                    LogMessage?.Invoke("Already logged in.");
                    StatusUpdated?.Invoke($"Вход ОК: {DateTime.Now:HH:mm}");
                }
                else
                {
                    LogMessage?.Invoke("Not logged in. Attempting to login...");

                    // Ввод данных
                    LogMessage?.Invoke($"Filling form. Login: {_login}");
                    string fillJs = $"var u = document.querySelector('input[name=\"user_name\"]'); if(u) u.value = '{_login}'; " +
                                    $"var p = document.querySelector('input[name=\"user_password\"]'); if(p) p.value = '{_password}';";
                    await Eval(fillJs).ConfigureAwait(false);

                    // Нажатие кнопки
                    LogMessage?.Invoke("Clicking Login button...");
                    string clickJs = "var btn = document.querySelector('input[name=\"Login\"]'); if(btn) btn.click();";
                    await Eval(clickJs).ConfigureAwait(false);

                    // Ожидание
                    LogMessage?.Invoke("Waiting for login (5s)...");
                    await Task.Delay(5000, token).ConfigureAwait(false);

                    // Повторная проверка
                    LogMessage?.Invoke("Checking login status again...");
                    resp = await Eval(checkJs).ConfigureAwait(false);
                    if (resp.Contains("\"value\":true"))
                    {
                        LogMessage?.Invoke("Авто-вход выполнен успешно.");
                        LogMessage?.Invoke("Login successful.");
                        StatusUpdated?.Invoke($"Вход ОК: {DateTime.Now:HH:mm}");
                    }
                    else
                    {
                        LogMessage?.Invoke("Не удалось выполнить вход (проверка не прошла).");
                        LogMessage?.Invoke("Login failed (Logout button not found).");
                        StatusUpdated?.Invoke("Ошибка входа");
                    }
                }

                // 6. Закрытие вкладки
                LogMessage?.Invoke($"Closing tab {tabId}...");
                try
                {
                    await http.GetStringAsync($"http://127.0.0.1:{AppConstants.ChromeDebugPort}/json/close/{tabId}", token).ConfigureAwait(false);
                    LogMessage?.Invoke("Tab closed.");
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Error closing tab: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke("Авто-вход прерван.");
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke("Ошибка входа");
                Debug.WriteLine($"[CrmAutoLoginService] CRITICAL ERROR: {ex}");
                LogMessage?.Invoke($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}\n{ex.StackTrace}");
            }
            LogMessage?.Invoke("=== END CRM AUTO-LOGIN ===");
        }

        public void Dispose()
        {
            Stop();
            _crmTimer.Tick -= OnTimerTickAsync;
        }
    }
}