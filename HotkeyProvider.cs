using System;
using System.Collections.Generic;
using System.Linq;
using iikoServiceHelper.Models;
using iikoServiceHelper.Utils;

namespace iikoServiceHelper.Services
{
    public static class HotkeyProvider
    {
        public static (Dictionary<string, Action> actions, List<HotkeyDisplay> displayItems) RegisterAll(
            ICommandExecutionService commandService,
            Action openCrmDialogAction,
            IEnumerable<CustomCommand> customCommands)
        {
            var hotkeyActions = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
            var displayItems = new List<HotkeyDisplay>();

            var groupedCmds = new Dictionary<string, List<string>>();
            var descOrder = new List<string>();
            var descDetails = new Dictionary<string, string>();

            // Helper to register keys
            void Reg(string keys, string desc, Action action, string? fullText = null)
            {
                hotkeyActions[keys] = action;
                if (groupedCmds.TryAdd(desc, new List<string>())) { descOrder.Add(desc); }
                groupedCmds[desc].Add(keys);
                descDetails.TryAdd(desc, fullText ?? desc);
            }

            // --- User-Editable Commands (from JSON) ---
            if (customCommands != null)
            {
                foreach (var cmd in customCommands)
                {
                    if (string.IsNullOrWhiteSpace(cmd.Trigger)) continue;

                    string content = cmd.Content ?? "";

                    if (string.Equals(cmd.Type, "Bot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (content.Equals("call", StringComparison.OrdinalIgnoreCase))
                        {
                            Reg(cmd.Trigger, cmd.Description, () => commandService.Enqueue("BotCall", null, cmd.Trigger), "Вызов меню бота (@chat_bot)");
                        }
                        else if (content.Contains("{DateTime.Now"))
                        {
                            try
                            {
                                // Handle dynamic date formatting
                                string format = content.Substring(content.IndexOf('{') + 1, content.IndexOf('}') - content.IndexOf('{') - 1);
                                string baseCommand = content.Substring(0, content.IndexOf('{')).Trim();
                                Reg(cmd.Trigger, cmd.Description, () => commandService.Enqueue("Bot", $"{baseCommand} {DateTime.Now.ToString(format.Replace("DateTime.Now:", ""))}", cmd.Trigger), content);
                            }
                            catch (Exception ex)
                            {
                                // Логируем ошибку форматирования даты
                                System.Diagnostics.Debug.WriteLine($"Date format error in command '{cmd.Trigger}': {ex.Message}");
                            }
                        }
                        else
                        {
                            Reg(cmd.Trigger, cmd.Description, () => commandService.Enqueue("Bot", content, cmd.Trigger), content);
                        }
                    }
                    else if (string.Equals(cmd.Type, "System", StringComparison.OrdinalIgnoreCase))
                    {
                        switch (content)
                        {
                            case "OPEN_CRM_DIALOG":
                                Reg(cmd.Trigger, cmd.Description, openCrmDialogAction, "Открыть диалог ввода списка ID для дубликатов");
                                break;
                            case "FixLayout":
                                Reg(cmd.Trigger, cmd.Description, () => commandService.Enqueue("FixLayout", null, cmd.Trigger), "Исправление раскладки выделенного текста");
                                break;
                            case "ClearQueue":
                                Reg(cmd.Trigger, cmd.Description, () => commandService.ClearQueue(), "Принудительная очистка очереди команд");
                                break;
                        }
                    }
                    else // Default to Reply
                    {
                        Reg(cmd.Trigger, cmd.Description, () => commandService.Enqueue("Reply", content, cmd.Trigger), content);
                    }
                }
            }

            // --- Build Display List ---
            foreach (var desc in descOrder)
            {
                var formattedKeys = groupedCmds[desc].Select(StringUtils.FormatKeyCombo);
                displayItems.Add(new HotkeyDisplay
                {
                    Keys = string.Join(" / ", formattedKeys),
                    Desc = desc,
                    FullCommand = descDetails.ContainsKey(desc) ? descDetails[desc] : desc
                });
            }

            return (hotkeyActions, displayItems);
        }
    }
}