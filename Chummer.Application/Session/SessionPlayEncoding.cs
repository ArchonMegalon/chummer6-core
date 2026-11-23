using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public static class SessionPlayEncoding
{
    public static readonly JsonSerializerOptions Json = CreateJson();

    public static string OperationDigest(string kind, string overlayId, CharacterVersionReference version,
        IReadOnlyList<SessionEventEnvelope> events, string? cursor)
        => Digest(new { Kind = kind, OverlayId = overlayId, Version = version, Events = events, Cursor = cursor });

    private static JsonSerializerOptions CreateJson()
    {
        // The durable store checks typed reserialization digests. New optional sheet
        // evidence must not add default fields to already committed pre-v3 records.
        // Non-default evidence still participates in the current authority digest;
        // this does not rebind an old origin or authorize any mechanics from it.
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(CharacterSheetDerivedSection))
            {
                foreach (var property in info.Properties)
                    if (property.Name == nameof(CharacterSheetDerivedSection.UnlockedSkills))
                        property.ShouldSerialize = (_, value) => value is IReadOnlyList<CharacterSheetSkillUnlock> skills && skills.Count > 0;
            }
            else if (info.Type == typeof(CharacterSheetGrantedTrait))
            {
                foreach (var property in info.Properties)
                {
                    switch (property.Name)
                    {
                        case nameof(CharacterSheetGrantedTrait.MechanicsUnavailableReason):
                            property.ShouldSerialize = (_, value) => value is not null;
                            break;
                        case nameof(CharacterSheetGrantedTrait.OwnerId):
                        case nameof(CharacterSheetGrantedTrait.ParentMetatypeId):
                        case nameof(CharacterSheetGrantedTrait.SelectedMetatypeId):
                            property.ShouldSerialize = (_, value) => value is Guid id && id != Guid.Empty;
                            break;
                        case nameof(CharacterSheetGrantedTrait.Selection):
                            property.ShouldSerialize = (_, value) => value is string selection && selection.Length > 0;
                            break;
                        case nameof(CharacterSheetGrantedTrait.Rating):
                            property.ShouldSerialize = (_, value) => value is int rating && rating != 0;
                            break;
                    }
                }
            }
        });
        return new JsonSerializerOptions { MaxDepth = 32, TypeInfoResolver = resolver };
    }
    public static string Digest<T>(T value) => Hash(JsonSerializer.Serialize(value, Json));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string OverlayId(SessionPlayBinding binding) => "local-sr5-" + Hash(
        JsonSerializer.Serialize(new[] { binding.OwnerId, binding.Version.CharacterId }, Json));
    public static string Cursor(IEnumerable<SessionEventEnvelope> events) => "local:" + Digest(events.ToArray());
    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new SessionPlayException(SessionPlayFailures.Invalid(code));
    }
    public static void Conflict([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new SessionPlayException(SessionPlayFailures.Conflict(code));
    }
    public static void Identity(string? value, string code)
        => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(c => !char.IsControl(c)), code);
}
