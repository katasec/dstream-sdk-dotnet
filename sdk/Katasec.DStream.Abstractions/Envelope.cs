namespace Katasec.DStream.Abstractions;

public readonly record struct Envelope(object Payload, IReadOnlyDictionary<string, object?> Meta);
