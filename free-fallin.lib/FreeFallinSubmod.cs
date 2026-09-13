using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;

namespace MeowSci.FreeFallinLib;

public sealed partial class FreeFallinSubmod : ISubmod
{
    public string Name => "Free Fallin - Parachute Customizer";
    public string Tooltip => "Customize the texture, tint, roughness, metallicness, and AO of every parachute canopy.";

    private readonly PngFileBrowser _browser = new("free_fallin", "Import Parachute PNG");
    private string[] _textures = Array.Empty<string>();
    private int _selectedTexture = -1;
    private int _textureMode;
    private float4 _tint = float4.One;
    private float _brightness = 1f;
    private float _decalScale = 0.45f;
    private float _fullCanopyRotation;
    private bool _useStockPbrMap = true;
    private float _ambientOcclusion = 1f;
    private float _roughness = 1f;
    private float _metallic = 1f;
    private string _message = "Stock parachute rendering is active.";
    private bool _messageIsError;

    public void Initialize()
    {
        PngLibrary.EnsureDir();
        Rescan();
    }

    public void Update(double dt) { }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##free_fallin_content");
        ImGui.TextWrapped("One material is applied to every deployed canopy, including parachutes that deploy later. " +
                          "Tint multiplies the stock or custom albedo while preserving the canopy's cloth normal map.");
        ImGui.Spacing();

