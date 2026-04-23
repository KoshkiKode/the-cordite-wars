using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CorditeWars.Core.Licensing;

/// <summary>
/// HTTPS client for the licensing API exposed by the AWS Lambda backend.
/// Routes:
///   POST /api/activate         — register/refresh a machine for a license
///   POST /api/deactivate       — release a slot
///   GET  /api/manage           — list slots for a license (out-of-band web UI)
///
/// Errors are returned as <see cref="LicenseClientResult"/> rather than
/// thrown so the gate can render a user-friendly message instead of a stack
/// trace, and so silent renewal failures don't crash the game.
/// </summary>
public sealed class LicenseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Construct with a base URL like <c>https://downloads.example.com</c>.
    /// The client appends <c>/api/...</c> to the base.</summary>
    public LicenseClient(string baseUrl, HttpClient? http = null, TimeSpan? timeout = null)
    {
        _baseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
        if (http != null)
        {
            _http = http;
            _ownsHttpClient = false;
        }
        else
        {
            _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
            _ownsHttpClient = true;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    public async Task<LicenseClientResult<ActivateResponse>> ActivateAsync(
        string formattedKey,
        string machineIdHex,
        string hostnameHint,
        CancellationToken ct = default)
    {
        var req = new ActivateRequest
        {
            Key = formattedKey,
            MachineId = machineIdHex,
            HostnameHint = hostnameHint,
        };
        return await PostAsync<ActivateRequest, ActivateResponse>("/api/activate", req, ct).ConfigureAwait(false);
    }

    public async Task<LicenseClientResult<ActivateResponse>> RenewAsync(
        string entitlementBase64Url,
        CancellationToken ct = default)
    {
        var req = new RenewRequest { EntitlementBase64Url = entitlementBase64Url };
        return await PostAsync<RenewRequest, ActivateResponse>("/api/renew", req, ct).ConfigureAwait(false);
    }

    public async Task<LicenseClientResult<object>> DeactivateAsync(
        string formattedKey,
        string machineIdHexToRelease,
        CancellationToken ct = default)
    {
        var req = new DeactivateRequest
        {
            Key = formattedKey,
            MachineIdToRelease = machineIdHexToRelease,
        };
        return await PostAsync<DeactivateRequest, object>("/api/deactivate", req, ct).ConfigureAwait(false);
    }

    private async Task<LicenseClientResult<TRes>> PostAsync<TReq, TRes>(string path, TReq body, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(_baseUrl + path, body, JsonOpts, ct).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                TRes? value = JsonSerializer.Deserialize<TRes>(text, JsonOpts);
                return LicenseClientResult<TRes>.Ok(value!);
            }
            // Try to extract a structured error.
            ErrorBody? err = null;
            try { err = JsonSerializer.Deserialize<ErrorBody>(text, JsonOpts); } catch { }
            return LicenseClientResult<TRes>.Failure(
                resp.StatusCode,
                err?.Error ?? "request_failed",
                err?.Message ?? text,
                err?.ActiveSlots);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return LicenseClientResult<TRes>.Failure(0, "timeout", "Request timed out.", null);
        }
        catch (HttpRequestException ex)
        {
            return LicenseClientResult<TRes>.Failure(0, "network_error", ex.Message, null);
        }
    }

    // --- Wire types ----------------------------------------------------------

    private sealed class ActivateRequest
    {
        [JsonPropertyName("key")]            public string Key { get; set; } = "";
        [JsonPropertyName("machine_id")]     public string MachineId { get; set; } = "";
        [JsonPropertyName("hostname_hint")]  public string HostnameHint { get; set; } = "";
    }

    private sealed class DeactivateRequest
    {
        [JsonPropertyName("key")]                    public string Key { get; set; } = "";
        [JsonPropertyName("machine_id_to_release")]  public string MachineIdToRelease { get; set; } = "";
    }

    private sealed class RenewRequest
    {
        [JsonPropertyName("entitlement_b64")]  public string EntitlementBase64Url { get; set; } = "";
    }

    public sealed class ActivateResponse
    {
        [JsonPropertyName("entitlement_b64")] public string EntitlementBase64Url { get; set; } = "";
        [JsonPropertyName("slot_index")]      public int SlotIndex { get; set; }
        [JsonPropertyName("issued_at")]       public long IssuedAt { get; set; }
        [JsonPropertyName("expires_at")]      public long ExpiresAt { get; set; }

        public byte[] DecodeBlob()
        {
            // Server returns base64-url with padding stripped; restore it.
            string s = EntitlementBase64Url.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }

    private sealed class ErrorBody
    {
        [JsonPropertyName("error")]         public string? Error { get; set; }
        [JsonPropertyName("message")]       public string? Message { get; set; }
        [JsonPropertyName("active_slots")]  public ActiveSlot[]? ActiveSlots { get; set; }
    }

    public sealed class ActiveSlot
    {
        [JsonPropertyName("slot_index")]    public int SlotIndex { get; set; }
        [JsonPropertyName("machine_id")]    public string MachineId { get; set; } = "";
        [JsonPropertyName("hostname_hint")] public string HostnameHint { get; set; } = "";
        [JsonPropertyName("last_seen")]     public long LastSeen { get; set; }
    }
}

public sealed class LicenseClientResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public HttpStatusCode StatusCode { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public LicenseClient.ActiveSlot[]? ActiveSlots { get; init; }

    public static LicenseClientResult<T> Ok(T value) => new()
    {
        Success = true,
        Value = value,
        StatusCode = HttpStatusCode.OK,
    };

    public static LicenseClientResult<T> Failure(
        HttpStatusCode status,
        string code,
        string? message,
        LicenseClient.ActiveSlot[]? slots)
        => new()
        {
            Success = false,
            StatusCode = status,
            ErrorCode = code,
            ErrorMessage = message,
            ActiveSlots = slots,
        };

    public bool IsMachineCapReached =>
        !Success && string.Equals(ErrorCode, "machine_cap_reached", StringComparison.Ordinal);
}
