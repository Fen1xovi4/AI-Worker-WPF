using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using WpfBrowserWorker.Models;

namespace WpfBrowserWorker.Services;

/// <summary>
/// Generates platform-specific post text using OpenAI or DeepSeek.
/// Supports vision (image analysis) when provider is OpenAI or DeepSeek-VL.
/// </summary>
public class AiContentService
{
    private readonly AiConfig _config;
    private readonly HttpClient _http;

    public AiContentService(AiConfig config)
    {
        _config = config;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>
    /// Generate a post for the given platform.
    /// </summary>
    /// <param name="platform">instagram | threads | facebook | tiktok | twitter | x | other</param>
    /// <param name="language">ru | en | de | fr | es | …</param>
    /// <param name="userPrompt">Instruction from the Telegram message</param>
    /// <param name="imageBytes">Optional image from Telegram</param>
    public async Task<string> GeneratePostAsync(
        string    platform,
        string    language,
        string    userPrompt,
        byte[]?   imageBytes = null)
    {
        var (endpoint, apiKey, model) = ResolveProvider();
        var supportsVision = SupportsVision(model);

        var systemPrompt = BuildSystemPrompt(platform, language);
        var userContent  = BuildUserContent(userPrompt, imageBytes, supportsVision);

        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userContent  }
            },
            max_tokens = 1024,
            temperature = 0.8
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(requestBody,
            options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Log.Debug("AiContentService: POST {Endpoint} model={Model} platform={Platform}", endpoint, model, platform);

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"AI API error {(int)response.StatusCode}: {err}");
        }

        var json   = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = json.GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? string.Empty;

        Log.Information("AiContentService: generated {Chars} chars for [{Platform}]", result.Length, platform);
        return result.Trim();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private (string endpoint, string apiKey, string model) ResolveProvider() =>
        _config.Provider.Equals("deepseek", StringComparison.OrdinalIgnoreCase)
            ? ("https://api.deepseek.com/v1/chat/completions", _config.DeepSeekKey, _config.DeepSeekModel)
            : ("https://api.openai.com/v1/chat/completions",   _config.OpenAiKey,   _config.OpenAiModel);

    /// <summary>
    /// Only models that advertise vision support get image input.
    /// DeepSeek-Chat (text-only) silently ignores images — better to skip.
    /// </summary>
    private static bool SupportsVision(string model) =>
        model.Contains("gpt-4o",   StringComparison.OrdinalIgnoreCase) ||
        model.Contains("gpt-4-v",  StringComparison.OrdinalIgnoreCase) ||
        model.Contains("vl",       StringComparison.OrdinalIgnoreCase) || // deepseek-vl2
        model.Contains("vision",   StringComparison.OrdinalIgnoreCase);

    private static string BuildSystemPrompt(string platform, string language)
    {
        var langName = language.ToLower() switch
        {
            "ru" => "русском",
            "en" => "английском",
            "de" => "немецком",
            "fr" => "французском",
            "es" => "испанском",
            _    => language
        };

        var style = platform.ToLower() switch
        {
            "threads" or "twitter" or "x" =>
                "Пиши в стиле Twitter/Threads: живо, коротко, по делу. " +
                "1–3 предложения. Разговорный тон. Максимум 2 хэштега. Никаких длинных описаний.",

            "instagram" =>
                "Пиши в стиле Instagram: вовлекающий текст, красивые образы, немного личного. " +
                "2–4 предложения. В конце — новая строка, затем 5–8 релевантных хэштегов через пробел.",

            "facebook" =>
                "Пиши в стиле Facebook: доверительный тон, 2–4 предложения, " +
                "можно призыв к действию. 2–3 хэштега.",

            "tiktok" =>
                "Пиши подпись для TikTok: энергично, молодёжно, 1–2 предложения + 3–5 хэштегов.",

            _ =>
                "Пиши увлекательный пост для социальных сетей. 2–3 предложения."
        };

        return $"Ты профессиональный SMM-специалист. Пиши на {langName} языке.\n\n{style}\n\n" +
               "Отвечай ТОЛЬКО текстом поста — без вступлений, пояснений и кавычек.\n" +
               "Никогда не используй длинное тире (—), только обычный дефис (-) или запятую.\n" +
               "Смайлики используй только самые распространённые (из базового Unicode): 😊 😂 ❤️ 🔥 👍 😍 🙌 💪 ✨ 🐱 🐶 🌸 ☕ 📸 и подобные.";
    }

    private static object BuildUserContent(string userPrompt, byte[]? imageBytes, bool supportsVision)
    {
        if (imageBytes is null || !supportsVision)
            return userPrompt;

        // OpenAI vision content array
        var base64 = Convert.ToBase64String(imageBytes);
        return new object[]
        {
            new { type = "text",      text      = userPrompt },
            new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64}" } }
        };
    }
}
