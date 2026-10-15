using System.Text.Json;
namespace DanceBridge.Core;

public sealed class ProfileLibrary
{
    public int Version { get; set; } = 1;
    public Guid ActiveId { get; set; }
    public List<Profile> Profiles { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public Profile Active => Profiles.Single(p => p.Id == ActiveId);
    public void Validate()
    {
        if (Version != 1 || Profiles.Count == 0 || Profiles.Select(p => p.Id).Distinct().Count() != Profiles.Count || !Profiles.Any(p => p.Id == ActiveId)) throw new FormatException("Invalid profile library.");
        foreach (var profile in Profiles) profile.Validate();
    }
}
public sealed class ProfileStore(string directory)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static Profile Clone(Profile profile) => Parse(JsonSerializer.Serialize(profile));
    public static Profile Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        bool legacy = !document.RootElement.TryGetProperty("Version", out var version);
        if (!legacy && version.GetInt32() != 2) throw new FormatException("This profile version is not supported.");
        var profile = JsonSerializer.Deserialize<Profile>(text) ?? throw new FormatException("Empty profile.");
        if (legacy) profile.EnabledPorts = [true, true, true, true]; // Preserve legacy global port behavior.
        profile.Validate(); return profile;
    }
    public ProfileLibrary Load()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "library.json");
        if (File.Exists(path)) { var library = JsonSerializer.Deserialize<ProfileLibrary>(File.ReadAllText(path)) ?? throw new FormatException("Empty library."); library.Validate(); return library; }
        var legacyPath = Path.Combine(directory, "profile.json");
        var profile = File.Exists(legacyPath) ? Parse(File.ReadAllText(legacyPath)) : new Profile();
        var created = new ProfileLibrary { ActiveId = profile.Id, Profiles = [profile] };
        Save(created);
        if (File.Exists(legacyPath) && !File.Exists(Path.Combine(directory,"legacy-profile.backup.json"))) File.Copy(legacyPath, Path.Combine(directory, "legacy-profile.backup.json"), false);
        return created;
    }
    public void Save(ProfileLibrary library)
    {
        library.Validate(); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "library.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(library, Json));
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }
    public static string Export(Profile profile) { profile.Validate(); return JsonSerializer.Serialize(profile, Json); }
}
