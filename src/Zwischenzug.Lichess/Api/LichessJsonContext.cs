using System.Text.Json.Serialization;

namespace Zwischenzug.Lichess;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BotEvent))]
[JsonSerializable(typeof(GameEvent))]
[JsonSerializable(typeof(GameState))]
internal partial class LichessJsonContext : JsonSerializerContext
{
}
