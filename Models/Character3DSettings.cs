namespace TaikoDiveLauncher.Models;

public sealed record Character3DSettings
{
    public bool Enabled { get; set; }
    public string ModelsPath { get; set; } = "Models/Donchan";
    public bool UseCostume { get; set; }
    public string Head { get; set; } = "0";
    public string Body { get; set; } = "0";
    public string Costume { get; set; } = "0";
    public string BodyColor { get; set; } = "#00A7BE";
    public string LimbsColor { get; set; } = "#FFF6DE";
    public string FaceColor { get; set; } = "#FF4125";
    public string RimColor { get; set; } = "#FFF6DE";
}

public sealed record CostumeOption(string Id, string Label, string? IconPath = null);
