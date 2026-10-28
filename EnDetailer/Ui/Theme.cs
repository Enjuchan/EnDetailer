using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EnDetailer.Ui;

/// <summary>Farben und Stil der En-Plugins: dunkler Grund, Rosa als Akzent.</summary>
public static class Theme
{
    public static readonly Vector4 Background = Hex(0x141016);
    public static readonly Vector4 Header = Hex(0x1A1319);
    public static readonly Vector4 Card = Hex(0x1D1620);
    public static readonly Vector4 CardBorder = Hex(0x33242F);
    public static readonly Vector4 Border = Hex(0x4A3346);
    public static readonly Vector4 Hover = Hex(0x2B1F29);
    public static readonly Vector4 Accent = Hex(0xE8679F);
    public static readonly Vector4 AccentLight = Hex(0xF08CBC);
    public static readonly Vector4 OnAccent = Hex(0x2A0F1C);
    public static readonly Vector4 Text = Hex(0xECE3EA);
    public static readonly Vector4 Muted = Hex(0x9B8C97);
    public static readonly Vector4 Success = Hex(0x7FD394);
    public static readonly Vector4 SuccessBorder = Hex(0x3E6B4A);
    public static readonly Vector4 Warning = Hex(0xE3B04F);
    public static readonly Vector4 WarningBorder = Hex(0x6B5530);
    public static readonly Vector4 Danger = Hex(0xE5707A);
    public static readonly Vector4 DangerBorder = Hex(0x6B343A);

    public const float Rounding = 7f;
    public const float CardRounding = 8f;

    private static readonly (ImGuiCol Slot, Vector4 Color)[] Colors =
    [
        (ImGuiCol.WindowBg, Background),
        (ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0)),
        (ImGuiCol.PopupBg, Card),
        (ImGuiCol.Border, CardBorder),
        (ImGuiCol.TitleBg, Header),
        (ImGuiCol.TitleBgActive, Header),
        (ImGuiCol.TitleBgCollapsed, Header),
        (ImGuiCol.Text, Text),
        (ImGuiCol.TextDisabled, Muted),
        (ImGuiCol.FrameBg, Hover),
        (ImGuiCol.FrameBgHovered, Hex(0x34252F)),
        (ImGuiCol.FrameBgActive, Hex(0x3D2B38)),
        (ImGuiCol.Button, Hover),
        (ImGuiCol.ButtonHovered, Hex(0x3D2B38)),
        (ImGuiCol.ButtonActive, Hex(0x4A3346)),
        (ImGuiCol.Header, Hover),
        (ImGuiCol.HeaderHovered, Hex(0x34252F)),
        (ImGuiCol.HeaderActive, Hex(0x3D2B38)),
        (ImGuiCol.CheckMark, Accent),
        (ImGuiCol.SliderGrab, Accent),
        (ImGuiCol.SliderGrabActive, AccentLight),
        (ImGuiCol.Separator, CardBorder),
        (ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0)),
        (ImGuiCol.ScrollbarGrab, Border),
        (ImGuiCol.ScrollbarGrabHovered, Hex(0x5C4057)),
        (ImGuiCol.ScrollbarGrabActive, Accent),
        (ImGuiCol.TableHeaderBg, Header),
        (ImGuiCol.TableBorderStrong, CardBorder),
        (ImGuiCol.TableBorderLight, CardBorder),
        (ImGuiCol.TableRowBg, new Vector4(0, 0, 0, 0)),
        (ImGuiCol.Tab, Hover),
        (ImGuiCol.TabHovered, Hex(0x3D2B38)),
        (ImGuiCol.TabActive, Hex(0x5C2A43)),
        (ImGuiCol.TabUnfocused, Hover),
        (ImGuiCol.TabUnfocusedActive, Hex(0x3D2B38)),
        (ImGuiCol.ResizeGrip, Border),
        (ImGuiCol.ResizeGripHovered, Hex(0x5C4057)),
        (ImGuiCol.ResizeGripActive, Accent),
        (ImGuiCol.TableRowBgAlt, Hex(0x1A1319)),
        (ImGuiCol.TextSelectedBg, Hex(0x5C2A43)),
    ];

    /// <summary>Setzt Farben und Abstände für ein Fenster; am Ende des Fensters wieder freigeben.</summary>
    public static Scope Push()
    {
        foreach (var (slot, color) in Colors)
            ImGui.PushStyleColor(slot, color);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, CardRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, CardRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 14));

        return new Scope(Colors.Length, 10);
    }

    /// <summary>
    /// Packt eine Farbe als ABGR wie ImGui, aber ohne Aufruf in die native Bibliothek; der Meter ruft das
    /// hunderte Male je Frame auf. Eine globale Stil-Deckkraft wird dabei nicht angewendet, sie ist hier immer 1.
    /// </summary>
    public static uint U32(Vector4 color) => Pack(color.X, color.Y, color.Z, color.W);

    public static uint Pack(float r, float g, float b, float a) =>
        (Channel(a) << 24) | (Channel(b) << 16) | (Channel(g) << 8) | Channel(r);

    private static uint Channel(float value) => (uint)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

    public static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public readonly struct Scope(int colors, int vars) : IDisposable
    {
        public void Dispose()
        {
            ImGui.PopStyleVar(vars);
            ImGui.PopStyleColor(colors);
        }
    }
}
