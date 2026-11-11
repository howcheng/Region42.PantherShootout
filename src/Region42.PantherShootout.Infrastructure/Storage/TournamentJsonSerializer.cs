using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Canonical JSON serializer for Panther Shootout storage documents (camelCase, indented, UTF-8 without BOM).
/// </summary>
public static class TournamentJsonSerializer
{
	public static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};

	public static string Serialize<T>(T value)
	{
		return JsonSerializer.Serialize(value, Options);
	}

	public static byte[] SerializeToUtf8Bytes<T>(T value)
	{
		return Utf8WithoutBom.GetBytes(Serialize(value));
	}

	public static T Deserialize<T>(string json)
	{
		ArgumentNullException.ThrowIfNull(json);

		return JsonSerializer.Deserialize<T>(json, Options)
			?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}.");
	}

	public static Task<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(stream);

		return DeserializeInternalAsync<T>(stream, cancellationToken);
	}

	private static async Task<T> DeserializeInternalAsync<T>(Stream stream, CancellationToken cancellationToken)
	{
		return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
			?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}.");
	}
}
