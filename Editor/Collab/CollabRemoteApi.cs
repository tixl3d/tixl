#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace T3.Editor.Collab;

/// <summary>
/// The HTTP side of remote management: the backend's <c>remote-*</c> ops. It holds no UI state;
/// every call either returns a parsed record or throws with the backend's message, so the state
/// machine in <see cref="CollabRemote"/> can decide what the window shows.
///
/// Controller calls carry the Discord access token (the backend verifies it once and caches the
/// account); installation calls carry the device id and secret issued when the pairing code was
/// redeemed. Both paths are plain HTTPS JSON, like the session browser.
/// </summary>
internal static class CollabRemoteApi
{
    public sealed record PairStart(string Code, DateTime ExpiresUtc, string Owner);

    public enum PairState
    {
        Waiting,
        Paired,
        Expired,
        GaveUp,
    }

    public sealed record PairStatus(PairState State, string DeviceId, string DeviceName, string Project);

    public sealed record Device(string Id, string Name, string Project, string Version, string Owner,
                                bool Enabled, bool Online, long LastSeen, string State, string Room, string Claim);

    public sealed record ConnectResult(string Claim, string Room, string Command);

    public sealed record HeartbeatResult(bool Enabled, string Owner, string CommandType, string Claim, string CommandId);

    public sealed record RedeemResult(string DeviceId, string Secret, string Owner, string Name);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TiXLCollab/1.0");
        return client;
    }

    private static async Task<JObject> PostAsync(string op, JObject payload, CancellationToken cancellation)
    {
        var url = CollabWebLobby.BackendUrl;
        if (url.Length == 0)
            throw new InvalidOperationException("No party backend configured (Settings > Collaboration > Advanced).");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?op={op}");
            request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request, cancellation);
            var text = await response.Content.ReadAsStringAsync(cancellation);

            JObject json;
            try
            {
                json = JObject.Parse(text);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                throw new InvalidOperationException("the backend answered with something that is not JSON");
            }

            if (!(json["ok"]?.Value<bool>() ?? false))
                throw new InvalidOperationException(json["error"]?.Value<string>() ?? "the backend refused the request");

            return json;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new InvalidOperationException($"the backend is unreachable ({e.Message})");
        }
    }

    private static JObject OwnerPayload(string token)
        => new() { ["token"] = token ?? "" };

    // ---------------------------------------------------------------------------------------
    // Controller (the logged-in owner)
    // ---------------------------------------------------------------------------------------

    public static async Task<PairStart> StartPairingAsync(string token, CancellationToken cancellation)
    {
        var json = await PostAsync("remote-pair-start", OwnerPayload(token), cancellation);
        var code = json["code"]?.Value<string>() ?? "";
        if (code.Length == 0)
            throw new InvalidOperationException("the backend sent no pairing code");

        var expires = json["expires"]?.Value<long>() ?? 0;
        return new PairStart(code,
                             DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime,
                             json["owner"]?.Value<string>() ?? "");
    }

    public static async Task<PairStatus> PairStatusAsync(string token, string code, CancellationToken cancellation)
    {
        var payload = OwnerPayload(token);
        payload["code"] = code;
        var json = await PostAsync("remote-pair-status", payload, cancellation);

        var state = json["state"]?.Value<string>() ?? "waiting";
        if (state == "paired" && json["device"] is JObject device)
        {
            return new PairStatus(PairState.Paired,
                                  device["id"]?.Value<string>() ?? "",
                                  device["name"]?.Value<string>() ?? "",
                                  device["project"]?.Value<string>() ?? "");
        }

        return state switch
                   {
                       "expired" => new PairStatus(PairState.Expired, "", "", ""),
                       "paired" => new PairStatus(PairState.GaveUp, "", "", ""),
                       _ => new PairStatus(PairState.Waiting, "", "", ""),
                   };
    }

    public static async Task<IReadOnlyList<Device>> ListAsync(string token, CancellationToken cancellation)
    {
        var json = await PostAsync("remote-list", OwnerPayload(token), cancellation);
        var devices = new List<Device>();
        foreach (var tokenEntry in json["devices"] ?? new JArray())
        {
            if (tokenEntry is not JObject entry)
                continue;

            var id = entry["id"]?.Value<string>() ?? "";
            if (id.Length == 0)
                continue;

            devices.Add(new Device(id,
                                   entry["name"]?.Value<string>() ?? "",
                                   entry["project"]?.Value<string>() ?? "",
                                   entry["version"]?.Value<string>() ?? "",
                                   entry["owner"]?.Value<string>() ?? "",
                                   entry["enabled"]?.Value<bool>() ?? true,
                                   entry["online"]?.Value<bool>() ?? false,
                                   entry["lastSeen"]?.Value<long>() ?? 0,
                                   entry["state"]?.Value<string>() ?? "idle",
                                   entry["room"]?.Value<string>() ?? "",
                                   entry["claim"]?.Value<string>() ?? ""));
        }

        return devices;
    }

    public static async Task<ConnectResult> ConnectAsync(string token, string deviceId, CancellationToken cancellation)
    {
        var payload = OwnerPayload(token);
        payload["device"] = deviceId;
        var json = await PostAsync("remote-connect", payload, cancellation);
        return new ConnectResult(json["claim"]?.Value<string>() ?? "",
                                 json["room"]?.Value<string>() ?? "",
                                 json["command"]?.Value<string>() ?? "");
    }

    public static async Task DisconnectAsync(string token, string deviceId, CancellationToken cancellation)
    {
        var payload = OwnerPayload(token);
        payload["device"] = deviceId;
        await PostAsync("remote-disconnect", payload, cancellation);
    }

    public static async Task AttachAsync(string token, string deviceId, string claim, CancellationToken cancellation)
    {
        var payload = OwnerPayload(token);
        payload["device"] = deviceId;
        payload["claim"] = claim;
        await PostAsync("remote-attach", payload, cancellation);
    }

    public static async Task SetAsync(string token, string deviceId, bool? enabled, string? name, CancellationToken cancellation)
    {
        var payload = OwnerPayload(token);
        payload["device"] = deviceId;
        if (enabled.HasValue)
            payload["enabled"] = enabled.Value;
        if (!string.IsNullOrEmpty(name))
            payload["name"] = name;

        await PostAsync("remote-set", payload, cancellation);
    }

    public static async Task ForgetAsync(string token, string deviceId, CancellationToken cancellation)
    {
        var payload = OwnerPayload(token);
        payload["device"] = deviceId;
        await PostAsync("remote-forget", payload, cancellation);
    }

    // ---------------------------------------------------------------------------------------
    // Installation (device secret, no Discord)
    // ---------------------------------------------------------------------------------------

    public static async Task<RedeemResult> RedeemAsync(string code, string name, string project, string version,
                                                       CancellationToken cancellation)
    {
        var payload = new JObject
                          {
                              ["code"] = code,
                              ["name"] = name,
                              ["project"] = project,
                              ["version"] = version,
                          };
        var json = await PostAsync("remote-pair-redeem", payload, cancellation);
        var device = json["device"]?.Value<string>() ?? "";
        var secret = json["secret"]?.Value<string>() ?? "";
        if (device.Length == 0 || secret.Length == 0)
            throw new InvalidOperationException("the backend sent an incomplete pairing");

        return new RedeemResult(device, secret,
                                json["owner"]?.Value<string>() ?? "",
                                json["name"]?.Value<string>() ?? name);
    }

    public static async Task<HeartbeatResult> HeartbeatAsync(string deviceId, string secret, string name, string project,
                                                             string version, string state, string ack,
                                                             CancellationToken cancellation)
    {
        var payload = DevicePayload(deviceId, secret);
        payload["name"] = name;
        payload["project"] = project;
        payload["version"] = version;
        payload["state"] = state;
        if (ack.Length > 0)
            payload["ack"] = ack;

        var json = await PostAsync("remote-heartbeat", payload, cancellation);
        var command = json["command"] as JObject;
        return new HeartbeatResult(json["enabled"]?.Value<bool>() ?? true,
                                   json["owner"]?.Value<string>() ?? "",
                                   command?["type"]?.Value<string>() ?? "",
                                   command?["claim"]?.Value<string>() ?? "",
                                   json["commandId"]?.Value<string>() ?? "");
    }

    public static async Task ReportAsync(string deviceId, string secret, string claim, string state, string room,
                                         CancellationToken cancellation)
    {
        var payload = DevicePayload(deviceId, secret);
        payload["claim"] = claim;
        payload["state"] = state;
        payload["room"] = room;
        await PostAsync("remote-report", payload, cancellation);
    }

    public static async Task UnpairAsync(string deviceId, string secret, CancellationToken cancellation)
    {
        await PostAsync("remote-unpair", DevicePayload(deviceId, secret), cancellation);
    }

    private static JObject DevicePayload(string deviceId, string secret)
        => new()
               {
                   ["device"] = deviceId,
                   ["secret"] = secret,
               };
}
