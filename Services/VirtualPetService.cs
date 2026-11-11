using System;
using System.Windows.Threading;

namespace DynamicIsland.Services;

public enum PetMood
{
    Sleeping,
    Awake,
    Dancing,
    Happy,
    Thirsty,
    Stretch,
    Eating
}

public enum WellnessAlertKind
{
    Hydrate,
    Stretch,
    EyeRest
}

public sealed record WellnessAlert(
    WellnessAlertKind Kind,
    string Title,
    string Message,
    string Glyph,
    string ActionLabel);

/// <summary>
/// Service managing the Dynamic Island Virtual Capsule Companion (Desktop Pet),
/// state machine moods (Sleeping, Awake, Dancing, Happy), affection points,
/// and periodic wellness notifications (Hydration, Posture Stretch, 20-20-20 Eye Rest).
/// </summary>
internal sealed class VirtualPetService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer? _wellnessTimer;
    private readonly DispatcherTimer? _happyResetTimer;

    private DateTime _lastReminderTime = DateTime.UtcNow;
    private DateTime _lastHappyTime = DateTime.MinValue;
    private DateTime _lastEatingTime = DateTime.MinValue;
    private int _alertCycleIndex;
    private PetMood _currentMood = PetMood.Sleeping;

    public static readonly string[] PresetPetNames =
    {
        "Mochi", "Kitsune", "Boba", "Pixel", "Hachi", "Nimbus", "Draco", "Yuzu"
    };

    public PetMood CurrentMood
    {
        get => _currentMood;
        private set
        {
            if (_currentMood != value)
            {
                _currentMood = value;
                MoodChanged?.Invoke(_currentMood);
            }
        }
    }

    public string PetSpecies => _settings.Current.PetSpecies;
    public string PetAccessory => _settings.Current.PetAccessory;
    public string PetName => _settings.Current.PetName;
    public int AffectionScore => _settings.Current.PetAffectionScore;
    public int WaterCount => _settings.Current.PetWaterCount;
    public int StretchCount => _settings.Current.PetStretchCount;
    public int TreatCount => _settings.Current.PetTreatCount;
    public int ReminderIntervalMinutes => _settings.Current.WellnessReminderMinutes;
    public bool IsEnabled => _settings.Current.EnableVirtualPet;

    public event Action<PetMood>? MoodChanged;
    public event Action<WellnessAlert>? WellnessAlertTriggered;
    public event Action<int>? AffectionChanged;

    public VirtualPetService(Dispatcher dispatcher, SettingsService settings)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _wellnessTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, OnWellnessTimerTick, _dispatcher);
        _wellnessTimer.Start();

        _happyResetTimer = new DispatcherTimer(TimeSpan.FromSeconds(3.5), DispatcherPriority.Background, (_, _) =>
        {
            _happyResetTimer?.Stop();
            if (CurrentMood is PetMood.Happy or PetMood.Eating)
            {
                CurrentMood = PetMood.Awake;
            }
        }, _dispatcher);
    }

    private void OnWellnessTimerTick(object? sender, EventArgs e)
    {
        if (!IsEnabled || ReminderIntervalMinutes <= 0) return;

        var elapsed = DateTime.UtcNow - _lastReminderTime;
        if (elapsed >= TimeSpan.FromMinutes(ReminderIntervalMinutes))
        {
            _lastReminderTime = DateTime.UtcNow;
            TriggerNextWellnessAlert();
        }
    }

    public void CheckWellnessNow(DateTime nowUtc)
    {
        if (!IsEnabled || ReminderIntervalMinutes <= 0) return;

        if (nowUtc - _lastReminderTime >= TimeSpan.FromMinutes(ReminderIntervalMinutes))
        {
            _lastReminderTime = nowUtc;
            TriggerNextWellnessAlert();
        }
    }

    private void TriggerNextWellnessAlert()
    {
        WellnessAlert alert = (_alertCycleIndex % 3) switch
        {
            0 => new WellnessAlert(
                WellnessAlertKind.Hydrate,
                $"{PetName} says: Hydrate Time!",
                "Drink a fresh glass of water to stay energized.",
                "\uE958", // Droplet / Beverage
                "Drink 💧"),

            1 => new WellnessAlert(
                WellnessAlertKind.Stretch,
                $"{PetName} says: Stretch Break!",
                "Roll your shoulders, stand up and stretch for 30 seconds.",
                "\uE7C9", // Action / Health
                "Stretched 🧘"),

            _ => new WellnessAlert(
                WellnessAlertKind.EyeRest,
                $"{PetName} says: 20-20-20 Eye Rest!",
                "Look at an object 20 feet away for 20 seconds.",
                "\uE7B3", // View / Eye
                "Rested 👀")
        };

        _alertCycleIndex++;
        CurrentMood = PetMood.Thirsty;
        WellnessAlertTriggered?.Invoke(alert);
    }

    public string Pet()
    {
        int newScore = AffectionScore + 3;
        _settings.Update(s => s.PetAffectionScore = newScore, nameof(AppSettings.PetAffectionScore));
        SetHappyMood();
        AffectionChanged?.Invoke(newScore);

        return PetSpecies switch
        {
            "Dog" => $"🐾 {PetName} wagged happily and gave a joyful bark! (+3 ❤️)",
            "Bunny" => $"🐾 {PetName} wiggled ears and did a binky hop! (+3 ❤️)",
            "Fox" => $"🦊 {PetName} yipped softly and swished a fluffy tail! (+3 ❤️)",
            "Panda" => $"🐼 {PetName} rolled with glee and hugged your hand! (+3 ❤️)",
            "Dragon" => $"🐲 {PetName} fluttered wings and puffed golden sparks! (+3 ❤️)",
            _ => $"🐾 {PetName} purred softly and leaned into your hand! (+3 ❤️)"
        };
    }

    public string FeedTreat()
    {
        int count = TreatCount + 1;
        int newScore = AffectionScore + 8;
        _settings.Update(s =>
        {
            s.PetTreatCount = count;
            s.PetAffectionScore = newScore;
        }, nameof(AppSettings.PetTreatCount));

        _lastEatingTime = DateTime.UtcNow;
        CurrentMood = PetMood.Eating;
        _happyResetTimer?.Stop();
        _happyResetTimer?.Start();
        AffectionChanged?.Invoke(newScore);

        return PetSpecies switch
        {
            "Dog" => $"🦴 Fed {PetName} a crunchy golden bone! (+8 ❤️)",
            "Bunny" => $"🥕 Fed {PetName} a sweet garden carrot! (+8 ❤️)",
            "Fox" => $"🫐 Fed {PetName} wild forest berries! (+8 ❤️)",
            "Panda" => $"🎋 Fed {PetName} tender bamboo shoots! (+8 ❤️)",
            "Dragon" => $"✨ Fed {PetName} a glowing starfruit gem! (+8 ❤️)",
            _ => $"🍣 Fed {PetName} fresh salmon sushi! (+8 ❤️)"
        };
    }

    public string PerformTrick()
    {
        int newScore = AffectionScore + 5;
        _settings.Update(s => s.PetAffectionScore = newScore, nameof(AppSettings.PetAffectionScore));
        SetHappyMood();
        AffectionChanged?.Invoke(newScore);

        return PetSpecies switch
        {
            "Dog" => $"🎪 {PetName} did a mid-air Shiba somersault! (+5 ❤️)",
            "Bunny" => $"🎪 {PetName} pulled off a high-flying binky flip! (+5 ❤️)",
            "Fox" => $"🎪 {PetName} executed a mystical Kitsune pounce spin! (+5 ❤️)",
            "Panda" => $"🎪 {PetName} did an acrobatic kung-fu tumble! (+5 ❤️)",
            "Dragon" => $"🎪 {PetName} looped a celestial aerial barrel roll! (+5 ❤️)",
            _ => $"🎪 {PetName} landed a purr-fect acrobatic backflip! (+5 ❤️)"
        };
    }

    public string LogWater()
    {
        int count = WaterCount + 1;
        int newScore = AffectionScore + 10;
        _settings.Update(s =>
        {
            s.PetWaterCount = count;
            s.PetAffectionScore = newScore;
        }, nameof(AppSettings.PetWaterCount));

        _lastReminderTime = DateTime.UtcNow;
        SetHappyMood();
        AffectionChanged?.Invoke(newScore);

        return $"💧 Glass #{count} logged! {PetName} celebrates your hydration! (+10 ❤️)";
    }

    public string LogStretch()
    {
        int count = StretchCount + 1;
        int newScore = AffectionScore + 10;
        _settings.Update(s =>
        {
            s.PetStretchCount = count;
            s.PetAffectionScore = newScore;
        }, nameof(AppSettings.PetStretchCount));

        _lastReminderTime = DateTime.UtcNow;
        SetHappyMood();
        AffectionChanged?.Invoke(newScore);

        return $"🧘 Stretch break #{count} logged! {PetName} did a big stretch with you! (+10 ❤️)";
    }

    public void CycleSpecies()
    {
        var current = PetSpecies;
        string next = current switch
        {
            "Cat" => "Dog",
            "Dog" => "Bunny",
            "Bunny" => "Fox",
            "Fox" => "Panda",
            "Panda" => "Dragon",
            _ => "Cat"
        };
        _settings.Update(s => s.PetSpecies = next, nameof(AppSettings.PetSpecies));
        SetHappyMood();
    }

    public void SetSpecies(string species)
    {
        string normalized = SettingsService.NormalizePetSpecies(species);
        _settings.Update(s => s.PetSpecies = normalized, nameof(AppSettings.PetSpecies));
        SetHappyMood();
    }

    public void CycleAccessory()
    {
        string next = PetAccessory switch
        {
            "None" => "Crown",
            "Crown" => "WizardHat",
            "WizardHat" => "Headphones",
            "Headphones" => "Sunglasses",
            "Sunglasses" => "Halo",
            "Halo" => "Sprout",
            _ => "None"
        };
        _settings.Update(s => s.PetAccessory = next, nameof(AppSettings.PetAccessory));
        SetHappyMood();
    }

    public void SetAccessory(string accessory)
    {
        string normalized = SettingsService.NormalizePetAccessory(accessory);
        _settings.Update(s => s.PetAccessory = normalized, nameof(AppSettings.PetAccessory));
        SetHappyMood();
    }

    public string CyclePetName()
    {
        int idx = Array.FindIndex(PresetPetNames, n => string.Equals(n, PetName, StringComparison.OrdinalIgnoreCase));
        string next = PresetPetNames[(idx + 1) % PresetPetNames.Length];
        _settings.Update(s => s.PetName = next, nameof(AppSettings.PetName));
        SetHappyMood();
        return next;
    }

    public void CycleReminderInterval()
    {
        int next = ReminderIntervalMinutes switch
        {
            30 => 45,
            45 => 60,
            60 => 0,
            _ => 30
        };
        _settings.Update(s => s.WellnessReminderMinutes = next, nameof(AppSettings.WellnessReminderMinutes));
        _lastReminderTime = DateTime.UtcNow;
    }

    public void ToggleEnabled()
    {
        bool next = !IsEnabled;
        _settings.Update(s => s.EnableVirtualPet = next, nameof(AppSettings.EnableVirtualPet));
    }

    public void UpdateActivity(bool isMediaPlaying, float audioEnergy, bool isUserNear)
    {
        var now = DateTime.UtcNow;
        if (now - _lastEatingTime < TimeSpan.FromSeconds(3.5))
        {
            CurrentMood = PetMood.Eating;
            return;
        }

        if (now - _lastHappyTime < TimeSpan.FromSeconds(3.5))
        {
            CurrentMood = PetMood.Happy;
            return;
        }

        if (isMediaPlaying && audioEnergy > 0.05f)
        {
            CurrentMood = PetMood.Dancing;
        }
        else if (isUserNear)
        {
            CurrentMood = PetMood.Awake;
        }
        else
        {
            CurrentMood = PetMood.Sleeping;
        }
    }

    private void SetHappyMood()
    {
        _lastHappyTime = DateTime.UtcNow;
        CurrentMood = PetMood.Happy;
        _happyResetTimer?.Stop();
        _happyResetTimer?.Start();
    }

    public void Dispose()
    {
        _wellnessTimer?.Stop();
        _happyResetTimer?.Stop();
    }
}
