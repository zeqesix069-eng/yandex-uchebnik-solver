using System;
using System.Windows;
using System.Windows.Media;

namespace YandexSolver
{
    public static class ThemeManager
    {
        public static readonly string[] ThemeNames =
        {
            "Dark (Slate)",
            "Light",
            "Midnight",
            "OLED Black",
            "Nord"
        };

        public static readonly string[] AccentNames =
        {
            "Синий",
            "Изумрудный",
            "Янтарный",
            "Красный",
            "Серый"
        };

        public static readonly string[] CornerNames =
        {
            "Острые",
            "Стандарт",
            "Мягкие"
        };

        public static void Apply(Window window, string themeName, string accentName, string cornerStyle, double opacity)
        {
            ResourceDictionary res = Application.Current.Resources;

            switch (themeName)
            {
                case "Light":
                    SetBrush(res, "BgRootBrush", "#F4F4F5");
                    SetBrush(res, "BgChromeBrush", "#EAEAEF");
                    SetBrush(res, "BgSurfaceBrush", "#FFFFFF");
                    SetBrush(res, "BgInputBrush", "#F8FAFC");
                    SetBrush(res, "BgLogBrush", "#FAFAFA");
                    SetBrush(res, "BgHoverBrush", "#E4E4E7");
                    SetBrush(res, "BorderBrush", "#D4D4D8");
                    SetBrush(res, "BorderHoverBrush", "#A1A1AA");
                    SetBrush(res, "TextPrimaryBrush", "#18181B");
                    SetBrush(res, "TextMutedBrush", "#52525B");
                    break;

                case "Midnight":
                    SetBrush(res, "BgRootBrush", "#0F172A");
                    SetBrush(res, "BgChromeBrush", "#0B1120");
                    SetBrush(res, "BgSurfaceBrush", "#1E293B");
                    SetBrush(res, "BgInputBrush", "#263349");
                    SetBrush(res, "BgLogBrush", "#0B1221");
                    SetBrush(res, "BgHoverBrush", "#334155");
                    SetBrush(res, "BorderBrush", "#334155");
                    SetBrush(res, "BorderHoverBrush", "#475569");
                    SetBrush(res, "TextPrimaryBrush", "#F1F5F9");
                    SetBrush(res, "TextMutedBrush", "#94A3B8");
                    break;

                case "OLED Black":
                    SetBrush(res, "BgRootBrush", "#000000");
                    SetBrush(res, "BgChromeBrush", "#070709");
                    SetBrush(res, "BgSurfaceBrush", "#0D0D10");
                    SetBrush(res, "BgInputBrush", "#16161A");
                    SetBrush(res, "BgLogBrush", "#050507");
                    SetBrush(res, "BgHoverBrush", "#222228");
                    SetBrush(res, "BorderBrush", "#24242B");
                    SetBrush(res, "BorderHoverBrush", "#3B3B45");
                    SetBrush(res, "TextPrimaryBrush", "#F4F4F5");
                    SetBrush(res, "TextMutedBrush", "#8E8E98");
                    break;

                case "Nord":
                    SetBrush(res, "BgRootBrush", "#2E3440");
                    SetBrush(res, "BgChromeBrush", "#272C36");
                    SetBrush(res, "BgSurfaceBrush", "#3B4252");
                    SetBrush(res, "BgInputBrush", "#434C5E");
                    SetBrush(res, "BgLogBrush", "#242933");
                    SetBrush(res, "BgHoverBrush", "#4C566A");
                    SetBrush(res, "BorderBrush", "#4C566A");
                    SetBrush(res, "BorderHoverBrush", "#616E88");
                    SetBrush(res, "TextPrimaryBrush", "#ECEFF4");
                    SetBrush(res, "TextMutedBrush", "#D8DEE9");
                    break;

                default:
                    SetBrush(res, "BgRootBrush", "#18181B");
                    SetBrush(res, "BgChromeBrush", "#141417");
                    SetBrush(res, "BgSurfaceBrush", "#202024");
                    SetBrush(res, "BgInputBrush", "#27272C");
                    SetBrush(res, "BgLogBrush", "#111114");
                    SetBrush(res, "BgHoverBrush", "#32323A");
                    SetBrush(res, "BorderBrush", "#34343B");
                    SetBrush(res, "BorderHoverBrush", "#4B4B55");
                    SetBrush(res, "TextPrimaryBrush", "#F4F4F5");
                    SetBrush(res, "TextMutedBrush", "#A1A1AA");
                    break;
            }

            switch (accentName)
            {
                case "Изумрудный":
                    SetBrush(res, "AccentBrush", "#059669");
                    SetBrush(res, "AccentHoverBrush", "#047857");
                    SetBrush(res, "AccentPressedBrush", "#065F46");
                    SetBrush(res, "BorderFocusBrush", "#10B981");
                    break;
                case "Янтарный":
                    SetBrush(res, "AccentBrush", "#D97706");
                    SetBrush(res, "AccentHoverBrush", "#B45309");
                    SetBrush(res, "AccentPressedBrush", "#92400E");
                    SetBrush(res, "BorderFocusBrush", "#F59E0B");
                    break;
                case "Красный":
                    SetBrush(res, "AccentBrush", "#DC2626");
                    SetBrush(res, "AccentHoverBrush", "#B91C1C");
                    SetBrush(res, "AccentPressedBrush", "#991B1B");
                    SetBrush(res, "BorderFocusBrush", "#EF4444");
                    break;
                case "Серый":
                    SetBrush(res, "AccentBrush", "#52525B");
                    SetBrush(res, "AccentHoverBrush", "#3F3F46");
                    SetBrush(res, "AccentPressedBrush", "#27272A");
                    SetBrush(res, "BorderFocusBrush", "#71717A");
                    break;
                default:
                    SetBrush(res, "AccentBrush", "#2563EB");
                    SetBrush(res, "AccentHoverBrush", "#1D4ED8");
                    SetBrush(res, "AccentPressedBrush", "#1E40AF");
                    SetBrush(res, "BorderFocusBrush", "#3B82F6");
                    break;
            }

            switch (cornerStyle)
            {
                case "Острые":
                    res["WindowRadius"] = new CornerRadius(3);
                    res["HeaderRadius"] = new CornerRadius(3, 3, 0, 0);
                    res["FooterRadius"] = new CornerRadius(0, 0, 3, 3);
                    res["CardRadius"] = new CornerRadius(3);
                    res["ControlRadius"] = new CornerRadius(2);
                    break;
                case "Мягкие":
                    res["WindowRadius"] = new CornerRadius(16);
                    res["HeaderRadius"] = new CornerRadius(16, 16, 0, 0);
                    res["FooterRadius"] = new CornerRadius(0, 0, 16, 16);
                    res["CardRadius"] = new CornerRadius(12);
                    res["ControlRadius"] = new CornerRadius(9);
                    break;
                default:
                    res["WindowRadius"] = new CornerRadius(10);
                    res["HeaderRadius"] = new CornerRadius(10, 10, 0, 0);
                    res["FooterRadius"] = new CornerRadius(0, 0, 10, 10);
                    res["CardRadius"] = new CornerRadius(8);
                    res["ControlRadius"] = new CornerRadius(6);
                    break;
            }

            if (window != null)
            {
                window.Opacity = Math.Max(0.75, Math.Min(1.0, opacity));
            }
        }

        private static void SetBrush(ResourceDictionary res, string key, string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush;
        }
    }
}
