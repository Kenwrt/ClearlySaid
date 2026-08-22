using ClearlySaid.Shared.Models;
using System.Text.RegularExpressions;

namespace ClearlySaid.Api.Services;

internal static class RefinementPrompt
{
    private static readonly Regex DisallowedDashPattern = new(
        @"\s*[\u2013\u2014]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string BaseInstructions =
        "Rewrite the user's dictated message so it is clear, concise, natural, and ready to send. " +
        "Preserve the original meaning, names, facts, tone, and intent. Remove filler, repetition, " +
        "and obvious speech-recognition artifacts. Never invent or infer missing details such as " +
        "units, quantities, places, names, relationships, or events. If a fragment is ambiguous, " +
        "preserve its literal meaning or omit it only when it is clearly a dictation artifact. " +
        "Do not add commentary, labels, quotation marks, or a sign-off. " +
        "Never use em dashes or en dashes. Use commas, periods, or parentheses instead. " +
        "Return only the rewritten message.";

    public static string BuildInstructions(MessageStyleOptions? style) => style is null
        ? BaseInstructions
        : $"{BaseInstructions} The user selected these style constraints: " +
          $"{MessageStyleCatalog.BuildInstructions(style)} Apply them without changing facts or intent.";

    public static string NormalizeOutput(string output) =>
        DisallowedDashPattern.Replace(output, ", ").Trim();
}
