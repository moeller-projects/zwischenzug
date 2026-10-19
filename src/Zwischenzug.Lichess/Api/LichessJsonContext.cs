using System.Text.Json.Serialization;

namespace Zwischenzug.Lichess;

/// <summary>Compile-time JSON metadata avoids reflection-based serialization in trimmed and AOT builds.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BotEvent))]
[JsonSerializable(typeof(GameEvent))]
[JsonSerializable(typeof(GameState))]
internal partial class LichessJsonContext : JsonSerializerContext
{
}
