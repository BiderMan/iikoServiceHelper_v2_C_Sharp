using System.Collections.Generic;
using iikoServiceHelper.Models;

namespace iikoServiceHelper.Services
{
    public static class DefaultCommandsProvider
    {
        public static List<CustomCommand> GetDefaultCommands()
        {
            return new List<CustomCommand>
            {
                // --- BOT COMMANDS ---
                new CustomCommand { Trigger = "Alt+0", Description = "@chat_bot (Вызов)", Type = "Bot", Content = "call" },
                new CustomCommand { Trigger = "Alt+C", Description = "@chat_bot (Вызов)", Type = "Bot", Content = "call" },
                new CustomCommand { Trigger = "Alt+1", Description = "cmd newtask", Type = "Bot", Content = "cmd newtask" },
                new CustomCommand { Trigger = "Alt+2", Description = "cmd add crmid", Type = "Bot", Content = "cmd add crmid" },
                new CustomCommand { Trigger = "Alt+3", Description = "cmd add user", Type = "Bot", Content = "cmd add user" },
                new CustomCommand { Trigger = "Alt+4", Description = "cmd remove crmid", Type = "Bot", Content = "cmd remove crmid" },
                new CustomCommand { Trigger = "Alt+5", Description = "cmd forcing", Type = "Bot", Content = "cmd forcing" },
                new CustomCommand { Trigger = "Alt+6", Description = "cmd timer set 6", Type = "Bot", Content = "cmd timer set 6" },
                new CustomCommand { Trigger = "Alt+Shift+6", Description = "cmd timer dismiss", Type = "Bot", Content = "cmd timer dismiss" },
                new CustomCommand { Trigger = "Alt+7", Description = "cmd timer delay", Type = "Bot", Content = "cmd timer delay {DateTime.Now:dd.MM.yyyy HH:mm}" },
                new CustomCommand { Trigger = "Alt+8", Description = "cmd duplicate", Type = "Bot", Content = "cmd duplicate" },
                new CustomCommand { Trigger = "Alt+9", Description = "cmd request info", Type = "Bot", Content = "cmd request info" },

                // --- QUICK REPLIES ---
                new CustomCommand { Trigger = "Alt+NumPad1", Description = "Добрый день!", Type = "Reply", Content = "Добрый день!" },
                new CustomCommand { Trigger = "Alt+L", Description = "Добрый день!", Type = "Reply", Content = "Добрый день!" },
                new CustomCommand { Trigger = "Alt+NumPad2", Description = "У Вас остались вопросы по данному обращению?", Type = "Reply", Content = "У Вас остались вопросы по данному обращению?" },
                new CustomCommand { Trigger = "Alt+D", Description = "У Вас остались вопросы по данному обращению?", Type = "Reply", Content = "У Вас остались вопросы по данному обращению?" },
                new CustomCommand { Trigger = "Alt+NumPad3", Description = "Ожидайте от нас обратную связь.", Type = "Reply", Content = "Ожидайте от нас обратную связь." },
                new CustomCommand { Trigger = "Alt+J", Description = "Ожидайте от нас обратную связь.", Type = "Reply", Content = "Ожидайте от нас обратную связь." },
                new CustomCommand { Trigger = "Alt+NumPad4", Description = "Заявку закрываем, нет ОС.", Type = "Reply", Content = "Заявку закрываем, так как не получили от Вас обратную связь." },
                new CustomCommand { Trigger = "Alt+P", Description = "Заявку закрываем, нет ОС.", Type = "Reply", Content = "Заявку закрываем, так как не получили от Вас обратную связь." },
                new CustomCommand { Trigger = "Alt+NumPad5", Description = "Ваша заявка передана специалисту.", Type = "Reply", Content = "Ваша заявка передана специалисту.  \nОтветственный специалист свяжется с Вами в ближайшее время.  " },
                new CustomCommand { Trigger = "Alt+G", Description = "Ваша заявка передана специалисту.", Type = "Reply", Content = "Ваша заявка передана специалисту.\nОтветственный специалист свяжется с Вами в ближайшее время." },
                new CustomCommand { Trigger = "Alt+NumPad6", Description = "Не удалось связаться с Вами по номеру:", Type = "Reply", Content = "Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?" },
                new CustomCommand { Trigger = "Alt+H", Description = "Не удалось связаться с Вами по номеру:", Type = "Reply", Content = "Не удалось связаться с Вами по номеру:\nПодскажите, когда с Вами можно будет связаться?" },
                new CustomCommand { Trigger = "Alt+NumPad7", Description = "Организация определилась верно: ?", Type = "Reply", Content = "Организация определилась верно: ?" },
                new CustomCommand { Trigger = "Alt+E", Description = "Организация определилась верно: ?", Type = "Reply", Content = "Организация определилась верно: ?" },
                new CustomCommand { Trigger = "Alt+Shift+E", Description = "Уточните ID Организации", Type = "Reply", Content = "Укажите, пожалуйста, ID Вашей организации, который можно посмотреть в iikoOffice (раздел Помощь, О программе). \nЛибо в iikoFront (на кассе) нажав на номер версии (цифры) в левом верхнем углу (iiko v.0.0.00). \nСпасибо.\n" },
                new CustomCommand { Trigger = "Alt+NumPad8", Description = "Ваше обращение взято в работу.", Type = "Reply", Content = "Ваше обращение взято в работу." },
                new CustomCommand { Trigger = "Alt+M", Description = "Ваше обращение взято в работу.", Type = "Reply", Content = "Ваше обращение взято в работу." },
                new CustomCommand { Trigger = "Alt+NumPad9", Description = "Подскажите пожалуйста Ваш контактный номер телефона.", Type = "Reply", Content = "Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения." },
                new CustomCommand { Trigger = "Alt+N", Description = "Подскажите пожалуйста Ваш контактный номер телефона.", Type = "Reply", Content = "Подскажите пожалуйста Ваш контактный номер телефона.\nЭто необходимо для регистрации Вашего обращения." },
                new CustomCommand { Trigger = "Alt+Multiply", Description = "Уточняем информацию по Вашему вопросу.", Type = "Reply", Content = "Уточняем информацию по Вашему вопросу." },
                new CustomCommand { Trigger = "Alt+X", Description = "Уточняем информацию по Вашему вопросу.", Type = "Reply", Content = "Уточняем информацию по Вашему вопросу." },
                new CustomCommand { Trigger = "Alt+Add", Description = "Чем могу Вам помочь?", Type = "Reply", Content = "Чем могу Вам помочь?" },
                new CustomCommand { Trigger = "Alt+F", Description = "Чем могу Вам помочь?", Type = "Reply", Content = "Чем могу Вам помочь?" },
                new CustomCommand { Trigger = "Alt+Z", Description = "Закрываем (выполнена)", Type = "Reply", Content = "Спасибо за обращение в iikoService и хорошего Вам дня.\nЗаявку закрываем как выполненную.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно." },
                new CustomCommand { Trigger = "Alt+Shift+Z", Description = "От вас не поступила обратная связь.", Type = "Reply", Content = "От вас не поступила обратная связь.\nСпасибо за обращение в iikoService и хорошего Вам дня.\nЕсли возникнут трудности или дополнительные вопросы, просим обратиться к нам повторно.\nЗаявку закрываем." },
                new CustomCommand { Trigger = "Alt+B", Description = "Закрываем (нет вопросов)", Type = "Reply", Content = "В связи с тем, что дополнительных вопросов от вас не поступало, данное обращение закрываем.\nЕсли у вас остались вопросы, при создании новой заявки, просим указать номер текущей.\nСпасибо за обращение в iikoService и хорошего Вам дня!" },
                new CustomCommand { Trigger = "Alt+Divide", Description = "Сообщить о платных работах", Type = "Reply", Content = "Добрый день, вы обратились в техническую поддержку iikoService.  \nК сожалению, с Вашей организацией не заключен договор технической поддержки.\nРаботы могут быть выполнены только на платной основе.\n\nСтоимость работ: руб.\nВы согласны на платные работы?" },
                new CustomCommand { Trigger = "Alt+V", Description = "Обращение актуально?", Type = "Reply", Content = "Добрый день! \nОт Вас не поступила обратная связь по данному обращению. \nПодскажите пожалуйста, Ваше обращение актуально?" },

                // --- SYSTEM COMMANDS ---
                new CustomCommand { Trigger = "Alt+Shift+8", Description = "cmd duplicate (список)", Type = "System", Content = "OPEN_CRM_DIALOG" },
                new CustomCommand { Trigger = "Alt+Space", Description = "Исправить раскладку (выделенное)", Type = "System", Content = "FixLayout" },
                new CustomCommand { Trigger = "Alt+Q", Description = "Очистить очередь", Type = "System", Content = "ClearQueue" },
            };
        }
    }
}