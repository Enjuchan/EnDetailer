using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace EnDetailer.Ui;

/// <summary>Alle Versionen des Plugins, neueste zuerst, jede als Karte; Versionen seit dem letzten Besuch tragen "Neu".</summary>
public sealed class ChangelogWindow : Window
{
    private readonly string pluginName;
    private readonly IReadOnlyList<ChangelogEntry> entries;
    private readonly Func<string, bool> isNew;
    private readonly Action<string> saveLanguage;
    private Theme.Scope? themeScope;

    /// <param name="entries">Neueste zuerst.</param>
    /// <param name="isNew">Ob die Version (Text) seit dem letzten Besuch dazugekommen ist.</param>
    /// <param name="saveLanguage">Speichert die im Sprachschalter gewählte Sprache ("de" oder "en").</param>
    public ChangelogWindow(string pluginName, IReadOnlyList<ChangelogEntry> entries, Func<string, bool> isNew, Action<string> saveLanguage)
        : base($"{pluginName} - Changelog###{pluginName}Changelog")
    {
        this.pluginName = pluginName;
        this.entries = entries;
        this.isNew = isNew;
        this.saveLanguage = saveLanguage;

        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 300), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };

        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
    }

    public override void PreDraw()
    {
        // Der Titel folgt der Sprache; die Kennung hinter ### hält die gespeicherte Lage.
        WindowName = $"{pluginName} - Changelog###{pluginName}Changelog";
        themeScope = Theme.Push();
    }

    public override void PostDraw()
    {
        themeScope?.Dispose();
        themeScope = null;
    }

    public override void Draw()
    {
        Decor.Background();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.AccentLight, pluginName);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Muted, "Changelog");
        ImGui.SameLine(ImGui.GetContentRegionMax().X - Widgets.LanguagePillWidth());
        Widgets.LanguagePill(saveLanguage);
        ImGui.Dummy(new Vector2(0, 4));

        foreach (var entry in entries)
        {
            var fresh = isNew(entry.Version);
            Widgets.Card($"entry{entry.Version}", width =>
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Theme.AccentLight, $"v{entry.Version.TrimStart('v', 'V')}");
                ImGui.SameLine();
                ImGui.TextColored(Theme.Muted, entry.Date);
                if (fresh)
                {
                    ImGui.SameLine();
                    Widgets.Pill(Loc.T("New", "Neu"), Theme.Accent, Theme.AccentLight);
                }

                foreach (var point in entry.Points)
                {
                    ImGui.TextColored(Theme.Muted, "•");
                    ImGui.SameLine();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 16);
                    ImGui.TextUnformatted(point.ToString());
                    ImGui.PopTextWrapPos();
                }
            }, fresh);
            ImGui.Spacing();
        }
    }
}
