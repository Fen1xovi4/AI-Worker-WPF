using System.ComponentModel.DataAnnotations;

namespace WpfBrowserWorker.Data.Entities;

public class TelegramBotConfig
{
    public int Id { get; set; }

    // One bot per account
    public int AccountId { get; set; }

    [MaxLength(256)]
    public string BotToken { get; set; } = string.Empty;

    // Fetched from Telegram on connect (e.g. "@my_bot")
    [MaxLength(128)]
    public string? BotUsername { get; set; }

    public bool IsActive { get; set; } = true;

    // Last chat that sent a message — used to reply
    public long? LastChatId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public StoredAccount Account { get; set; } = null!;
}
