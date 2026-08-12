namespace ClearlySaid.Shared.Models;

public sealed record MessageStyleOptions(
    string? Purpose,
    string? Tone,
    string? Directness);

public sealed record MessageStyleOption(
    string Id,
    string Label,
    string Instruction);

public static class MessageStyleCatalog
{
    public const string PreservePurpose = "preserve";
    public const string PreserveTone = "preserve";
    public const string BalancedDirectness = "balanced";

    public static IReadOnlyList<MessageStyleOption> Purposes { get; } =
    [
        new(PreservePurpose, "Keep original purpose", "Preserve the message's original purpose."),
        new("request", "Request", "Make the message an effective request with a clear ask."),
        new("follow-up", "Follow-up", "Frame the message as a clear, natural follow-up."),
        new("persuade", "Persuade", "Make the reasoning persuasive without exaggerating or inventing facts."),
        new("address-issue", "Address an issue", "Address the issue constructively and make the concern clear."),
        new("decline", "Decline", "Communicate the refusal clearly and respectfully."),
        new("set-boundary", "Set a boundary", "State the boundary clearly without adding threats or hostility.")
    ];

    public static IReadOnlyList<MessageStyleOption> Tones { get; } =
    [
        new(PreserveTone, "Keep original tone", "Preserve the message's original tone."),
        new("professional", "Professional", "Use a polished, professional tone."),
        new("warm", "Warm and friendly", "Use a warm, friendly tone."),
        new("casual", "Casual", "Use a relaxed, conversational tone."),
        new("diplomatic", "Diplomatic", "Use a tactful, diplomatic tone."),
        new("firm", "Firm", "Use a calm, firm tone."),
        new("apologetic", "Apologetic", "Use a sincere, appropriately apologetic tone."),
        new("encouraging", "Encouraging", "Use a positive, encouraging tone.")
    ];

    public static IReadOnlyList<MessageStyleOption> DirectnessLevels { get; } =
    [
        new("gentle", "Gentle", "Express the point gently and soften the phrasing where appropriate."),
        new(BalancedDirectness, "Balanced", "Balance clarity with tact."),
        new("direct", "Direct", "State the main point early and use direct, concise wording."),
        new("pointed", "Pointed", "Make the point unmistakable while remaining respectful.")
    ];

    public static bool TryNormalize(
        MessageStyleOptions? value,
        out MessageStyleOptions? normalized)
    {
        normalized = null;
        if (value is null)
        {
            return true;
        }

        var purpose = Find(Purposes, value.Purpose);
        var tone = Find(Tones, value.Tone);
        var directness = Find(DirectnessLevels, value.Directness);
        if (purpose is null || tone is null || directness is null)
        {
            return false;
        }

        normalized = new MessageStyleOptions(purpose.Id, tone.Id, directness.Id);
        return true;
    }

    public static string BuildInstructions(MessageStyleOptions value)
    {
        var purpose = Find(Purposes, value.Purpose)
            ?? throw new ArgumentException("Select a valid message purpose.", nameof(value));
        var tone = Find(Tones, value.Tone)
            ?? throw new ArgumentException("Select a valid message tone.", nameof(value));
        var directness = Find(DirectnessLevels, value.Directness)
            ?? throw new ArgumentException("Select a valid directness level.", nameof(value));

        return $"{purpose.Instruction} {tone.Instruction} {directness.Instruction}";
    }

    private static MessageStyleOption? Find(
        IEnumerable<MessageStyleOption> options,
        string? id) =>
        options.FirstOrDefault(option =>
            string.Equals(option.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
}
