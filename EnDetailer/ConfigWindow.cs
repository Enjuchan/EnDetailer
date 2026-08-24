using System;
using Dalamud.Bindings.ImGui;
using EnDetailer.Core;

namespace EnDetailer;

public sealed class ConfigWindow(Configuration config, EncounterTracker encounters)
{
    public bool Visible;

    public void Draw()
    {
        if (!this.Visible)
            return;

        if (!ImGui.Begin("EnDetailer Settings", ref this.Visible, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Current DPS calculation");
        var method = (int)config.DpsMethod;
        var methodChanged = ImGui.RadioButton("flat window", ref method, (int)DpsMethod.FlatWindow);
        ImGui.SameLine();
        methodChanged |= ImGui.RadioButton("weighted", ref method, (int)DpsMethod.Weighted);

        if (methodChanged)
        {
            config.DpsMethod = (DpsMethod)method;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Flat: every second in the window counts the same. A hit jumps\n" +
                "into the value when it lands and out again when it expires.\n\n" +
                "Weighted: recent damage counts more, older damage fades out. No\n" +
                "more steps, and identical results on steady damage.");
        }

        var window = config.RollingWindowSeconds;
        if (ImGui.SliderInt("Rolling window (seconds)", ref window, 5, 120))
        {
            config.RollingWindowSeconds = window;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Time span the current DPS is averaged over.\n\n" +
                "10s: single abilities are visible, very jumpy.\n" +
                "30s: one rotation. Calmer, still reacts quickly to downtime.\n" +
                "60s: a single big hit barely shows,\n" +
                "     standing still shows immediately.\n" +
                "120s: a full burst cycle. Very calm, but sluggish.\n\n" +
                "A burst cycle in FFXIV lasts two minutes.");
        }

        ImGui.TextDisabled("Presets:");
        foreach (var preset in new[] { 10, 30, 60, 120 })
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"{preset}s"))
            {
                config.RollingWindowSeconds = preset;
                config.Save();
            }
        }

        var grace = config.GraceSeconds;
        if (ImGui.SliderInt("Grace period before combat ends (seconds)", ref grace, 5, 60))
        {
            config.GraceSeconds = grace;
            encounters.GracePeriod = TimeSpan.FromSeconds(grace);
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "How long combat must stay quiet before the encounter ends.\n" +
                "Cutscenes do not count - they keep the fight alive.\n" +
                "Raise this if a fight still gets cut short.");
        }

        var zone = config.ResetOnZoneChange;
        if (ImGui.Checkbox("Reset on zone change", ref zone))
        {
            config.ResetOnZoneChange = zone;
            encounters.ResetOnZoneChange = zone;
            config.Save();
        }

        var endIinact = config.EndIinactEncounter;
        if (ImGui.Checkbox("End IINACT encounter as well", ref endIinact))
        {
            config.EndIinactEncounter = endIinact;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Ends IINACTs encounter once EnDetailer considers the fight over.\n" +
                "Keeps both in sync and cuts logs cleanly for FFLogs.\n" +
                "This prints a local echo line 'end' to your chat.");
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Appearance");

        var titleBar = config.ShowTitleBar;
        if (ImGui.Checkbox("Show title bar", ref titleBar))
        {
            config.ShowTitleBar = titleBar;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Without a title bar the window reads as an overlay.\n" +
                "Move it by dragging inside the window,\n" +
                "toggle it with /endetailer.");
        }

        var accent = new System.Numerics.Vector4(
            config.AccentColor[0], config.AccentColor[1], config.AccentColor[2], config.AccentColor[3]);

        if (ImGui.ColorEdit4("Accent colour", ref accent, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            config.AccentColor = [accent.X, accent.Y, accent.Z, accent.W];
            config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Colours the timer, column headers, divider and total.");

        var glassEdge = config.GlassEdge;
        if (ImGui.Checkbox("Glass edge", ref glassEdge))
        {
            config.GlassEdge = glassEdge;
            config.Save();
        }

        ImGui.SameLine();
        var glassGradient = config.GlassGradient;
        if (ImGui.Checkbox("Gradient", ref glassGradient))
        {
            config.GlassGradient = glassGradient;
            config.Save();
        }

        var rounding = config.BarRounding;
        if (ImGui.SliderFloat("Corner rounding", ref rounding, 0f, 8f, "%.0f"))
        {
            config.BarRounding = rounding;
            config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Applies to bars and window corners. 0 looks sharper.");

        var gloss = config.BarGloss;
        if (ImGui.Checkbox("Bar gloss", ref gloss))
        {
            config.BarGloss = gloss;
            config.Save();
        }

        ImGui.SameLine();
        var glow = config.BarGlow;
        if (ImGui.Checkbox("Leading edge glow", ref glow))
        {
            config.BarGlow = glow;
            config.Save();
        }

        var stripes = config.RowStripes;
        if (ImGui.Checkbox("Alternating rows", ref stripes))
        {
            config.RowStripes = stripes;
            config.Save();
        }

        ImGui.SameLine();
        var highlightSelf = config.HighlightSelf;
        if (ImGui.Checkbox("Highlight own row", ref highlightSelf))
        {
            config.HighlightSelf = highlightSelf;
            config.Save();
        }

        var locked = config.Locked;
        if (ImGui.Checkbox("Lock window", ref locked))
        {
            config.Locked = locked;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Locked: no title bar, fixed in place and click-through.\n" +
                "In combat you hit the boss instead of the window.\n" +
                "Unlock again to sort or move it.\n" +
                "Also toggles with /endetailer lock.");
        }

        var alpha = config.BackgroundAlpha;
        if (ImGui.SliderFloat("Background opacity", ref alpha, 0f, 1f, "%.2f"))
        {
            config.BackgroundAlpha = alpha;
            config.Save();
        }

        var barAlpha = (int)config.BarAlpha;
        if (ImGui.SliderInt("Bar opacity", ref barAlpha, 40, 255))
        {
            config.BarAlpha = (byte)barAlpha;
            config.Save();
        }

        var icons = config.ShowJobIcons;
        if (ImGui.Checkbox("Show job icons", ref icons))
        {
            config.ShowJobIcons = icons;
            config.Save();
        }

        var fontScale = config.FontScale;
        if (ImGui.SliderFloat("Font size", ref fontScale, 0.7f, 2.0f, "%.2f"))
        {
            config.FontScale = fontScale;
            config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Bar height and job icons scale along.");

        var valueSmoothing = config.ValueSmoothing;
        if (ImGui.SliderFloat("Number easing", ref valueSmoothing, 0f, 3f, "%.2f s"))
        {
            config.ValueSmoothing = valueSmoothing;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "The number travels to the computed value instead of jumping.\n" +
                "It always gets there - just a moment later.\n" +
                "Nothing is estimated or predicted.\n" +
                "At 0 it snaps as before.");
        }

        var smoothing = config.BarSmoothing;
        if (ImGui.SliderFloat("Bar easing", ref smoothing, 0f, 0.6f, "%.2f s"))
        {
            config.BarSmoothing = smoothing;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "How slowly the bars follow the value.\n" +
                "Affects the bars only - the numbers stay exact.\n" +
                "At 0 they snap instantly.");
        }

        var padding = config.Padding;
        if (ImGui.SliderFloat("Padding", ref padding, 0f, 14f, "%.0f"))
        {
            config.Padding = padding;
            config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Spacing at the window edge and between columns.\nAt 0 everything sits flush.");

        ImGui.TextUnformatted("Bar width");
        var extent = (int)config.BarExtent;
        var changed = ImGui.RadioButton("name only", ref extent, (int)BarExtent.NameOnly);
        ImGui.SameLine();
        changed |= ImGui.RadioButton("through Total", ref extent, (int)BarExtent.ThroughTotal);
        ImGui.SameLine();
        changed |= ImGui.RadioButton("full row", ref extent, (int)BarExtent.FullRow);

        if (changed)
        {
            config.BarExtent = (BarExtent)extent;
            config.Save();
        }

        ImGui.TextUnformatted("Text style");
        var style = (int)config.TextStyle;
        var styleChanged = ImGui.RadioButton("plain", ref style, (int)TextStyle.Plain);
        ImGui.SameLine();
        styleChanged |= ImGui.RadioButton("shadow", ref style, (int)TextStyle.Shadow);
        ImGui.SameLine();
        styleChanged |= ImGui.RadioButton("outline", ref style, (int)TextStyle.Outline);

        if (styleChanged)
        {
            config.TextStyle = (TextStyle)style;
            config.Save();
        }

        var diagnostics = config.ShowDiagnostics;
        if (ImGui.Checkbox("Diagnostics in footer", ref diagnostics))
        {
            config.ShowDiagnostics = diagnostics;
            config.Save();
        }

        ImGui.Separator();
        ImGui.TextDisabled("In IINACT, turn OFF 'End encounter automatically");
        ImGui.TextDisabled("after leaving combat' - EnDetailer decides");
        ImGui.TextDisabled("that on its own.");

        ImGui.Separator();

        // Die AGPL sieht fuer interaktive Programme einen sichtbaren Hinweis auf
        // Urheberschaft, fehlende Gewaehrleistung und Lizenz vor.
        ImGui.TextDisabled("EnDetailer - Copyright (c) 2026 Enjuchan");
        ImGui.TextDisabled("Licensed under GNU AGPL-3.0, without any warranty.");
        ImGui.TextDisabled("github.com/Enjuchan/EnDetailer");

        ImGui.End();
    }
}
