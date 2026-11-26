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
    Eating,
    Playing
}

public enum PetSpecialActionKind
{
    None,
    LaserHunt,      // Cat: Glowing red laser pointer hunt & swat
    BallFetch,      // Dog: Bouncing tennis ball leap & catch
    BinkyFlip,      // Bunny: 180° mid-air twist binky jump
    FoxfireDance,   // Fox: 3 orbiting cyan spirit flame orbs
    KungFuRoll,     // Panda: Backward acrobatic tumble roll
    DragonBreath    // Dragon: Deep breath & stardust flame puff
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
    private bool _disposed;

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
    public bool AutoRoamEnabled => _settings.Current.EnablePetAutoRoam;
    public bool ContextReactionsEnabled => _settings.Current.EnablePetContextReactions;
    public int CareStreakDays => _settings.Current.PetCareStreakDays;
    public string CurrentContextCaption { get; private set; } = "Cozy & Content";

    public event Action<PetMood>? MoodChanged;
    public event Action<WellnessAlert>? WellnessAlertTriggered;
    public event Action<int>? AffectionChanged;
    public event Action<PetSpecialActionKind>? SpecialActionTriggered;

    public VirtualPetService(Dispatcher dispatcher, SettingsService settings)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _wellnessTimer = new DispatcherTimer(TimeSpan.FromMinutes(30), DispatcherPriority.Background, OnWellnessTimerTick, _dispatcher);
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        UpdateWellnessTimerState();

