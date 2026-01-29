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
            CommandExecutionService commandService,
            Action openCrmDialogAction)
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

            void RegReply(string keys, string desc, string text) => Reg(keys, desc, () => commandService.Enqueue("Reply", text, keys), text);
            void RegBot(string keys, string desc, string cmd) => Reg(keys, desc, () => commandService.Enqueue("Bot", cmd, keys), cmd);

            // --- BOT COMMANDS ---
            Action botCall = () =>
            {
                commandService.Enqueue("BotCall", null, "Alt+C");
            };

            Reg("Alt+D0", "@chat_bot (Вызов)", botCall, "Вызов меню бота (@chat_bot)");
            Reg("Alt+C", "@chat_bot (Вызов)", botCall, "Вызов меню бота (@chat_bot)");
            RegBot("Alt+D1", "cmd newtask", "cmd newtask");
            RegBot("Alt+D2", "cmd add crmid", "cmd add crmid");
            RegBot("Alt+D3", "cmd add user", "cmd add user");
            RegBot("Alt+D4", "cmd remove crmid", "cmd remove crmid");
            RegBot("Alt+D5", "cmd forcing", "cmd forcing");
            RegBot("Alt+D6", "cmd timer set 6", "cmd timer set 6");
            RegBot("Alt+Shift+D6", "cmd timer dismiss", "cmd timer dismiss");

            // Dynamic Date
            Reg("Alt+D7", "cmd timer delay", () => commandService.Enqueue("Bot", $"cmd timer delay {DateTime.Now:dd.MM.yyyy HH:mm}", "Alt+D7"), "cmd timer delay [ТекущаяДата Время]");

            RegBot("Alt+D8", "cmd duplicate", "cmd duplicate");
            Reg("Alt+Shift+D8", "cmd duplicate (список)", openCrmDialogAction, "Открыть диалог ввода списка ID для дубликатов");
            RegBot("Alt+D9", "cmd request info", "cmd request info");

            // --- QUICK REPLIES ---
            RegReply("Alt+NumPad1", "Добрый день!", "Добрый день!");
            RegReply("Alt+L", "Добрый день!", "Добрый день!");

            RegReply("Alt+NumPad2", "У Вас остались вопросы по данному обращению?", "У Вас остались вопросы по данному обращению?");
            RegReply("Alt+D", "У Вас остались вопросы по данному обращению?", "У Вас остались вопросы по данному обращению?");

            RegReply("Alt+NumPad3", "Ожидайте от нас обратную связь.", "Ожидайте от нас обратную связь.");
            RegReply("Alt+J", "Ожидайте от нас обратную связь.", "Ожидайте от нас обратную связь.");

            RegReply("Alt+NumPad4", "Заявку закрываем, нет ОС.", "Заявку закрываем, так как не получили от Вас обратную связь.");
            RegReply("Alt+P", "Заявку закрываем, нет ОС.", "Заявку закрываем, так как не получили от Вас обратную связь.");

            RegReply("Alt+NumPad5", "Ваша заявка передана специалисту.", "Ваша заявка передана специалисту.\nОтветственный специалист свяжется с Вами в ближайшее время.");
            RegReply("Alt+G", "Ваша заявка передана специалисту.", "Ваша заявка передана специалисту.\nОтветственный специалист свяжется с Вами в ближайшее время.");

            RegReply("Alt+NumPad6", "Не удалось связаться с Вами по номеру:", "Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?");
            RegReply("Alt+H", "Не удалось связаться с Вами по номеру:", "Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?");

            RegReply("Alt+NumPad7", "Организация определилась верно: ?", "Организация определилась верно: ?");
            RegReply("Alt+E", "Организация определилась верно: ?", "Организация определилась верно: ?");

            RegReply("Alt+NumPad8", "Ваше обращение взято в работу.", "Ваше обращение взято в работу.");
            RegReply("Alt+M", "Ваше обращение взято в работу.", "Ваше обращение взято в работу.");

            RegReply("Alt+NumPad9", "Подскажите пожалуйста Ваш контактный номер телефона.", "Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения.");
            RegReply("Alt+N", "Подскажите пожалуйста Ваш контактный номер телефона.", "Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения.");

            RegReply("Alt+Multiply", "Уточняем информацию по Вашему вопросу.", "Уточняем информацию по Вашему вопросу.");
            RegReply("Alt+X", "Уточняем информацию по Вашему вопросу.", "Уточняем информацию по Вашему вопросу.");

            RegReply("Alt+Add", "Чем могу Вам помочь?", "Чем могу Вам помочь?");
            RegReply("Alt+F", "Чем могу Вам помочь?", "Чем могу Вам помочь?");

            RegReply("Alt+Z", "Закрываем (выполнена)", "Спасибо за обращение в iikoService и хорошего Вам дня.\nЗаявку закрываем как выполненную.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно.");
            RegReply("Alt+Shift+Z", "От вас не поступила обратная связь.", "От вас не поступила обратная связь.\nСпасибо за обращение в iikoService и хорошего Вам дня.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно.\nЗаявку закрываем.");
            RegReply("Alt+B", "Закрываем (нет вопросов)", "В связи с тем, что дополнительных вопросов от вас не поступало, данное обращение закрываем.\nЕсли у вас остались вопросы, при создании новой заявки, просим указать номер текущей.\nСпасибо за обращение в iikoService и хорошего Вам дня!");
            RegReply("Alt+Divide", "Сообщить о платных работах", "Добрый день, вы обратились в техническую поддержку iikoService.  \nК сожалению, с Вашей организацией не заключен договор технической поддержки.\nРаботы могут быть выполнены только на платной основе.\n\nСтоимость работ: руб.\nВы согласны на платные работы?");
            RegReply("Alt+V", "Обращение актуально?", "Добрый день! \nОт Вас не поступила обратная связь по данному обращению. \nПодскажите пожалуйста, Ваше обращение актуально?");

            Reg("Alt+Space", "Исправить раскладку (выделенное)", () => commandService.Enqueue("FixLayout", null, "Alt+Space"), "Исправление раскладки выделенного текста (или последнего слова)");
            Reg("Alt+Q", "Очистить очередь", () => commandService.ClearQueue(), "Принудительная очистка очереди команд");

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