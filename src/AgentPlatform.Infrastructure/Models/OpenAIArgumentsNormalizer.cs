using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentPlatform.Infrastructure.Models;

/// <summary>
/// Delegating handler that normalizes the OpenAI/Agnes chat-completion request shape.
///
/// Semantic Kernel 1.30 echoes an assistant <c>tool_calls[].function.arguments</c> in several shapes that
/// Agnes rejects with HTTP 400: a JSON <b>string</b> containing a JSON object (OpenAI wire format), a
/// literal <b>null</b> (empty <c>KernelArguments</c>), or the string <c>"null"</c>. Agnes deserializes
/// <c>arguments</c> as a String and then parses its content as JSON, so any null / non-object / raw-map
/// value fails. This handler rewrites every such <c>arguments</c> value into a JSON <b>string</b> whose
/// content is a valid JSON object literal (<c>"{}"</c> for the empty case) before the request leaves the
/// process, leaving all other fields untouched.
///
/// The transformation is purely structural: it does not alter the semantic content of the call, so the
/// agent's ReAct loop is unaffected.
/// </summary>
internal sealed class OpenAIArgumentsNormalizer : DelegatingHandler
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = false };

    public OpenAIArgumentsNormalizer(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null ||
            !request.RequestUri?.AbsolutePath.Contains("chat/completions", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        string body;
        try
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return await base.SendAsync(request, cancellationToken);
        }

        string? normalized = NormalizeBody(body);
        if (normalized is null)
        {
            // Not a parseable chat body, or nothing to rewrite — forward as-is.
            return await base.SendAsync(request, cancellationToken);
        }

        // Replace the request body. Use ByteArrayContent (not StringContent) and clear the cached
        // Content-Length: SK/OpenAI's pipeline pre-computes Content-Length for the original body, and
        // StringContent would otherwise carry the stale length, causing a "Sent N bytes but
        // Content-Length promised M" mismatch. ByteArrayContent lets HttpClient recompute it.
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var newContent = new ByteArrayContent(bytes);
        newContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        foreach (var header in request.Content.Headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        request.Content = newContent;
        request.Content.Headers.ContentLength = bytes.Length;

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Parses the request body and rewrites <c>tool_calls[].function.arguments</c> into the shape Agnes
    /// accepts: a JSON <b>string</b> whose content is a valid JSON object literal (e.g. <c>"{}"</c> or
    /// <c>"{\"path\":\".\"}"</c>). Returns <c>null</c> when the body is not JSON or contains nothing to
    /// normalize.
    /// </summary>
    private static string? NormalizeBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject obj) return null;
        if (obj["messages"] is not JsonArray messages) return null;

        bool changed = false;
        foreach (var msg in messages)
        {
            if (msg is not JsonObject message) continue;
            if (message["tool_calls"] is not JsonArray toolCalls) continue;

            foreach (var tc in toolCalls)
            {
                if (tc is not JsonObject toolCall) continue;
                if (toolCall["function"] is not JsonObject function) continue;

                var argNode = function["arguments"];

                // Agnes (unlike a strict OpenAI endpoint) deserializes `arguments` as a String and then
                // parses its content as JSON — it rejects null, "null" and raw object/map values with 400.
                // Normalize every shape into a JSON *string* whose content is a valid JSON object literal.
                string normalizedArgs;
                if (argNode is JsonValue argValue)
                {
                    if (argValue.TryGetValue<string>(out var argStr))
                    {
                        // Keep a string only if its content is a valid JSON object; otherwise emit "{}".
                        normalizedArgs = IsJsonObjectText(argStr) ? argStr : "{}";
                    }
                    else
                    {
                        // Literal null/number/bool -> empty object text.
                        normalizedArgs = "{}";
                    }
                }
                else if (argNode is JsonObject or JsonArray)
                {
                    // Raw object/array -> serialize to its string form, e.g. {} -> "{}".
                    normalizedArgs = argNode.ToJsonString();
                }
                else
                {
                    // Missing or explicit null node.
                    normalizedArgs = "{}";
                }

                function["arguments"] = normalizedArgs;
                changed = true;
            }
        }

        return changed ? root.ToJsonString(_indented) : null;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="text"/> parses as a JSON object literal (not null/array/scalar).
    /// </summary>
    private static bool IsJsonObjectText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            return JsonNode.Parse(text) is JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
