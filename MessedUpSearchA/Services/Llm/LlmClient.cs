using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Llm;

/// <summary>Провайдеры с OpenAI-совместимым API. «Свой» — любой адрес того же формата (LM Studio, Ollama, свой прокси).</summary>
public static class LlmProviders
{
    public const string Groq = "Groq";
    public const string OpenRouter = "OpenRouter";
    public const string Custom = "Custom";

    public static readonly string[] All = [Groq, OpenRouter, Custom];

    public static string BaseUrl(string provider, string custom) => provider switch
    {
        Groq => "https://api.groq.com/openai/v1",
        OpenRouter => "https://openrouter.ai/api/v1",
        _ => custom.Trim().TrimEnd('/')
    };

    public static string KeyName(string provider) => "llm:" + provider.ToLowerInvariant();

    /// <summary>Что выбрать по умолчанию из списка провайдера: имена меняются, поэтому по частям имени.</summary>
    private static readonly string[] Preferred = ["llama-3.3-70b", "gemma-4-31b", "qwen3", "gemma", "llama"];

    public static string PickDefault(IReadOnlyList<string> models) =>
        Preferred.Select(p => models.FirstOrDefault(m => m.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(m => m is not null) ?? models.FirstOrDefault() ?? string.Empty;
}

public class LlmException(string message, bool isAuth = false) : Exception(message)
{
    public bool IsAuth { get; } = isAuth;
}

/// <summary>Минимальный клиент OpenAI-совместимого API: список моделей и один ответ на сообщения.</summary>
public class LlmClient(string baseUrl, string apiKey, string model = "")
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/models");
        Authorize(request);
        using var doc = JsonDocument.Parse(await SendAsync(request, ct));

        var ids = doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(m => m.GetProperty("id").GetString() ?? string.Empty)
            .Where(id => id.Length > 0)
            .ToList();

        // Бесплатные модели OpenRouter — вперёд: с ними можно начать без денег на счету.
        return ids.OrderByDescending(id => id.EndsWith(":free")).ThenBy(id => id).ToList();
    }

    public async Task<string> CompleteAsync(string system, string user, double temperature, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model,
            temperature,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        Authorize(request);

        using var doc = JsonDocument.Parse(await SendAsync(request, ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? string.Empty;
    }

    private void Authorize(HttpRequestMessage request)
    {
        if (apiKey.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        // OpenRouter просит назвать приложение — так запросы видны в его статистике.
        request.Headers.TryAddWithoutValidation("X-Title", "MessedUpSearch");
    }

    private static async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmException("timeout");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return body;

            var status = (int)response.StatusCode;
            throw new LlmException($"{status}: {ErrorMessage(body)}", status is 401 or 403);
        }
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
                return error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m)
                    ? m.GetString() ?? body
                    : error.ToString();
        }
        catch (JsonException)
        {
        }

        return body.Length > 200 ? body[..200] : body;
    }
}
