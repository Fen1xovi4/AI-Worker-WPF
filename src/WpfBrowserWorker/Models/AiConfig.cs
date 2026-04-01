namespace WpfBrowserWorker.Models;

public class AiConfig
{
    // "openai" | "deepseek"
    public string Provider { get; set; } = "openai";

    public string OpenAiKey   { get; set; } = string.Empty;
    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    public string DeepSeekKey   { get; set; } = string.Empty;
    public string DeepSeekModel { get; set; } = "deepseek-chat";
}
