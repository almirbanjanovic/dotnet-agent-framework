using MudBlazor;

namespace Contoso.BlazorUi.Shared;

/// <summary>
/// Brand theme for Contoso Outdoors — deep forest-green primary, warm amber
/// accent, modern Inter typography, soft layered shadows. Applied via
/// &lt;MudThemeProvider Theme="@AppTheme.Theme" /&gt; in App.razor.
/// </summary>
public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary       = "#1F5F3F",      // forest green
            PrimaryDarken = "#143F2A",
            Secondary     = "#D97706",      // warm amber
            Tertiary      = "#0F766E",      // alpine teal
            Info          = "#2563EB",
            Success       = "#16A34A",
            Warning       = "#D97706",
            Error         = "#DC2626",
            Background    = "#F8FAF7",      // soft off-white with green tint
            Surface       = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText       = "#0F1F17",
            DrawerBackground = "#FFFFFF",
            DrawerText       = "#1F2937",
            DrawerIcon       = "#4B5563",
            TextPrimary      = "#0F1F17",
            TextSecondary    = "#475569",
            TextDisabled     = "#94A3B8",
            ActionDefault    = "#475569",
            Divider          = "#E5E7EB",
            DividerLight     = "#F1F5F4",
            LinesDefault     = "#E5E7EB",
            TableLines       = "#E5E7EB"
        },

        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "Segoe UI", "Helvetica Neue", "Arial", "sans-serif"],
                FontSize   = "0.95rem",
                LineHeight = "1.55",
                LetterSpacing = "normal"
            },
            H1 = new H1Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "700", FontSize = "2.25rem", LineHeight = "1.2",  LetterSpacing = "-0.02em" },
            H2 = new H2Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "700", FontSize = "1.875rem", LineHeight = "1.25", LetterSpacing = "-0.02em" },
            H3 = new H3Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "1.5rem",  LineHeight = "1.3",  LetterSpacing = "-0.01em" },
            H4 = new H4Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "1.25rem", LineHeight = "1.35" },
            H5 = new H5Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "1.125rem", LineHeight = "1.4" },
            H6 = new H6Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "1rem",     LineHeight = "1.4" },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "500", FontSize = "0.95rem" },
            Subtitle2 = new Subtitle2Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "500", FontSize = "0.85rem" },
            Body1 = new Body1Typography { FontFamily = ["Inter", "sans-serif"], FontSize = "0.95rem" },
            Body2 = new Body2Typography { FontFamily = ["Inter", "sans-serif"], FontSize = "0.85rem" },
            Button = new ButtonTypography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "0.875rem", LetterSpacing = "0.01em", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = ["Inter", "sans-serif"], FontSize = "0.75rem" }
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft     = "260px",
            AppbarHeight        = "64px"
        },

        Shadows = new Shadow()
    };
}
