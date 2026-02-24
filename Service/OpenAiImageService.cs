using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace GeoApi.Services;

public class OpenAiImageService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiImageService> _logger;
    private readonly string? _apiKey;
    private readonly bool _enabled;
    private static int _disabledDueToBilling;
    private static bool IsDisabledDueToBilling => Volatile.Read(ref _disabledDueToBilling) == 1;

    public OpenAiImageService(HttpClient httpClient, ILogger<OpenAiImageService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var enabledEnv = Environment.GetEnvironmentVariable("OPENAI_ENABLED");
        _enabled = string.IsNullOrWhiteSpace(enabledEnv) || !enabledEnv.Equals("false", StringComparison.OrdinalIgnoreCase);
        
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Variable de entorno OPENAI_API_KEY no configurada");
        }
        if (!_enabled)
        {
            _logger.LogInformation("OpenAI image generation está deshabilitado por variable OPENAI_ENABLED=false");
        }
    }

    /// <summary>
    /// Genera una imagen de una luminaria basada en sus características
    /// </summary>
    public async Task<string?> GenerateLuminarioImageAsync(
        string barrio,
        string tecnologia,
        string potencia,
        CancellationToken ct = default)
    {
        // Si el servicio está deshabilitado o ya fue desactivado por límite de facturación, devolver fallback
        if (!_enabled || IsDisabledDueToBilling)
        {
            if (!_enabled)
                _logger.LogDebug("OpenAI deshabilitado por configuración; devolviendo fallback SVG.");
            if (IsDisabledDueToBilling)
                _logger.LogWarning("OpenAI deshabilitado debido a límite de facturación previo; devolviendo fallback SVG.");
            return GenerateFallbackSvgDataUrl(barrio, tecnologia, potencia);
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("OpenAI API Key no configurada");
            return GenerateFallbackSvgDataUrl(barrio, tecnologia, potencia);
        }

        try
        {
            var prompt = BuildPrompt(barrio, tecnologia, potencia);
            _logger.LogInformation($"Generando imagen para luminaria en {barrio}");

            var requestBody = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = 1,
                size = "1024x1024",
                quality = "standard",
                style = "natural",
                response_format = "url"
            };

            var requestContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations")
            {
                Content = requestContent
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                // Intentamos detectar el código de error para actuar en consecuencia
                try
                {
                    using var errDoc = JsonDocument.Parse(errorContent);
                    if (errDoc.RootElement.TryGetProperty("error", out var err))
                    {
                        var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
                        if (string.Equals(code, "billing_hard_limit_reached", StringComparison.OrdinalIgnoreCase))
                        {
                            // Deshabilitar llamadas futuras y usar fallback
                            Interlocked.Exchange(ref _disabledDueToBilling, 1);
                            _logger.LogWarning("OpenAI: límite de facturación alcanzado. Se usará imagen fallback local.");
                            return GenerateFallbackSvgDataUrl(barrio, tecnologia, potencia);
                        }
                    }
                }
                catch
                {
                    // Fallthrough: si no se puede parsear, no mostrar el body completo en logs
                    _logger.LogError($"Error de OpenAI: {response.StatusCode} - respuesta no parseable");
                    return GenerateFallbackSvgDataUrl(barrio, tecnologia, potencia);
                }

                _logger.LogError($"Error de OpenAI: {response.StatusCode}");
                return GenerateFallbackSvgDataUrl(barrio, tecnologia, potencia);
            }

            var responseText = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataArray) && 
                dataArray.ValueKind == JsonValueKind.Array && 
                dataArray.GetArrayLength() > 0)
            {
                var firstImage = dataArray[0];
                if (firstImage.ValueKind == JsonValueKind.Object)
                {
                    if (firstImage.TryGetProperty("url", out var urlProperty))
                    {
                        var imageUrl = urlProperty.GetString();
                        _logger.LogInformation($"Imagen generada exitosamente (url)");
                        return imageUrl;
                    }

                    // Algunas APIs devuelven base64 en b64_json
                    if (firstImage.TryGetProperty("b64_json", out var b64Property))
                    {
                        var b64 = b64Property.GetString();
                        if (!string.IsNullOrWhiteSpace(b64))
                        {
                            var dataUrl = "data:image/png;base64," + b64;
                            _logger.LogInformation($"Imagen generada exitosamente (b64)");
                            return dataUrl;
                        }
                    }
                }
            }

            _logger.LogWarning("No se generó ninguna imagen (respuesta sin data). Usando fallback SVG.");
            return GenerateFallbackSvgDataUrl(barrio, tecnologia, potencia);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando imagen con OpenAI");
            return GenerateFallbackSvgDataUrl(barrio, tecnologia, potencia);
        }
    }

    private string GenerateFallbackSvgDataUrl(string barrio, string tecnologia, string potencia)
    {
        // Crear SVG simple dividido en dos columnas (día / noche) con texto y variantes por petición
        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        var ledColor = tecnologia?.Contains("LED", StringComparison.OrdinalIgnoreCase) == true ? "#ffffff" : "#ffb347";

        var svg = $@"<svg xmlns='http://www.w3.org/2000/svg' width='1600' height='800' viewBox='0 0 1600 800'>
  <defs>
    <linearGradient id='skyDay' x1='0' x2='0' y1='0' y2='1'>
      <stop offset='0' stop-color='#87CEEB'/>
      <stop offset='1' stop-color='#BFE9FF'/>
    </linearGradient>
    <linearGradient id='skyNight' x1='0' x2='0' y1='0' y2='1'>
      <stop offset='0' stop-color='#081730'/>
      <stop offset='1' stop-color='#001426'/>
    </linearGradient>
  </defs>
  <!-- Día -->
  <rect x='0' y='0' width='800' height='800' fill='url(#skyDay)'/>
  <!-- Poste -->
  <rect x='360' y='220' width='16' height='380' fill='#666'/>
  <circle cx='368' cy='210' r='30' fill='#999'/>
  <!-- Calle y casas (día) -->
  <rect x='0' y='600' width='800' height='200' fill='#ddd'/>
  <text x='20' y='760' font-size='22' fill='#333'>Día - {barrio} - {tecnologia} {potencia}</text>

  <!-- Noche -->
  <rect x='800' y='0' width='800' height='800' fill='url(#skyNight)'/>
  <rect x='1160' y='220' width='16' height='380' fill='#333'/>
  <!-- Luminaria encendida -->
  <g>
    <circle cx='1168' cy='210' r='28' fill='#222' />
    <circle cx='1168' cy='210' r='90' fill='{ledColor}' fill-opacity='0.08' />
    <circle cx='1168' cy='210' r='170' fill='{ledColor}' fill-opacity='0.02' />
  </g>
  <rect x='800' y='600' width='800' height='200' fill='#111'/>
  <text x='820' y='760' font-size='22' fill='#fff'>Noche - {barrio} - {tecnologia} {potencia}</text>

  <!-- Divider -->
  <line x1='800' y1='0' x2='800' y2='800' stroke='#444' stroke-width='2' />

  <!-- Footer info -->
  <text x='20' y='30' font-size='14' fill='#222'>Simulación local (fallback) - id:{nonce} - {timestamp}</text>
</svg>";

        var dataUrl = "data:image/svg+xml;utf8," + Uri.EscapeDataString(svg);
        return dataUrl;
    }

    private string BuildPrompt(string barrio, string tecnologia, string potencia)
    {
        // Añadir variabilidad al prompt con detalles específicos
        var randomElement = DateTime.Now.Millisecond % 3;
        var condicionClima = randomElement switch
        {
            0 => "cielo despejado",
            1 => "cielo nublado",
            _ => "cielo con algunas nubes"
        };

        var tipoAmbiente = (DateTime.Now.Second % 2) switch
        {
            0 => "zona residencial tranquila",
            _ => "avenida comercial"
        };

        return $@"Genera una imagen realista DIVIDIDA EN DOS MITADES:

LADO IZQUIERDO (DÍA):
- Luminaria de {potencia} montada en un poste gris metálico estándar
- La luminaria está APAGADA durante el día
- {tipoAmbiente} del barrio {barrio}
- Iluminación natural del día (mediodía)
- Tecnología: {tecnologia}
- Ambiente amigable, calles limpias

LADO DERECHO (NOCHE):
- LA MISMA luminaria en EL MISMO poste en la MISMA ubicación
- La luminaria está ENCENDIDA por la noche
- Luz {(tecnologia.Contains("LED", StringComparison.OrdinalIgnoreCase) ? "blanca neutra y brillante" : "anaranjada cálida")} iluminando toda la calle
- {condicionClima}
- Hora: 20:00 (8 PM)
- Se pueden ver: casas, árboles, personas a distancia iluminadas por la luminaria

ESPECIFICACIONES TÉCNICAS:
- Potencia: {potencia}
- Barrio: {barrio}
- La imagen debe mostrar claramente el contraste día/noche
- Fotografía realista, estilo urbano latino colombiano
- Proporción: 2 escenas lado a lado en una sola imagen

La imagen debe ser como un before/after mostrando cómo la luminaria ilumina la calle de noche.";
    }
}
