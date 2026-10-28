using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace EnDetailer.Ui;

/// <summary>
/// Begrüßung nach der Installation: Zweck, erste Schritte, die wichtigsten Einstellungen und die Abhängigkeiten.
/// Unten stehen Knöpfe für das Hauptfenster und das Changelog.
/// </summary>
public sealed class WelcomeWindow : Window
{
    private readonly WelcomeContent content;
    private readonly Dependencies dependencies;
    private readonly Action openMain;
    private readonly Action openChangelog;
    private readonly Action<string> saveLanguage;
    private Theme.Scope? themeScope;

    /// <param name="openMain">Öffnet das Hauptfenster des Plugins.</param>
    /// <param name="saveLanguage">Speichert die im Sprachschalter gewählte Sprache ("de" oder "en").</param>
    public WelcomeWindow(WelcomeContent content, Dependencies dependencies, Action openMain, Action openChangelog, Action<string> saveLanguage)
        : base($"{content.Name} - Welcome###{content.Name}Welcome")
    {
        this.content = content;
        this.dependencies = dependencies;
        this.openMain = openMain;
        this.openChangelog = openChangelog;
        this.saveLanguage = saveLanguage;

        Size = new Vector2(620, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 360), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };

        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
    }

    public override void PreDraw()
    {
        // Der Titel folgt der Sprache; die Kennung hinter ### hält die gespeicherte Lage.
        WindowName = $"{content.Name} - {Loc.T("Welcome", "Willkommen")}###{content.Name}Welcome";
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
        ImGui.TextColored(Theme.AccentLight, content.Name);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Muted, content.Version);
        ImGui.SameLine(ImGui.GetContentRegionMax().X - Widgets.LanguagePillWidth());
        Widgets.LanguagePill(saveLanguage);
        ImGui.Dummy(new Vector2(0, 4));

        var footer = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2;
        ImGui.BeginChild("welcomeBody", new Vector2(0, -footer));
        DrawBody();
        ImGui.EndChild();

        DrawFooter();
    }

    private void DrawBody()
    {
        Widgets.SectionHeader(Loc.T("What it does", "Was es macht"));
        Widgets.Card("purpose", width =>
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(content.Purpose.ToString());
            ImGui.PopTextWrapPos();
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader(Loc.T("First steps", "Erste Schritte"));
        Widgets.Card("steps", width =>
        {
            for (var i = 0; i < content.Steps.Count; i++)
            {
                ImGui.TextColored(Theme.AccentLight, $"{i + 1}.");
                ImGui.SameLine();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 24);
                ImGui.TextUnformatted(content.Steps[i].ToString());
                ImGui.PopTextWrapPos();
            }
        });

        ImGui.Dummy(new Vector2(0, 4));
        Widgets.SectionHeader(Loc.T("Key settings", "Wichtigste Einstellungen"));
        Widgets.Card("settings", content.DrawSettings);

        if (dependencies.Groups.Count == 0)
            return;

        ImGui.Dummy(new Vector2(0, 4));
        DependenciesView.Draw(dependencies);
    }

    private void DrawFooter()
    {
        if (Widgets.PrimaryButton(Loc.T($"Open {content.Name}", $"{content.Name} öffnen"), FontAwesomeIcon.ArrowRight))
        {
            openMain();
            IsOpen = false;
        }

        ImGui.SameLine();
        if (ImGui.Button("Changelog"))
            openChangelog();
    }
}
