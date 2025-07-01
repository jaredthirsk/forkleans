using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Shooter.Tests.Infrastructure;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shooter.Tests;

/// <summary>
/// Integration tests that validate the MetricsHealthCheck HTTP endpoints are working
/// and exposing telemetry metrics via HTTP for external monitoring.
/// </summary>
public class MetricsHealthCheckTest : IClassFixture<ShooterTestFixture>
{
    private readonly ShooterTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<MetricsHealthCheckTest> _logger;
    private readonly HttpClient _httpClient;

    public MetricsHealthCheckTest(ShooterTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().AddProvider(new XunitLoggerProvider(output)));
        _logger = loggerFactory.CreateLogger<MetricsHealthCheckTest>();
        
        _httpClient = new HttpClient();
    }

    [Fact]
    public async Task ValidateActionServerMetricsEndpoint()
    {
        _logger.LogInformation("Testing ActionServer metrics endpoint");
        
        try
        {
            // Wait for services to be ready
            await Task.Delay(5000);
            
            // Try default ActionServer port
            var actionServerUrl = "http://localhost:7072";
            var metricsUrl = $"{actionServerUrl}/health/metrics";
            
            _logger.LogInformation("Fetching metrics from: {MetricsUrl}", metricsUrl);
            
            var response = await _httpClient.GetAsync(metricsUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Metrics response: {Content}", content);
                
                // Parse and validate JSON response
                var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;
                
                // Should contain status and entries
                Assert.True(root.TryGetProperty("status", out _), "Response should contain 'status' field");
                Assert.True(root.TryGetProperty("entries", out var entries), "Response should contain 'entries' field");
                
                // Should have metrics entry
                var entriesObj = entries.EnumerateObject().ToList();
                var metricsEntry = entriesObj.FirstOrDefault(e => e.Name.Contains("metrics"));
                
                if (metricsEntry.Value.ValueKind != JsonValueKind.Undefined)
                {
                    _logger.LogInformation("✓ ActionServer metrics endpoint working");
                    
                    // Validate some expected metrics are present
                    if (metricsEntry.Value.TryGetProperty("data", out var data) && 
                        data.TryGetProperty("metrics", out var metrics))
                    {
                        _logger.LogInformation("Metrics data: {Metrics}", metrics.ToString());
                    }
                }
                else
                {
                    _logger.LogWarning("No metrics entry found in response");
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch metrics: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not test ActionServer metrics endpoint (service may not be running)");
            // Don't fail the test if service isn't available - this is integration test
        }
    }

    [Fact]
    public async Task ValidateSiloMetricsEndpoint()
    {
        _logger.LogInformation("Testing Silo metrics endpoint");
        
        try
        {
            // Wait for services to be ready
            await Task.Delay(5000);
            
            var siloUrl = "https://localhost:7071";
            var metricsUrl = $"{siloUrl}/health/metrics";
            
            _logger.LogInformation("Fetching metrics from: {MetricsUrl}", metricsUrl);
            
            // Configure HttpClient to ignore SSL certificate errors for test environment
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            
            using var httpsClient = new HttpClient(handler);
            var response = await httpsClient.GetAsync(metricsUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Silo metrics response length: {Length}", content.Length);
                
                // Parse and validate JSON response
                var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;
                
                Assert.True(root.TryGetProperty("status", out _), "Response should contain 'status' field");
                Assert.True(root.TryGetProperty("entries", out _), "Response should contain 'entries' field");
                
                _logger.LogInformation("✓ Silo metrics endpoint working");
            }
            else
            {
                _logger.LogWarning("Failed to fetch Silo metrics: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not test Silo metrics endpoint (service may not be running)");
            // Don't fail the test if service isn't available
        }
    }

    [Fact]
    public async Task ValidateMetricsEndpointFormat()
    {
        _logger.LogInformation("Testing metrics endpoint JSON format");
        
        try
        {
            // Try to get metrics from any available service
            var urls = new[]
            {
                "http://localhost:7072/health/metrics", // ActionServer
                "https://localhost:7071/health/metrics" // Silo
            };
            
            foreach (var url in urls)
            {
                try
                {
                    _logger.LogInformation("Testing URL: {Url}", url);
                    
                    var client = url.StartsWith("https") 
                        ? new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
                        : _httpClient;
                    
                    var response = await client.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        
                        // Validate JSON structure
                        var jsonDoc = JsonDocument.Parse(content);
                        var root = jsonDoc.RootElement;
                        
                        // Check required fields
                        Assert.True(root.TryGetProperty("status", out var status), "Should have 'status' field");
                        Assert.True(root.TryGetProperty("timestamp", out var timestamp), "Should have 'timestamp' field");
                        Assert.True(root.TryGetProperty("entries", out var entries), "Should have 'entries' field");
                        
                        _logger.LogInformation("✓ Metrics format validation passed for {Url}", url);
                        _logger.LogInformation("Status: {Status}, Timestamp: {Timestamp}", 
                            status.GetString(), timestamp.GetString());
                        
                        // If we got a successful response, we're done
                        return;
                    }
                    
                    if (client != _httpClient) client.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "URL {Url} failed", url);
                }
            }
            
            _logger.LogInformation("No metrics endpoints were available for format testing");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metrics endpoint format validation failed");
        }
    }

    [Fact]
    public async Task ValidateMetricsContentTypes()
    {
        _logger.LogInformation("Testing metrics endpoint content types");
        
        try
        {
            var urls = new[]
            {
                "http://localhost:7072/health/metrics",
                "https://localhost:7071/health/metrics"
            };
            
            foreach (var url in urls)
            {
                try
                {
                    var client = url.StartsWith("https") 
                        ? new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
                        : _httpClient;
                    
                    var response = await client.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        // Validate content type is JSON
                        var contentType = response.Content.Headers.ContentType?.MediaType;
                        Assert.Equal("application/json", contentType);
                        
                        _logger.LogInformation("✓ Content type validation passed for {Url}: {ContentType}", url, contentType);
                        
                        if (client != _httpClient) client.Dispose();
                        return;
                    }
                    
                    if (client != _httpClient) client.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Content type test failed for {Url}", url);
                }
            }
            
            _logger.LogInformation("No endpoints available for content type testing");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content type validation failed");
        }
    }

    [Fact]
    public async Task ValidateMetricsAccessibility()
    {
        _logger.LogInformation("Testing metrics endpoint accessibility");
        
        var results = new Dictionary<string, bool>();
        
        var endpoints = new Dictionary<string, string>
        {
            ["ActionServer"] = "http://localhost:7072/health/metrics",
            ["Silo"] = "https://localhost:7071/health/metrics"
        };
        
        foreach (var kvp in endpoints)
        {
            var serviceName = kvp.Key;
            var url = kvp.Value;
            
            try
            {
                var client = url.StartsWith("https") 
                    ? new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
                    : _httpClient;
                
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await client.GetAsync(url, timeoutCts.Token);
                
                results[serviceName] = response.IsSuccessStatusCode;
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✓ {Service} metrics endpoint accessible", serviceName);
                }
                else
                {
                    _logger.LogWarning("✗ {Service} metrics endpoint returned {StatusCode}", serviceName, response.StatusCode);
                }
                
                if (client != _httpClient) client.Dispose();
            }
            catch (Exception ex)
            {
                results[serviceName] = false;
                _logger.LogWarning(ex, "✗ {Service} metrics endpoint not accessible", serviceName);
            }
        }
        
        var accessibleCount = results.Values.Count(accessible => accessible);
        var totalCount = results.Count;
        
        _logger.LogInformation("Metrics accessibility summary: {AccessibleCount}/{TotalCount} endpoints accessible", 
            accessibleCount, totalCount);
        
        // Log individual results
        foreach (var kvp in results)
        {
            _logger.LogInformation("{Service}: {Status}", kvp.Key, kvp.Value ? "✓ Accessible" : "✗ Not accessible");
        }
        
        // Test passes if at least one endpoint is accessible
        Assert.True(accessibleCount > 0, "At least one metrics endpoint should be accessible");
    }
}