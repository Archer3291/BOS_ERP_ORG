// Models/Usuario.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    // Models/UsuarioTema.cs
    public class UsuarioTema
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string Modo { get; set; } = "light";

        // ── Light mode ─────────────────────────────────────────
        public string PrimaryBlue { get; set; }  // --primary-blue
        public string PrimaryBlueHover { get; set; }  // --primary-blue-dark (hover)
        public string SecondaryBlue { get; set; }  // --secondary-blue
        public string LightBlue { get; set; }  // --light-blue
        public string VeryLightBlue { get; set; }  // --very-light-blue
        public string DarkBlueLight { get; set; }  // --dark-blue (light mode)
        public string AccentBlue { get; set; }  // --accent-blue
        public string BgLight { get; set; }  // --bg-light
        public string BgGray { get; set; }  // --bg-gray
        public string TextDark { get; set; }  // --text-dark
        public string TextLight { get; set; }  // --text-light
        public string BorderLight { get; set; }  // --border-light

        // ── Dark mode ───────────────────────────────────────────
        public string DarkPrimaryBlue { get; set; }  // --primary-blue
        public string DarkBlueHover { get; set; }  // --primary-blue-dark (hover)
        public string DarkSecondaryBlue { get; set; }  // --secondary-blue
        public string DarkLightBlue { get; set; }  // --light-blue
        public string DarkVeryLightBlue { get; set; }  // --very-light-blue
        public string DarkDarkBlue { get; set; }  // --dark-blue
        public string DarkAccentBlue { get; set; }  // --accent-blue
        public string DarkBgLight { get; set; }  // --bg-light
        public string DarkBgGray { get; set; }  // --bg-gray
        public string DarkTextDark { get; set; }  // --text-dark
        public string DarkTextLight { get; set; }  // --text-light
        public string DarkBorderLight { get; set; }  // --border-light

        public string PresetNombre { get; set; }
        public bool? ParticulasActivas { get; set; } = true;
        public int? ParticulasCantidad { get; set; } = 200;
        public string ParticulasForma { get; set; } = "circle";
        public string ParticulasPreset { get; set; } = "theme";

        public string SidebarPosicion { get; set; } = "left";   // "left" | "right"
        public string SidebarAncho { get; set; } = "285";    // px sin unidad
        public string SidebarIconStyle { get; set; } = "duotone"; // "duotone"|"solid"|"light"|"regular"
        public string SidebarHoverEfecto { get; set; } = "slide";  // "slide"|"glow"|"fill"|"none"
        public string SidebarBgColor { get; set; }             // null = usa --bg-light del tema
        public string SidebarLinkColor { get; set; }             // null = usa --text-light
        public string SidebarActiveColor { get; set; }             // null = usa --light-blue
        public string SidebarActiveLinkColor { get; set; }            // null = usa --active-link
        public string SidebarHeaderBg { get; set; }             // null = usa gradiente actual
        public string SidebarModo { get; set; } = "normal";

        // Tipo de armazón de navegación:
        // "sidebar" (default) | "topbar" | "compact" | "hybrid"
        public string LayoutTipo { get; set; } = "sidebar";
    }

    // Presets predefinidos (temporadas / eventos)
    public static class TemaPresets
    {
        public static readonly Dictionary<string, UsuarioTema> Presets =
            new Dictionary<string, UsuarioTema>
            {
                ["default"] = new UsuarioTema
                {
                    PrimaryBlue = "#1e3a5f",
                    PrimaryBlueHover = "#0f1729",
                    SecondaryBlue = "#475569",
                    LightBlue = "#e1e7ef",
                    VeryLightBlue = "#f8fafc",
                    DarkBlueLight = "#0f1729",
                    AccentBlue = "#3b82f6",
                    BgLight = "#ffffff",
                    BgGray = "#f8fafc",
                    TextDark = "#0f172a",
                    TextLight = "#64748b",
                    BorderLight = "#e2e8f0",

                    DarkPrimaryBlue = "#475569",
                    DarkBlueHover = "#334155",
                    DarkSecondaryBlue = "#64748b",
                    DarkLightBlue = "#334155",
                    DarkVeryLightBlue = "#8191a7",
                    DarkDarkBlue = "#cbd5e1",
                    DarkAccentBlue = "#60a5fa",
                    DarkBgLight = "#0f172a",
                    DarkBgGray = "#1e293b",
                    DarkTextDark = "#f1f5f9",
                    DarkTextLight = "#cbd5e1",
                    DarkBorderLight = "#475569",
                },
                ["navidad"] = new UsuarioTema
                {
                    PrimaryBlue = "#14532d",
                    PrimaryBlueHover = "#052e16",
                    SecondaryBlue = "#166534",
                    LightBlue = "#bbf7d0",
                    VeryLightBlue = "#f0fdf4",
                    DarkBlueLight = "#052e16",
                    AccentBlue = "#dc2626",
                    BgLight = "#ffffff",
                    BgGray = "#f0fdf4",
                    TextDark = "#14532d",
                    TextLight = "#4b7c5a",
                    BorderLight = "#bbf7d0",

                    DarkPrimaryBlue = "#166534",
                    DarkBlueHover = "#052e16",
                    DarkSecondaryBlue = "#15803d",
                    DarkLightBlue = "#14532d",
                    DarkVeryLightBlue = "#4b7c5a",
                    DarkDarkBlue = "#a7f3d0",
                    DarkAccentBlue = "#ef4444",
                    DarkBgLight = "#052e16",
                    DarkBgGray = "#14532d",
                    DarkTextDark = "#f0fdf4",
                    DarkTextLight = "#bbf7d0",
                    DarkBorderLight = "#166534",

                    ParticulasForma = "star",
                    ParticulasPreset = "snow",
                    ParticulasCantidad = 250,
                },
                ["verano"] = new UsuarioTema
                {
                    PrimaryBlue = "#92400e",
                    PrimaryBlueHover = "#451a03",
                    SecondaryBlue = "#b45309",
                    LightBlue = "#fde68a",
                    VeryLightBlue = "#fef3c7",
                    DarkBlueLight = "#451a03",
                    AccentBlue = "#f59e0b",
                    BgLight = "#fffbeb",
                    BgGray = "#fef3c7",
                    TextDark = "#1c1917",
                    TextLight = "#78716c",
                    BorderLight = "#fde68a",

                    DarkPrimaryBlue = "#b45309",
                    DarkBlueHover = "#78350f",
                    DarkSecondaryBlue = "#d97706",
                    DarkLightBlue = "#92400e",
                    DarkVeryLightBlue = "#b45309",
                    DarkDarkBlue = "#fcd34d",
                    DarkAccentBlue = "#fbbf24",
                    DarkBgLight = "#1c1400",
                    DarkBgGray = "#292200",
                    DarkTextDark = "#fffbeb",
                    DarkTextLight = "#fde68a",
                    DarkBorderLight = "#b45309",

                    ParticulasPreset = "matrix",
                    ParticulasForma = "plus",
                },
                ["aurora"] = new UsuarioTema
                {
                    // Light — sin cambios
                    PrimaryBlue = "#3d52a0",
                    PrimaryBlueHover = "#1e2d6b",
                    SecondaryBlue = "#7091e6",
                    LightBlue = "#d6e4ff",
                    VeryLightBlue = "#f0f4ff",
                    DarkBlueLight = "#1e2d6b",
                    AccentBlue = "#8697c4",
                    BgLight = "#ffffff",
                    BgGray = "#f0f4ff",
                    TextDark = "#0f1340",
                    TextLight = "#6672a8",
                    BorderLight = "#d6e4ff",

                    // Dark — corregido
                    DarkPrimaryBlue = "#7091e6",  // color medio, destaca sobre fondo oscuro
                    DarkBlueHover = "#8ca5f5",  // un tono más claro para hover
                    DarkSecondaryBlue = "#5470cc",
                    DarkLightBlue = "#2a3a7a",  // para fondos de chips/tags
                    DarkVeryLightBlue = "#3d52a0",
                    DarkDarkBlue = "#c8d5f5",  // texto sobre fondos oscuros
                    DarkAccentBlue = "#a8b8f0",
                    DarkBgLight = "#0d1130",  // fondo base — muy oscuro
                    DarkBgGray = "#141d45",  // fondo superficies — visible pero sutil
                    DarkTextDark = "#edf0ff",  // texto principal — casi blanco azulado
                    DarkTextLight = "#8ca5f5",  // texto secundario — medio-claro
                    DarkBorderLight = "#2a3a7a",  // bordes — apenas visibles
                },

                ["bosque"] = new UsuarioTema
                {
                    // Light — sin cambios
                    PrimaryBlue = "#2d6a4f",
                    PrimaryBlueHover = "#1a3e2e",
                    SecondaryBlue = "#40916c",
                    LightBlue = "#b7e4c7",
                    VeryLightBlue = "#f1f8f4",
                    DarkBlueLight = "#1a3e2e",
                    AccentBlue = "#52b788",
                    BgLight = "#ffffff",
                    BgGray = "#f1f8f4",
                    TextDark = "#0d2b1e",
                    TextLight = "#4a7c63",
                    BorderLight = "#d8f3dc",

                    // Dark — corregido
                    DarkPrimaryBlue = "#52b788",  // verde medio-claro
                    DarkBlueHover = "#74c69d",
                    DarkSecondaryBlue = "#40916c",
                    DarkLightBlue = "#1b4332",
                    DarkVeryLightBlue = "#2d6a4f",
                    DarkDarkBlue = "#b7e4c7",
                    DarkAccentBlue = "#95d5b2",
                    DarkBgLight = "#071a10",
                    DarkBgGray = "#0f2d1c",
                    DarkTextDark = "#e8f5ed",
                    DarkTextLight = "#74c69d",
                    DarkBorderLight = "#1b4332",
                },

                ["amanecer"] = new UsuarioTema
                {
                    // Light — sin cambios
                    PrimaryBlue = "#c05621",
                    PrimaryBlueHover = "#7b3311",
                    SecondaryBlue = "#dd6b20",
                    LightBlue = "#fbd38d",
                    VeryLightBlue = "#fff8f3",
                    DarkBlueLight = "#7b3311",
                    AccentBlue = "#ed8936",
                    BgLight = "#ffffff",
                    BgGray = "#fff8f3",
                    TextDark = "#2d1a08",
                    TextLight = "#9c4221",
                    BorderLight = "#feebc8",

                    // Dark — corregido
                    DarkPrimaryBlue = "#ed8936",  // naranja brillante sobre fondo oscuro
                    DarkBlueHover = "#f6a860",
                    DarkSecondaryBlue = "#dd6b20",
                    DarkLightBlue = "#7b3311",
                    DarkVeryLightBlue = "#a04020",
                    DarkDarkBlue = "#fbd38d",
                    DarkAccentBlue = "#f6ad55",
                    DarkBgLight = "#1a0d04",
                    DarkBgGray = "#2b160a",
                    DarkTextDark = "#fff3e6",
                    DarkTextLight = "#f6a860",
                    DarkBorderLight = "#5c2a0e",
                },

                ["oceano"] = new UsuarioTema
                {
                    // Light — sin cambios
                    PrimaryBlue = "#0c4a6e",
                    PrimaryBlueHover = "#062d47",
                    SecondaryBlue = "#075985",
                    LightBlue = "#7dd3fc",
                    VeryLightBlue = "#f0f9ff",
                    DarkBlueLight = "#062d47",
                    AccentBlue = "#0284c7",
                    BgLight = "#ffffff",
                    BgGray = "#f0f9ff",
                    TextDark = "#082f49",
                    TextLight = "#075985",
                    BorderLight = "#bae6fd",

                    // Dark — corregido
                    DarkPrimaryBlue = "#38bdf8",  // celeste brillante
                    DarkBlueHover = "#7dd3fc",
                    DarkSecondaryBlue = "#0ea5e9",
                    DarkLightBlue = "#0c3554",
                    DarkVeryLightBlue = "#0c4a6e",
                    DarkDarkBlue = "#bae6fd",
                    DarkAccentBlue = "#7dd3fc",
                    DarkBgLight = "#040e1a",
                    DarkBgGray = "#071a2e",
                    DarkTextDark = "#e0f2fe",
                    DarkTextLight = "#7dd3fc",
                    DarkBorderLight = "#0c4a6e",
                },

                ["anochecer"] = new UsuarioTema
                {
                    // Light — sin cambios
                    PrimaryBlue = "#6b21a8",
                    PrimaryBlueHover = "#3b0764",
                    SecondaryBlue = "#7e22ce",
                    LightBlue = "#d8b4fe",
                    VeryLightBlue = "#fdf4ff",
                    DarkBlueLight = "#3b0764",
                    AccentBlue = "#a855f7",
                    BgLight = "#ffffff",
                    BgGray = "#fdf4ff",
                    TextDark = "#2e1065",
                    TextLight = "#7c3aed",
                    BorderLight = "#e9d5ff",

                    // Dark — corregido
                    DarkPrimaryBlue = "#c084fc",  // violeta claro
                    DarkBlueHover = "#d8b4fe",
                    DarkSecondaryBlue = "#a855f7",
                    DarkLightBlue = "#4a1272",
                    DarkVeryLightBlue = "#6b21a8",
                    DarkDarkBlue = "#e9d5ff",
                    DarkAccentBlue = "#d8b4fe",
                    DarkBgLight = "#0f0520",
                    DarkBgGray = "#1a0a35",
                    DarkTextDark = "#f5eeff",
                    DarkTextLight = "#c084fc",
                    DarkBorderLight = "#4a1272",
                },

                ["carbon"] = new UsuarioTema
                {
                    // Light — sin cambios
                    PrimaryBlue = "#1a1a2e",
                    PrimaryBlueHover = "#0d0d1a",
                    SecondaryBlue = "#16213e",
                    LightBlue = "#334155",
                    VeryLightBlue = "#f8f9fa",
                    DarkBlueLight = "#0d0d1a",
                    AccentBlue = "#e94560",
                    BgLight = "#ffffff",
                    BgGray = "#f8f9fa",
                    TextDark = "#0f172a",
                    TextLight = "#475569",
                    BorderLight = "#e2e8f0",

                    // Dark — carbón tiene lógica invertida, el fondo ya es la identidad
                    DarkPrimaryBlue = "#e94560",  // el carmesí ES el primario en dark
                    DarkBlueHover = "#f1647a",
                    DarkSecondaryBlue = "#f1647a",
                    DarkLightBlue = "#2a2a45",
                    DarkVeryLightBlue = "#1a1a2e",
                    DarkDarkBlue = "#e2e8f0",
                    DarkAccentBlue = "#f1647a",
                    DarkBgLight = "#0d0d1a",
                    DarkBgGray = "#1a1a2e",
                    DarkTextDark = "#f1f5f9",
                    DarkTextLight = "#94a3b8",
                    DarkBorderLight = "#2a2a45",
                },
            };
    }
}