        RenderTextureSection();
        ImGui.Spacing();
        RenderPbrSection();
        ImGui.Spacing();
        RenderActions();
        SubmodUI.EndContentArea();
    }

    public void RenderFloatingWindows() => _browser.Render(Import);

    public void Dispose() => FreeFallinPatches.RestoreStock();

    private void RenderTextureSection()
    {
        ImGui.SeparatorText("Canopy appearance");
        if (BeginForm("##ff_texture_mode"))
        {
            Label("Texture");
            ImGui.RadioButton("Stock##ff_stock", ref _textureMode, (int)CanopyTextureMode.Stock);
            ImGui.SameLine(0f, 10f);
            ImGui.RadioButton("Replace##ff_replace", ref _textureMode, (int)CanopyTextureMode.Replace);
            ImGui.SameLine(0f, 10f);
            ImGui.RadioButton("Full canopy##ff_full", ref _textureMode, (int)CanopyTextureMode.FullCanopy);
            ImGui.SameLine(0f, 10f);
            ImGui.RadioButton("Center decal##ff_decal", ref _textureMode, (int)CanopyTextureMode.CenterDecal);

            Label("PNG library");
            ImGui.SetNextItemWidth(-1f);
            string preview = _selectedTexture >= 0 && _selectedTexture < _textures.Length ? _textures[_selectedTexture] : "Select a PNG...";
            if (ImGui.BeginCombo("##ff_texture", preview))
            {
                for (int i = 0; i < _textures.Length; i++)
                {
                    bool selected = i == _selectedTexture;
                    if (ImGui.Selectable($"{_textures[i]}##ff_tex_{i}", selected)) _selectedTexture = i;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            EndForm();
        }

        if (ImGui.Button(" Import PNG... ##ff_browse")) _browser.Open();
        ImGui.SameLine(0f, 8f);
        if (ImGui.Button(" Rescan PNGs ##ff_rescan")) Rescan();
        ImGui.SameLine(0f, 10f);
        ImGui.TextDisabled(_textures.Length == 1 ? "1 shared PNG" : $"{_textures.Length} shared PNGs");

        if (BeginForm("##ff_color"))
        {
            Label("Tint");
            ImGui.ColorEdit4("##ff_tint", ref _tint, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.NoAlpha);
            Label("Brightness");
            ImGui.SetNextItemWidth(-1f);
            ImGui.DragFloat("##ff_brightness", ref _brightness, 0.01f, 0f, 4f, "%.2fx");
            if (_textureMode == (int)CanopyTextureMode.CenterDecal)
            {
                Label("Decal size");
                ImGui.SetNextItemWidth(-1f);
                ImGui.DragFloat("##ff_decal_scale", ref _decalScale, 0.01f, 0.05f, 1f, "%.2f");
                ImGui.SetItemTooltip("Fraction of the stock albedo's width/height available to the centered PNG.");
            }
            else if (_textureMode == (int)CanopyTextureMode.FullCanopy)
            {
                Label("Rotation");
                ImGui.SetNextItemWidth(-1f);
                ImGui.DragFloat("##ff_full_rotation", ref _fullCanopyRotation, 1f, -180f, 180f, "%.0f deg");
                ImGui.SetItemTooltip("Rotates the cohesive image around the canopy apex.");
            }
            EndForm();
        }

        if (_textureMode == (int)CanopyTextureMode.Stock)
            ImGui.TextDisabled("Stock keeps the original panel pattern; tint recolors it multiplicatively.");
        else if (_textureMode == (int)CanopyTextureMode.Replace)
            ImGui.TextDisabled("Replace tiles the PNG through the canopy's authored panel UVs.");
        else if (_textureMode == (int)CanopyTextureMode.FullCanopy)
            ImGui.TextDisabled("Full canopy projects the PNG once across the complete circular canopy.");
        else
            ImGui.TextDisabled("Center decal alpha-composites the PNG over the stock albedo, then maps it with the cloth UVs.");
    }

    private void RenderPbrSection()
    {
        ImGui.SeparatorText("Physically based material");
        ImGui.Checkbox("Preserve stock AO / roughness / metallic map##ff_stock_pbr", ref _useStockPbrMap);
        ImGui.SetItemTooltip(_useStockPbrMap
            ? "The values below multiply the stock texture channels, preserving woven/panel variation."
            : "The values below replace the stock map with uniform physical properties.");

        float max = _useStockPbrMap ? 4f : 1f;
        string suffix = _useStockPbrMap ? "%.2fx" : "%.2f";
        if (BeginGrid("##ff_pbr_grid"))
        {
            GridDrag("Ambient occlusion", "##ff_ao", ref _ambientOcclusion, max, suffix);
            GridDrag("Roughness", "##ff_rough", ref _roughness, max, suffix);
            GridDrag("Metallic", "##ff_metal", ref _metallic, max, suffix);
            ImGui.TableNextColumn(); ImGui.TableNextColumn();
            EndGrid();
        }
        ImGui.TextDisabled(_useStockPbrMap
            ? "Multipliers: 1.00 preserves the authored material. A zero stock metallic channel remains non-metallic."
            : "Uniform mode allows truly metallic fabric; 0 = dielectric, 1 = fully metallic.");
    }

    private void RenderActions()
    {
        if (ImGui.Button(" Apply to All Parachutes ##ff_apply")) Apply();
        ImGui.SameLine(0f, 8f);
        if (ImGui.Button(" Restore Stock ##ff_restore"))
        {
            FreeFallinPatches.RestoreStock();
            _message = "Stock parachute rendering restored.";
            _messageIsError = false;
        }
        ImGui.SameLine(0f, 8f);
        if (ImGui.Button(" Reset Controls ##ff_reset")) ResetControls();

        ImGui.Spacing();
        if (_messageIsError) ImGui.TextColored(new float4(1f, .3f, .3f, 1f), _message);
        else ImGui.TextColored(new float4(.45f, .85f, .55f, 1f), _message);
    }

    private void Apply()
    {
        try
        {
            string? texture = _selectedTexture >= 0 && _selectedTexture < _textures.Length ? _textures[_selectedTexture] : null;
            CanopyMaterialController.Apply(new CanopyMaterialSettings
            {
                TextureMode = (CanopyTextureMode)_textureMode,
                TextureName = texture,
                Tint = _tint,
                Brightness = Math.Clamp(_brightness, 0f, 4f),
                DecalScale = Math.Clamp(_decalScale, .05f, 1f),
                FullCanopyRotationDegrees = Math.Clamp(_fullCanopyRotation, -180f, 180f),
                UseStockPbrMap = _useStockPbrMap,
                AmbientOcclusion = Math.Clamp(_ambientOcclusion, 0f, _useStockPbrMap ? 4f : 1f),
                Roughness = Math.Clamp(_roughness, 0f, _useStockPbrMap ? 4f : 1f),
                Metallic = Math.Clamp(_metallic, 0f, _useStockPbrMap ? 4f : 1f)
            });
            _message = "Applied globally. Deploy a parachute to preview it.";
            _messageIsError = false;
        }
        catch (Exception ex)
        {
            _message = ex.Message;
            _messageIsError = true;
            Console.WriteLine($"free-fallin: apply failed: {ex.Message}");
        }
    }

    private void Import(string imported)
    {
        Rescan();
        _selectedTexture = Array.FindIndex(_textures, name => string.Equals(name, imported, StringComparison.OrdinalIgnoreCase));
        _message = $"Imported {imported} into the shared PNG library.";
        _messageIsError = false;
    }

    private void Rescan()
    {
        string? selected = _selectedTexture >= 0 && _selectedTexture < _textures.Length ? _textures[_selectedTexture] : null;
        _textures = PngLibrary.Scan();
        _selectedTexture = selected == null ? (_textures.Length > 0 ? 0 : -1) : Array.FindIndex(_textures,
            name => string.Equals(name, selected, StringComparison.OrdinalIgnoreCase));
        if (_selectedTexture < 0 && _textures.Length > 0) _selectedTexture = 0;
    }

    private void ResetControls()
    {
        _textureMode = 0; _tint = float4.One; _brightness = 1f; _decalScale = .45f; _fullCanopyRotation = 0f;
        _useStockPbrMap = true; _ambientOcclusion = 1f; _roughness = 1f; _metallic = 1f;
    }

    private static bool BeginForm(string id)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX)) return true;
        ImGui.PopStyleVar(); return false;
    }
    private static void EndForm() { ImGui.EndTable(); ImGui.PopStyleVar(); }
    private static void Label(string text) { ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text(text); ImGui.TableNextColumn(); }
    private static bool BeginGrid(string id)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable(id, 4, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX)) return true;
        ImGui.PopStyleVar(); return false;
    }
    private static void EndGrid() { ImGui.EndTable(); ImGui.PopStyleVar(); }
    private static void GridDrag(string label, string id, ref float value, float max, string format)
    {
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text(label);
        ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f); ImGui.DragFloat(id, ref value, .01f, 0f, max, format);
    }
}
