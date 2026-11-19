using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public partial class PetSanctuaryView : UserControl
{
    private IslandWindow? _window;
    private VirtualPetService? _petService;

    public PetSanctuaryView()
    {
        InitializeComponent();
        Loaded += PetSanctuaryView_Loaded;
        Unloaded += PetSanctuaryView_Unloaded;
    }

    private void PetSanctuaryView_Loaded(object sender, RoutedEventArgs e)
    {
        _window = IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;
        _petService = _window?.PetService;

        if (_petService != null)
        {
            _petService.AffectionChanged += OnAffectionChanged;
            _petService.MoodChanged += OnMoodChanged;
            _petService.SpecialActionTriggered += OnSpecialActionTriggered;
        }

        RefreshUI();
    }

    private void PetSanctuaryView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_petService != null)
        {
            _petService.AffectionChanged -= OnAffectionChanged;
            _petService.MoodChanged -= OnMoodChanged;
            _petService.SpecialActionTriggered -= OnSpecialActionTriggered;
        }
    }

    private void OnAffectionChanged(int affection) => Dispatcher.InvokeAsync(RefreshUI);

    private void OnMoodChanged(PetMood mood) => Dispatcher.InvokeAsync(() =>
    {
        StagePet.Mood = mood;
        UpdateMoodText(mood);
    });

    private void OnSpecialActionTriggered(PetSpecialActionKind kind) => Dispatcher.InvokeAsync(() =>
    {
        StagePet.TriggerSpecialAction(kind);
        RefreshUI();
    });

    public void RefreshUI()
    {
        var service = _petService ?? _window?.PetService;
        if (service == null) return;

        string species = service.PetSpecies;
        string accessory = service.PetAccessory;
        string name = service.PetName;
        int affection = service.AffectionScore;
        var mood = service.CurrentMood;

        // Stage Pet synchronization
        StagePet.Species = species;
        StagePet.Accessory = accessory;
        StagePet.Mood = mood;
        StagePet.PetName = name;
        StagePet.Affection = affection;

        // Header and names
        StagePetName.Text = name;
        PetSpeciesBadgeText.Text = FormatSpeciesBadge(species);
        StageAccessoryTag.Text = FormatAccessoryGlyph(accessory);
        UpdateMoodText(mood);

        // Rank and progress
        string rankTitle;
        int prevThresh, nextThresh;
        if (affection < 50)
        {
            rankTitle = "Level 1 · Curious Stray";
            prevThresh = 0;
            nextThresh = 50;
        }
        else if (affection < 150)
        {
            rankTitle = "Level 2 · Playful Friend";
            prevThresh = 50;
            nextThresh = 150;
        }
        else if (affection < 300)
        {
            rankTitle = "Level 3 · Devoted Bestie";
            prevThresh = 150;
            nextThresh = 300;
        }
        else if (affection < 500)
        {
            rankTitle = "Level 4 · Soulmate Companion";
            prevThresh = 300;
            nextThresh = 500;
        }
        else if (affection < 1000)
        {
            rankTitle = "Level 5 · Island Legend";
            prevThresh = 500;
            nextThresh = 1000;
        }
        else
        {
            rankTitle = "Level 6 · Mythic Guardian";
            prevThresh = 1000;
            nextThresh = 2000;
        }

        StageRankTitle.Text = rankTitle;
        StageScoreText.Text = $"{affection} / {nextThresh} ❤️";
        double frac = Math.Clamp((double)(affection - prevThresh) / Math.Max(1, nextThresh - prevThresh), 0.05, 1.0);
        StageProgressBar.Width = frac * 120.0;

        // Species unique perk & action labels
        StagePerkText.Text = service.GetSpeciesPerkDescription();
        SpecialActionTitle.Text = service.GetSpecialActionName();

        // Treats & Wellness
        TreatIconGlyph.Text = GetSpeciesTreatIcon(species);
        TreatTitleText.Text = GetSpeciesTreatName(species);
        TreatCountText.Text = $"+8 ❤️ Total: {service.TreatCount}";

        WaterBtnText.Text = $"💧 Water: {service.WaterCount}";
        StretchBtnText.Text = $"🧘 Stretch: {service.StretchCount}";
        SpeciesBtnText.Text = $"🐾 {species}";
        GearBtnText.Text = FormatAccessoryBtnLabel(accessory);
    }

    private void UpdateMoodText(PetMood mood)
    {
        StagePetMoodText.Text = mood switch
        {
            PetMood.Sleeping => "💤 Snoozing cozy (Loaf)",
            PetMood.Awake => "👀 Alert & Curious",
            PetMood.Dancing => "🎵 Grooving to beats!",
            PetMood.Happy => "✨ Overjoyed & Purring",
            PetMood.Thirsty => "💧 Thirsty for water",
            PetMood.Stretch => "🧘 Big healthy stretch",
            PetMood.Eating => "🍣 Munching treat!",
            PetMood.Playing => "🎾 Having a blast playing!",
            _ => "🐾 Happy Companion"
        };
    }

    private static string FormatSpeciesBadge(string species) => species switch
    {
        "Dog" => "🐶 Shiba Inu Companion",
        "Bunny" => "🐰 Marshmallow Lop Bunny",
        "Fox" => "🦊 Kitsune Spirit Fox",
        "Panda" => "🐼 Giant Bamboo Panda",
        "Dragon" => "🐲 Celestial Sky Dragon",
        _ => "🐱 Ginger Tabby Cat"
    };

    private static string FormatAccessoryGlyph(string acc) => acc switch
    {
        "Crown" => " 👑",
        "WizardHat" => " 🧙",
        "Headphones" => " 🎧",
        "Sunglasses" => " 😎",
        "Halo" => " 😇",
        "Sprout" => " 🌱",
        _ => ""
    };

    private static string FormatAccessoryBtnLabel(string acc) => acc switch
    {
        "Crown" => "👑 Crown",
        "WizardHat" => "🧙 Wizard",
        "Headphones" => "🎧 Beats",
        "Sunglasses" => "😎 Shades",
        "Halo" => "😇 Halo",
        "Sprout" => "🌱 Sprout",
        _ => "👑 Gear"
    };

    private static string GetSpeciesTreatIcon(string species) => species switch
    {
        "Dog" => "🦴",
        "Bunny" => "🥕",
        "Fox" => "🫐",
        "Panda" => "🎋",
        "Dragon" => "⭐",
        _ => "🍣"
    };

    private static string GetSpeciesTreatName(string species) => species switch
    {
        "Dog" => "Golden Bone",
        "Bunny" => "Sweet Carrot",
        "Fox" => "Moonberries",
        "Panda" => "Bamboo Shoot",
        "Dragon" => "Starfruit Gem",
        _ => "Salmon Sushi"
    };

    private void StagePet_Click(object sender, MouseButtonEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.Pet();
        _window?.TriggerLiquidImpulse(180, 80, 12);
        StagePet.PetClickImpulse();
        RefreshUI();
        _window?.PushToast(new ToastContent(
            "\uE76E",
            _petService.PetName,
            msg,
            ThemeBrushes.PinkColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback));
        e.Handled = true;
    }

    private void StagePet_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_petService == null) return;
        e.Handled = true;
        string msg = _petService.PerformTrick();
        _window?.TriggerLiquidImpulse(220, -90, 16);
        StagePet.TriggerTrick();
        RefreshUI();
        _window?.PushToast(new ToastContent(
            "\uE734",
            $"{_petService.PetName} Flip!",
            msg,
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    private void PetCuddle_Click(object sender, MouseButtonEventArgs e)
    {
        StagePet_Click(sender, e);
    }

    private void SpecialAction_Click(object sender, MouseButtonEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.PlaySpecialAction();
        _window?.TriggerLiquidImpulse(240, 60, 15);
        StagePet.TriggerSpecialAction();
        RefreshUI();
        _window?.PushToast(new ToastContent(
            "\uE734",
            $"{_petService.PetName} Playtime!",
            msg,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(3.0),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
        e.Handled = true;
    }

    private void FeedTreat_Click(object sender, MouseButtonEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.FeedTreat();
        _window?.TriggerLiquidImpulse(160, 40, 10);
        StagePet.PetClickImpulse();
        RefreshUI();
        _window?.PushToast(new ToastContent(
            "\uE76E",
            "Yummy Gourmet Snack!",
            msg,
            ThemeBrushes.AmberColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback,
            Category: "pet"));
        e.Handled = true;
    }

    private void PerformTrick_Click(object sender, MouseButtonEventArgs e)
    {
        StagePet_RightClick(sender, e);
    }

    private void LogWaterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.LogWater();
        RefreshUI();
        _window?.PushToast(new ToastContent(
            "\uE958",
            "Hydration Logged!",
            msg,
            ThemeBrushes.BlueColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback));
    }

    private void LogStretchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        string msg = _petService.LogStretch();
        RefreshUI();
        _window?.PushToast(new ToastContent(
            "\uE7C9",
            "Stretch Logged!",
            msg,
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.5),
            TargetView: null,
            Priority: ToastPriority.Feedback));
    }

    private void CycleSpeciesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        _petService.CycleSpecies();
        StagePet.PetClickImpulse();
        RefreshUI();
        _window?.PushToast(new ToastContent(
            "\uE708",
            "Companion Species",
            $"Switched to {FormatSpeciesBadge(_petService.PetSpecies)}",
            ThemeBrushes.PinkColor,
            TimeSpan.FromSeconds(2.0),
            TargetView: "pet",
            Priority: ToastPriority.Feedback,
            Category: "pet"));
    }

    private void CycleAccessoryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_petService == null) return;
        _petService.CycleAccessory();
        StagePet.PetClickImpulse();
        RefreshUI();
    }

    private void BackToWidgets_Click(object sender, RoutedEventArgs e) =>
        _window?.NavigateToView(ExpandedView.WidgetsGallery);

    private void ClosePlayground_Click(object sender, RoutedEventArgs e) =>
        _window?.CollapseNow();

    private void RoamDesktopBtn_Click(object sender, RoutedEventArgs e)
    {
        _window?.TogglePetFloatWindow();
    }
}