        _happyResetTimer = new DispatcherTimer(TimeSpan.FromSeconds(3.5), DispatcherPriority.Background, (_, _) =>
        {
            _happyResetTimer?.Stop();
            if (CurrentMood is PetMood.Happy or PetMood.Eating or PetMood.Playing)
            {
                CurrentMood = PetMood.Awake;
            }
        }, _dispatcher);
    }

    private void OnSettingsPropertyChanged(string? propName)
    {
        if (_disposed) return;
        if (propName is nameof(AppSettings.EnableVirtualPet) or nameof(AppSettings.WellnessReminderMinutes))
        {
            _dispatcher.InvokeAsync(UpdateWellnessTimerState, DispatcherPriority.Background);
        }
    }

    private void UpdateWellnessTimerState()
    {
        if (_disposed || _wellnessTimer == null) return;
        int mins = ReminderIntervalMinutes;
        if (!IsEnabled || mins <= 0)
        {
            _wellnessTimer.Stop();
            return;
        }

        var remaining = TimeSpan.FromMinutes(mins) - (DateTime.UtcNow - _lastReminderTime);
        var nextInterval = remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
        _wellnessTimer.Stop();
        _wellnessTimer.Interval = nextInterval;
        _wellnessTimer.Start();
    }

    private void OnWellnessTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || !IsEnabled || ReminderIntervalMinutes <= 0)
        {
            _wellnessTimer?.Stop();
            return;
        }

        var elapsed = DateTime.UtcNow - _lastReminderTime;
        if (elapsed >= TimeSpan.FromMinutes(ReminderIntervalMinutes))
        {
            _lastReminderTime = DateTime.UtcNow;
            TriggerNextWellnessAlert();
        }
        UpdateWellnessTimerState();
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
        RecordDailyCare();
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
        RecordDailyCare();
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
        RecordDailyCare();
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

    public PetSpecialActionKind GetSpeciesSpecialActionKind() => PetSpecies switch
    {
        "Dog" => PetSpecialActionKind.BallFetch,
        "Bunny" => PetSpecialActionKind.BinkyFlip,
        "Fox" => PetSpecialActionKind.FoxfireDance,
        "Panda" => PetSpecialActionKind.KungFuRoll,
        "Dragon" => PetSpecialActionKind.DragonBreath,
        _ => PetSpecialActionKind.LaserHunt
    };

    public string GetSpecialActionName() => PetSpecies switch
    {
        "Dog" => "Fetch Ball 🎾",
        "Bunny" => "Binky Hop 🌸",
        "Fox" => "Foxfire 🔥",
        "Panda" => "Kung-Fu Roll 🎋",
        "Dragon" => "Star Breath ✨",
        _ => "Laser Hunt 🔴"
    };

    public string GetSpeciesPerkDescription() => PetSpecies switch
    {
        "Dog" => "Brisk tail wag syncs to music beat · Joyful barking acrobats",
        "Bunny" => "High-velocity binky flips · Ultra-responsive squash physics",
        "Fox" => "Kitsune spirit fire orbs · Evening active night-owl mode",
        "Panda" => "Zen tumbler · Extra calm metabolism & water celebration (+15 ❤️)",
        "Dragon" => "Continuous celestial flight hover · Stardust flame puffs",
        _ => "Agile laser hunter · Cozy sleeping loaf & purr resonance"
    };

    public string PlaySpecialAction()
    {
        RecordDailyCare();
        var kind = GetSpeciesSpecialActionKind();
        int newScore = AffectionScore + 6;
        _settings.Update(s => s.PetAffectionScore = newScore, nameof(AppSettings.PetAffectionScore));
        _lastHappyTime = DateTime.UtcNow;
        CurrentMood = PetMood.Playing;
        _happyResetTimer?.Stop();
        _happyResetTimer?.Start();
        AffectionChanged?.Invoke(newScore);
        SpecialActionTriggered?.Invoke(kind);

        return PetSpecies switch
        {
            "Dog" => $"🎾 Threw the tennis ball! {PetName} vaulted mid-air, caught it, and wagged proudly! (+6 ❤️)",
            "Bunny" => $"🌸 {PetName} performed a spectacular high-flying binky flip with joyful ear twitches! (+6 ❤️)",
            "Fox" => $"🔥 {PetName} summoned 3 ethereal Kitsune foxfire orbs and danced gracefully through spirit flames! (+6 ❤️)",
            "Panda" => $"🎋 {PetName} executed an adorable clumsy kung-fu somersault roll and beamed! (+6 ❤️)",
            "Dragon" => $"✨ {PetName} fluttered cosmic wings and breathed a sparkling stardust heart flame! (+6 ❤️)",
            _ => $"🔴 {PetName} locked onto the red laser dot, pounced, and swatted with lightning speed! (+6 ❤️)"
        };
    }

    public string LogWater()
    {
        RecordDailyCare();
        int count = WaterCount + 1;
        int newScore = AffectionScore + 10;
        _settings.Update(s =>
        {
            s.PetWaterCount = count;
            s.PetAffectionScore = newScore;
        }, nameof(AppSettings.PetWaterCount));

        _lastReminderTime = DateTime.UtcNow;
        UpdateWellnessTimerState();
        SetHappyMood();
        AffectionChanged?.Invoke(newScore);

        return $"💧 Glass #{count} logged! {PetName} celebrates your hydration! (+10 ❤️)";
    }

    public string LogStretch()
    {
        RecordDailyCare();
        int count = StretchCount + 1;
        int newScore = AffectionScore + 10;
        _settings.Update(s =>
        {
            s.PetStretchCount = count;
            s.PetAffectionScore = newScore;
        }, nameof(AppSettings.PetStretchCount));

        _lastReminderTime = DateTime.UtcNow;
        UpdateWellnessTimerState();
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

    public string SetPetName(string? name)
    {
        string clean = SettingsService.SanitizePetName(name);
        _settings.Update(s => s.PetName = clean, nameof(AppSettings.PetName));
        SetHappyMood();
        return clean;
    }

    public void ToggleAutoRoam()
    {
        bool next = !AutoRoamEnabled;
        _settings.Update(s => s.EnablePetAutoRoam = next, nameof(AppSettings.EnablePetAutoRoam));
    }

    public void ToggleContextReactions()
    {
        bool next = !ContextReactionsEnabled;
        _settings.Update(s => s.EnablePetContextReactions = next, nameof(AppSettings.EnablePetContextReactions));
    }

    public (double? X, double? Y) GetFloatPosition() =>
        (_settings.Current.PetFloatX, _settings.Current.PetFloatY);

    public void SaveFloatPosition(double x, double y)
    {
        _settings.Update(s =>
        {
            s.PetFloatX = x;
            s.PetFloatY = y;
        }, nameof(AppSettings.PetFloatX));
    }

    /// <summary>
    /// Cozy, non-punishing daily care streak tracker (UTC calendar days).
    /// </summary>
    public int RecordDailyCare(DateTime? nowUtc = null)
    {
        var today = (nowUtc ?? DateTime.UtcNow).Date;
        string todayStr = today.ToString("yyyy-MM-dd");
        string? lastStr = _settings.Current.PetLastCareDate;
        int streak = _settings.Current.PetCareStreakDays;

        if (string.Equals(lastStr, todayStr, StringComparison.Ordinal))
        {
            if (streak < 1)
            {
                streak = 1;
                _settings.Update(s => s.PetCareStreakDays = 1, nameof(AppSettings.PetCareStreakDays));
            }
            return streak;
        }

        if (DateTime.TryParseExact(lastStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var lastDate) &&
            (today - lastDate.Date).TotalDays == 1)
        {
            streak = Math.Max(1, streak) + 1;
        }
        else
        {
            streak = 1;
        }

        _settings.Update(s =>
        {
            s.PetCareStreakDays = streak;
            s.PetLastCareDate = todayStr;
        }, nameof(AppSettings.PetCareStreakDays));
        return streak;
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
        UpdateWellnessTimerState();
    }

    public void ToggleEnabled()
    {
        bool next = !IsEnabled;
        _settings.Update(s => s.EnableVirtualPet = next, nameof(AppSettings.EnableVirtualPet));
    }

    public void UpdateActivity(bool isMediaPlaying, float audioEnergy, bool isUserNear) =>
        UpdateActivity(isMediaPlaying, audioEnergy, isUserNear, isFocusTimerActive: false, isLowBattery: false, isHighLoad: false, localHour: -1);

    public void UpdateActivity(
        bool isMediaPlaying,
        float audioEnergy,
        bool isUserNear,
        bool isFocusTimerActive,
        bool isLowBattery,
        bool isHighLoad,
        int localHour)
    {
        var now = DateTime.UtcNow;
        if (now - _lastEatingTime < TimeSpan.FromSeconds(3.5))
        {
            CurrentMood = PetMood.Eating;
            CurrentContextCaption = "🍣 Delicious treat!";
            return;
        }

        if (now - _lastHappyTime < TimeSpan.FromSeconds(3.5))
        {
            if (CurrentMood != PetMood.Playing)
            {
                CurrentMood = PetMood.Happy;
                CurrentContextCaption = "✨ Overjoyed & purring";
            }
            else
            {
                CurrentContextCaption = "🎾 Playtime!";
            }
            return;
        }

        if (isMediaPlaying && audioEnergy > 0.05f)
        {
            CurrentMood = PetMood.Dancing;
            CurrentContextCaption = "🎵 Vibing to beats";
            return;
        }

        if (ContextReactionsEnabled)
        {
            if (isFocusTimerActive)
            {
                CurrentMood = PetMood.Awake;
                CurrentContextCaption = "📚 Focus Buddy";
                return;
            }

            if (isLowBattery)
            {
                CurrentMood = PetMood.Sleeping;
                CurrentContextCaption = "🔋 Low battery yawn";
                return;
            }

            if (isHighLoad)
            {
                CurrentMood = PetMood.Awake;
                CurrentContextCaption = "⚡ Working hard!";
                return;
            }

            if (localHour >= 0 && (localHour >= 23 || localHour < 6))
            {
                CurrentMood = isUserNear ? PetMood.Awake : PetMood.Sleeping;
                CurrentContextCaption = isUserNear ? "🌙 Night-owl buddy" : "🌙 Late-night loaf";
                return;
            }
        }

        if (isUserNear)
        {
            CurrentMood = PetMood.Awake;
            CurrentContextCaption = "👀 Ready to play";
        }
        else
        {
            CurrentMood = PetMood.Sleeping;
            CurrentContextCaption = "💤 Cozy snoozing";
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
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _wellnessTimer?.Stop();
        _happyResetTimer?.Stop();
    }
}
