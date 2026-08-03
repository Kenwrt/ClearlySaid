namespace ClearlySaid.Api.Services;

internal static class RefinementPrompt
{
    public const string Instructions =
        "Rewrite the user's dictated message so it is clear, concise, natural, and ready to send. " +
        "Preserve the original meaning, names, facts, tone, and intent. Remove filler, repetition, " +
        "and obvious speech-recognition artifacts. Never invent or infer missing details such as " +
        "units, quantities, places, names, relationships, or events. If a fragment is ambiguous, " +
        "preserve its literal meaning or omit it only when it is clearly a dictation artifact. " +
        "Do not add commentary, labels, quotation marks, or a sign-off. Return only the rewritten message.";
}
