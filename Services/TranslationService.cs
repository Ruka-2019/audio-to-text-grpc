namespace Services;

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class TranslationService
{
    private readonly IHttpClientFactory  _httpClientFactory;
    private readonly ILogger<TranslationService> _logger;
    private readonly string _subscriptionKey;
    private readonly string _endpoint;
    private readonly string _region;

    public TranslationService(IHttpClientFactory httpClientFactory,  ILogger<TranslationService> logger)
    {
        string configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\"));
        var builder = new ConfigurationBuilder()
            .SetBasePath(configPath)
            .AddJsonFile("config.json", optional: true, reloadOnChange: true);

        var Configuration = builder.Build();

        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _subscriptionKey = Configuration["AzureTranslatorService:SubscriptionKey"];
        _endpoint = Configuration["AzureTranslatorService:GlobalTextTranslationEndpoint"];
        _region = Configuration["AzureTranslatorService:ServiceRegion"];
    }

    public async Task<string> TranslateTextAsync(string text, string fromLanguage, string toLanguage)
    {
        var httpClient = _httpClientFactory.CreateClient();
        // Construct the request URL
        
        string route = $"/translate?api-version=3.0&to={toLanguage}";
        if (!String.IsNullOrEmpty(fromLanguage))
        {
            route = $"/translate?api-version=3.0&from={fromLanguage}&to={toLanguage}";
        }
        string requestUri = _endpoint + route;

        // Create the request body
        var requestBody = JsonConvert.SerializeObject(new[] { new { Text = text } });
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        // Set the headers
        httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Region", _region);

        // Send the request
        var response = await httpClient.PostAsync(requestUri, content);
        response.EnsureSuccessStatusCode();

        // Read and parse the response
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JsonConvert.DeserializeObject<dynamic>(responseBody);
        // var jsonString = JsonConvert.SerializeObject(result, Formatting.Indented); // Serialize the object to a JSON string for pretty printing

        // Assuming the result contains at least one translation
        _logger.LogInformation("Translate API ResponseBody: {result}", responseBody);
        return result[0].translations[0].text;
    }
}
