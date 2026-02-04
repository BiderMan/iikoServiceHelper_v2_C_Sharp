using System.Text.Json.Serialization;

namespace iikoServiceHelper.Models
{
    public class CustomCommand
    {
        public string Trigger { get; set; } = "";      // Хоткей, например "Alt+NumPad0"
        public string Description { get; set; } = "";  // Описание для таблицы
        public string Type { get; set; } = "Reply";    // Тип: "Reply" (текст) или "Bot" (команда бота)
        public string Content { get; set; } = "";      // Текст ответа или команда

        [JsonIgnore]
        public bool IsReadOnly { get; set; }
    }
}