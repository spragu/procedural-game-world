using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using procedural_game_world;

namespace WorldGenerator.App;

public partial class MainWindow : Window
{
    private ProceduralGameWorld? _generatedWorld;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSliderValues();
        await GenerateWorldAsync();
    }

    private void SettingsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            UpdateSliderValues();
        }
    }

    private void RandomizeSeed_Click(object sender, RoutedEventArgs e)
    {
        SeedTextBox.Text = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);
    }

    private async void GenerateWorld_Click(object sender, RoutedEventArgs e)
    {
        await GenerateWorldAsync();
    }

    private async Task GenerateWorldAsync()
    {
        var settings = CreateSettings(out var validationError);

        if (settings is null)
        {
            ShowError(validationError);
            return;
        }

        GenerateWorldButton.IsEnabled = false;
        StatusText.Text = "GENERATING";

        try
        {
            var world = await Task.Run(() => ProceduralWorldBuilder.BuildWorld(settings));

            _generatedWorld = world;
            WorldMapImage.Source = WorldMapRenderer.Render(world);
            MapSizeText.Text = $"{world.WorldWidth} x {world.WorldHeight} TILES";
            var smoothingPassLabel = settings.SmoothingPasses == 1
                ? "1 smoothing pass"
                : $"{settings.SmoothingPasses} smoothing passes";
            GenerationSummary.Text = settings.Seed is int seed
                ? $"Seed {seed:N0} | {world.WorldWidth * world.WorldHeight:N0} tiles | {world.GeneratedBiomeCount} biomes | {settings.BiomeVariationChance:P0} boundary variation | {smoothingPassLabel}"
                : $"Random seed | {world.WorldWidth * world.WorldHeight:N0} tiles | {world.GeneratedBiomeCount} biomes | {settings.BiomeVariationChance:P0} boundary variation | {smoothingPassLabel}";
            ErrorText.Visibility = Visibility.Collapsed;
            StatusText.Text = "WORLD GENERATED";
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ShowError(exception.Message);
        }
        catch (OutOfMemoryException)
        {
            ShowError("The map is too large for the available memory. Reduce its width or height and try again.");
        }
        finally
        {
            GenerateWorldButton.IsEnabled = true;
        }
    }

    private WorldGenerationSettings? CreateSettings(out string validationError)
    {
        validationError = string.Empty;
        int? seed = null;
        var seedText = SeedTextBox.Text.Trim();

        if (seedText.Length > 0)
        {
            if (!int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeed))
            {
                validationError = "Seed must be a whole number.";
                return null;
            }

            seed = parsedSeed;
        }

        var minBiomeCount = (int)MinSeedsSlider.Value;
        var maxBiomeCount = (int)MaxSeedsSlider.Value;

        if (minBiomeCount > maxBiomeCount)
        {
            validationError = "Minimum biomes cannot exceed maximum biomes.";
            return null;
        }

        return new WorldGenerationSettings
        {
            WorldWidth = (int)WidthSlider.Value,
            WorldHeight = (int)HeightSlider.Value,
            MinBiomeCount = minBiomeCount,
            MaxBiomeCount = maxBiomeCount,
            SmoothingPasses = (int)SmoothingPassesSlider.Value,
            BiomeVariationChance = (float)BiomeVariationSlider.Value,
            Seed = seed
        };
    }

    private void UpdateSliderValues()
    {
        WidthValue.Text = ((int)WidthSlider.Value).ToString(CultureInfo.InvariantCulture);
        HeightValue.Text = ((int)HeightSlider.Value).ToString(CultureInfo.InvariantCulture);
        MinSeedsValue.Text = ((int)MinSeedsSlider.Value).ToString(CultureInfo.InvariantCulture);
        MaxSeedsValue.Text = ((int)MaxSeedsSlider.Value).ToString(CultureInfo.InvariantCulture);
        SmoothingPassesValue.Text = ((int)SmoothingPassesSlider.Value).ToString(CultureInfo.InvariantCulture);
        BiomeVariationValue.Text = BiomeVariationSlider.Value.ToString("P0", CultureInfo.InvariantCulture);
    }

    private void WorldMapImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_generatedWorld is null || !TryGetHoveredTile(e.GetPosition(WorldMapImage), out var tileX, out var tileY))
        {
            HoveredBiomeText.Text = "BIOME: -";
            return;
        }

        HoveredBiomeText.Text = $"BIOME: {FormatBiomeName(_generatedWorld.Tiles[tileX, tileY].Biome)}";
    }

    private void WorldMapImage_MouseLeave(object sender, MouseEventArgs e)
    {
        HoveredBiomeText.Text = "BIOME: -";
    }

    private bool TryGetHoveredTile(Point pointerPosition, out int tileX, out int tileY)
    {
        tileX = 0;
        tileY = 0;

        if (_generatedWorld is null || WorldMapImage.ActualWidth <= 0 || WorldMapImage.ActualHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(
            WorldMapImage.ActualWidth / _generatedWorld.WorldWidth,
            WorldMapImage.ActualHeight / _generatedWorld.WorldHeight);

        if (scale <= 0)
        {
            return false;
        }

        var renderedWidth = _generatedWorld.WorldWidth * scale;
        var renderedHeight = _generatedWorld.WorldHeight * scale;
        var mapX = pointerPosition.X - ((WorldMapImage.ActualWidth - renderedWidth) / 2);
        var mapY = pointerPosition.Y - ((WorldMapImage.ActualHeight - renderedHeight) / 2);

        if (mapX < 0 || mapY < 0 || mapX >= renderedWidth || mapY >= renderedHeight)
        {
            return false;
        }

        tileX = Math.Min((int)(mapX / scale), _generatedWorld.WorldWidth - 1);
        tileY = Math.Min((int)(mapY / scale), _generatedWorld.WorldHeight - 1);
        return true;
    }

    private static string FormatBiomeName(Biome biome)
    {
        var biomeName = biome.ToString();
        var displayName = new StringBuilder(biomeName.Length + 4);

        for (var index = 0; index < biomeName.Length; index++)
        {
            var character = biomeName[index];

            if (index > 0 && char.IsUpper(character))
            {
                displayName.Append(' ');
            }

            displayName.Append(character);
        }

        return displayName.ToString();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        StatusText.Text = "CHECK SETTINGS";
    }
}
